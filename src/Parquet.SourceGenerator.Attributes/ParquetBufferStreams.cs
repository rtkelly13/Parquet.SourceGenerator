using System;
using System.Buffers;
using System.IO;
using System.Runtime.InteropServices;

namespace Parquet.SourceGenerator;

/// <summary>
/// Creates independent read-only streams over a byte buffer without copying it.
/// </summary>
/// <remarks>
/// Generated readers call this for every worker in a parallel read: a Parquet reader seeks within its
/// stream, so workers cannot share one, but they can all be given their own cursor over the same bytes.
/// Array-backed buffers become a <see cref="MemoryStream"/>; anything else — notably memory-mapped file
/// pages — is pinned and wrapped in an <see cref="UnmanagedMemoryStream"/> rather than copied out.
/// </remarks>
public static class ParquetBufferStreams
{
    /// <summary>
    /// Wraps <paramref name="buffer"/> as a seekable read-only stream.
    /// </summary>
    /// <param name="buffer">The bytes to expose.</param>
    /// <returns>A stream positioned at the start of the buffer. The caller owns and must dispose it.</returns>
    public static unsafe Stream Create(ReadOnlyMemory<byte> buffer)
    {
        if (MemoryMarshal.TryGetArray(buffer, out ArraySegment<byte> segment))
        {
            return new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false);
        }

        if (buffer.Length == 0)
        {
            // UnmanagedMemoryStream rejects a null pointer, which is what pinning an empty buffer can hand back.
            return new MemoryStream(Array.Empty<byte>(), writable: false);
        }

        MemoryHandle handle;
        try
        {
            handle = buffer.Pin();
        }
        catch (NotSupportedException)
        {
            // A custom MemoryManager is allowed to refuse pinning. Copying is the wrong shape for a
            // large file, but it is better than failing a read that used to work.
            return new MemoryStream(buffer.ToArray(), writable: false);
        }

        try
        {
            return new PinnedBufferStream(handle, (byte*)handle.Pointer, buffer.Length);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>
    /// An <see cref="UnmanagedMemoryStream"/> that releases its pin when disposed.
    /// </summary>
    private sealed unsafe class PinnedBufferStream : UnmanagedMemoryStream
    {
        private MemoryHandle _handle;
        private bool _released;

        internal PinnedBufferStream(MemoryHandle handle, byte* pointer, long length)
            : base(pointer, length)
        {
            _handle = handle;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!_released)
            {
                _released = true;
                _handle.Dispose();
            }
        }
    }
}
