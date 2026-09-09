using System;
using System.Buffers;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace Parquet.SourceGenerator;

/// <summary>
/// Maps a Parquet file into virtual memory and exposes it as a <see cref="ReadOnlyMemory{T}"/> over the
/// mapped pages, so readers can seek within the file without copying it into the managed heap.
/// </summary>
/// <remarks>
/// <para>
/// The generated <c>ReadParquetAsync(FileInfo)</c> family uses this to avoid the intermediate
/// <c>byte[]</c> that <c>File.ReadAllBytes</c> or a buffered <c>FileStream</c> would materialise, and to let
/// parallel row-group workers each hold an independent cursor over the same pages with no locking.
/// </para>
/// <para>
/// The exposed memory is only valid until <see cref="Dispose"/> is called. Reading it afterwards is
/// an access violation, not a managed exception, so never let the memory outlive the instance.
/// </para>
/// <para>
/// On Windows an active mapping keeps a handle on the file: it cannot be deleted or truncated until
/// every mapping over it is disposed. Unix platforms allow unlinking a mapped file.
/// </para>
/// </remarks>
public sealed class ParquetMemoryMappedFile : IDisposable
{
    /// <summary>
    /// Gets the largest file length that can be exposed as a single <see cref="ReadOnlyMemory{T}"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="Memory{T}"/> is indexed by <see cref="int"/>, so a file larger than this cannot be
    /// handed to the buffer overloads as one contiguous span. <see cref="TryOpen(FileInfo)"/> returns
    /// <see langword="null"/> for such files and callers fall back to stream reading.
    /// </remarks>
    public const long MaxMappableLength = int.MaxValue;

    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly ViewMemoryManager _manager;
    private bool _disposed;

    private ParquetMemoryMappedFile(
        MemoryMappedFile file,
        MemoryMappedViewAccessor view,
        ViewMemoryManager manager
    )
    {
        _file = file;
        _view = view;
        _manager = manager;
    }

    /// <summary>
    /// Gets the mapped file contents. Valid only until this instance is disposed.
    /// </summary>
    /// <exception cref="ObjectDisposedException">The mapping has already been disposed.</exception>
    public ReadOnlyMemory<byte> Memory
    {
        get
        {
#if NET8_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ParquetMemoryMappedFile));
            }
#endif

            return _manager.Memory;
        }
    }

    /// <summary>
    /// Gets the length in bytes of the mapped file.
    /// </summary>
    public long Length => _manager.Length;

    /// <summary>
    /// Maps <paramref name="file"/> into memory, or returns <see langword="null"/> when it cannot be
    /// mapped as a single contiguous buffer.
    /// </summary>
    /// <param name="file">The file to map.</param>
    /// <returns>
    /// A live mapping, or <see langword="null"/> when the file is empty (nothing to map) or longer than
    /// <see cref="MaxMappableLength"/>. Callers are expected to fall back to stream reading.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="file"/> is <see langword="null"/>.</exception>
    public static ParquetMemoryMappedFile? TryOpen(FileInfo file)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(file);
#else
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }
#endif

        return TryOpen(file.FullName);
    }

    /// <summary>
    /// Maps the file at <paramref name="path"/> into memory, or returns <see langword="null"/> when it
    /// cannot be mapped as a single contiguous buffer.
    /// </summary>
    /// <param name="path">Path of the file to map.</param>
    /// <returns>
    /// A live mapping, or <see langword="null"/> when the file is empty (nothing to map) or longer than
    /// <see cref="MaxMappableLength"/>. Callers are expected to fall back to stream reading.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static unsafe ParquetMemoryMappedFile? TryOpen(string path)
    {
#if NET8_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(path);
#else
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }
#endif

        // Length is read before mapping because CreateFromFile throws on a zero-length file, and
        // because a file past int.MaxValue cannot be surfaced as a single ReadOnlyMemory<byte>.
        long length = new FileInfo(path).Length;
        if (length <= 0 || length > MaxMappableLength)
        {
            return null;
        }

        MemoryMappedFile? file = null;
        MemoryMappedViewAccessor? view = null;
        try
        {
            file = MemoryMappedFile.CreateFromFile(
                path,
                FileMode.Open,
                mapName: null,
                capacity: 0,
                MemoryMappedFileAccess.Read
            );
            view = file.CreateViewAccessor(0, length, MemoryMappedFileAccess.Read);

            byte* pointer = null;
            view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
            var manager = new ViewMemoryManager(
                view,
                pointer + view.PointerOffset,
                checked((int)length)
            );
            return new ParquetMemoryMappedFile(file, view, manager);
        }
        catch
        {
            view?.Dispose();
            file?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases the view and the mapping. Any memory previously handed out becomes invalid.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _manager.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }

    /// <summary>
    /// Presents the mapped pages as <see cref="ReadOnlyMemory{T}"/> without copying them.
    /// </summary>
    private sealed unsafe class ViewMemoryManager : MemoryManager<byte>
    {
        private readonly MemoryMappedViewAccessor _view;
        private readonly byte* _pointer;
        private readonly int _length;
        private bool _released;

        internal ViewMemoryManager(MemoryMappedViewAccessor view, byte* pointer, int length)
        {
            _view = view;
            _pointer = pointer;
            _length = length;
        }

        internal int Length => _length;

        public override Span<byte> GetSpan() => new Span<byte>(_pointer, _length);

        // The pages are already fixed in the address space, so pinning is a no-op that just hands
        // the caller the mapped address.
        public override MemoryHandle Pin(int elementIndex = 0)
        {
            if (elementIndex < 0 || elementIndex > _length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            }

            return new MemoryHandle(_pointer + elementIndex);
        }

        public override void Unpin() { }

        internal void ReleasePointer()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            _view.SafeMemoryMappedViewHandle.ReleasePointer();
        }

        protected override void Dispose(bool disposing) { }
    }
}
