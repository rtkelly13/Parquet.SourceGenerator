using System;
using System.Linq;
using Xunit;

namespace Parquet.SourceGenerator.Tests;

public sealed class VectorizedColumnTransformsTests
{
    [Fact]
    public void ConstantsMatchExpectedValues()
    {
        Assert.Equal(DateTime.UnixEpoch.Ticks, VectorizedColumnTransforms.UnixEpochTicks);
        Assert.Equal(
            DateTime.UnixEpoch.Ticks | (1L << 62),
            VectorizedColumnTransforms.UtcEpochConstant
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(1001)]
    public void ConvertEpochMicrosecondsToTicksAndDateTimeMatchesScalar(int count)
    {
        var input = new long[count];
        for (int i = 0; i < count; i++)
        {
            // Spread across various ranges: negative, zero, positive
            input[i] = (long)(i - count / 2) * 86_400_000_000L + (i * 12345L);
        }

        var ticksDst = new long[count];
        VectorizedColumnTransforms.ConvertEpochMicrosecondsToTicks(input, ticksDst);

        var dtDst = new DateTime[count];
        VectorizedColumnTransforms.ConvertEpochMicrosecondsToDateTime(input, dtDst);

        for (int i = 0; i < count; i++)
        {
            long expectedTicks = VectorizedColumnTransforms.UnixEpochTicks + input[i] * 10L;
            Assert.Equal(expectedTicks, ticksDst[i]);
            Assert.Equal(DateTimeKind.Utc, dtDst[i].Kind);
            Assert.Equal(expectedTicks, dtDst[i].Ticks);
        }

        // Roundtrip: ticks -> epoch microseconds
        var roundtripMicros = new long[count];
        VectorizedColumnTransforms.ConvertTicksToEpochMicroseconds(ticksDst, roundtripMicros);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(input[i], roundtripMicros[i]);
        }

        // Roundtrip: DateTime -> epoch microseconds
        var roundtripDtMicros = new long[count];
        VectorizedColumnTransforms.ConvertDateTimeToEpochMicroseconds(dtDst, roundtripDtMicros);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(input[i], roundtripDtMicros[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(17)]
    [InlineData(65)]
    [InlineData(500)]
    public void ConvertEpochMillisecondsToTicksAndDateTimeMatchesScalar(int count)
    {
        var input = new long[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = (long)(i - count / 2) * 86_400_000L + (i * 456L);
        }

        var ticksDst = new long[count];
        VectorizedColumnTransforms.ConvertEpochMillisecondsToTicks(input, ticksDst);

        var dtDst = new DateTime[count];
        VectorizedColumnTransforms.ConvertEpochMillisecondsToDateTime(input, dtDst);

        for (int i = 0; i < count; i++)
        {
            long expectedTicks = VectorizedColumnTransforms.UnixEpochTicks + input[i] * 10_000L;
            Assert.Equal(expectedTicks, ticksDst[i]);
            Assert.Equal(DateTimeKind.Utc, dtDst[i].Kind);
            Assert.Equal(expectedTicks, dtDst[i].Ticks);
        }

        // Roundtrip: ticks -> epoch milliseconds
        var roundtripMillis = new long[count];
        VectorizedColumnTransforms.ConvertTicksToEpochMilliseconds(ticksDst, roundtripMillis);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(input[i], roundtripMillis[i]);
        }

        // Roundtrip: DateTime -> epoch milliseconds
        var roundtripDtMillis = new long[count];
        VectorizedColumnTransforms.ConvertDateTimeToEpochMilliseconds(dtDst, roundtripDtMillis);
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(input[i], roundtripDtMillis[i]);
        }
    }

    [Fact]
    public void TimestampConversionsShortDestinationThrowsArgumentException()
    {
        var src = new long[10];
        var shortTicks = new long[9];
        var shortDt = new DateTime[9];

        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertEpochMicrosecondsToTicks(src, shortTicks)
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertEpochMicrosecondsToDateTime(src, shortDt)
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertEpochMillisecondsToTicks(src, shortTicks)
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertEpochMillisecondsToDateTime(src, shortDt)
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertTicksToEpochMicroseconds(src, shortTicks)
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertTicksToEpochMilliseconds(src, shortTicks)
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertDateTimeToEpochMicroseconds(
                new DateTime[10],
                shortTicks
            )
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.ConvertDateTimeToEpochMilliseconds(
                new DateTime[10],
                shortTicks
            )
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(128)]
    [InlineData(513)]
    public void MultiplyScaleDoubleMatchesScalar(int count)
    {
        var input = new double[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = (i - count / 2) * 1.5;
        }

        const double factor = 0.001;
        var destination = new double[count];
        VectorizedColumnTransforms.MultiplyScale(input, factor, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(input[i] * factor, destination[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(65)]
    [InlineData(513)]
    public void MultiplyScaleFloatMatchesScalar(int count)
    {
        var input = new float[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = (i - count / 2) * 2.25f;
        }

        const float factor = 0.5f;
        var destination = new float[count];
        VectorizedColumnTransforms.MultiplyScale(input, factor, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal(input[i] * factor, destination[i]);
        }
    }

    [Fact]
    public void MultiplyScaleShortDestinationThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.MultiplyScale(new double[10], 2.0, new double[9])
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.MultiplyScale(new float[10], 2.0f, new float[9])
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(257)]
    public void WidenIntToLongPreservesValuesAndSign(int count)
    {
        var input = new int[count];
        for (int i = 0; i < count; i++)
        {
            input[i] =
                (i % 2 == 0 ? 1 : -1)
                * (i * 1000 + (i == 0 ? int.MinValue : (i == 1 ? int.MaxValue : 42)));
        }

        var destination = new long[count];
        VectorizedColumnTransforms.Widen(input, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal((long)input[i], destination[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(257)]
    public void WidenShortToIntPreservesValuesAndSign(int count)
    {
        var input = new short[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = (short)(
                (i % 2 == 0 ? 1 : -1)
                * (i * 10 + (i == 0 ? short.MinValue : (i == 1 ? short.MaxValue : 5)))
            );
        }

        var destination = new int[count];
        VectorizedColumnTransforms.Widen(input, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal((int)input[i], destination[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(65)]
    [InlineData(257)]
    public void WidenByteToIntPreservesValues(int count)
    {
        var input = new byte[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = (byte)(i & 0xFF);
        }

        var destination = new int[count];
        VectorizedColumnTransforms.Widen(input, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal((int)input[i], destination[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(257)]
    public void NarrowLongToIntPreservesLower32Bits(int count)
    {
        var input = new long[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = (long)i * 123456789L - 987654321L;
        }

        var destination = new int[count];
        VectorizedColumnTransforms.Narrow(input, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal((int)input[i], destination[i]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(257)]
    public void NarrowIntToShortPreservesLower16Bits(int count)
    {
        var input = new int[count];
        for (int i = 0; i < count; i++)
        {
            input[i] = i * 12345 - 54321;
        }

        var destination = new short[count];
        VectorizedColumnTransforms.Narrow(input, destination);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal((short)input[i], destination[i]);
        }
    }

    [Fact]
    public void WideningAndNarrowingShortDestinationThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.Widen(new int[10], new long[9])
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.Widen(new short[10], new int[9])
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.Widen(new byte[10], new int[9])
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.Narrow(new long[10], new int[9])
        );
        Assert.Throws<ArgumentException>(() =>
            VectorizedColumnTransforms.Narrow(new int[10], new short[9])
        );
    }
}
