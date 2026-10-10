using NAudio.Wave;

namespace MicMind;

/// <summary>
/// Converts captured audio (PCM 16/24/32-bit or 32-bit float, any channel count and rate)
/// into interleaved 32-bit float at the output's channel count and sample rate.
/// </summary>
internal sealed class SampleConverter
{
    private static readonly Guid IeeeFloatSubtype = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly bool inputIsFloat;
    private readonly int bytesPerSample;
    private readonly int inChannels;
    private readonly int outChannels;
    private readonly double step; // input frames per output frame
    private readonly float[] previous; // last input frame, already mapped to output channels
    private double position;
    private float[] frames = Array.Empty<float>();
    private float[] output = Array.Empty<float>();

    public SampleConverter(WaveFormat input, WaveFormat output)
    {
        inputIsFloat = input.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (input is WaveFormatExtensible ext && ext.SubFormat == IeeeFloatSubtype);
        bytesPerSample = input.BitsPerSample / 8;
        if (inputIsFloat ? bytesPerSample != 4 : bytesPerSample is not (2 or 3 or 4))
            throw new NotSupportedException($"Ljudformatet stöds inte: {input}");

        inChannels = input.Channels;
        outChannels = output.Channels;
        step = (double)input.SampleRate / output.SampleRate;
        previous = new float[outChannels];
    }

    /// <summary>Peak level (0..1) of the most recent block.</summary>
    public float Peak { get; private set; }

    /// <summary>Converts a block; returns the number of floats written to <paramref name="result"/>.</summary>
    public int Convert(byte[] data, int byteCount, out float[] result)
    {
        int frameCount = byteCount / (bytesPerSample * inChannels);
        DecodeAndMap(data, frameCount);

        if (step == 1.0)
        {
            result = frames;
            return frameCount * outChannels;
        }

        // Linear interpolation. Index 0 is the last frame of the previous block,
        // index i (1..frameCount) is frame i-1 of this block.
        int maxOut = (int)Math.Ceiling(frameCount / step) + 2;
        if (output.Length < maxOut * outChannels)
            output = new float[maxOut * outChannels];

        int written = 0;
        while (position + 1 <= frameCount)
        {
            int i = (int)position;
            float t = (float)(position - i);
            for (int c = 0; c < outChannels; c++)
            {
                float a = i == 0 ? previous[c] : frames[(i - 1) * outChannels + c];
                float b = frames[i * outChannels + c];
                output[written++] = a + (b - a) * t;
            }
            position += step;
        }
        position -= frameCount;
        if (frameCount > 0)
            Array.Copy(frames, (frameCount - 1) * outChannels, previous, 0, outChannels);

        result = output;
        return written;
    }

    private void DecodeAndMap(byte[] data, int frameCount)
    {
        if (frames.Length < frameCount * outChannels)
            frames = new float[frameCount * outChannels];

        float peak = 0;
        Span<float> input = stackalloc float[inChannels];
        int offset = 0;
        for (int f = 0; f < frameCount; f++)
        {
            float sum = 0;
            for (int c = 0; c < inChannels; c++)
            {
                float s = ReadSample(data, offset);
                offset += bytesPerSample;
                input[c] = s;
                sum += s;
            }
            float mono = sum / inChannels;
            peak = Math.Max(peak, Math.Abs(mono));

            int o = f * outChannels;
            for (int c = 0; c < outChannels; c++)
                frames[o + c] = inChannels == outChannels ? input[c] : mono;
        }
        Peak = Math.Min(1f, peak);
    }

    private float ReadSample(byte[] data, int offset)
    {
        if (inputIsFloat)
            return BitConverter.ToSingle(data, offset);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(data, offset) / 32768f,
            3 => ((data[offset] << 8) | (data[offset + 1] << 16) | (data[offset + 2] << 24)) / 2147483648f,
            _ => BitConverter.ToInt32(data, offset) / 2147483648f,
        };
    }
}
