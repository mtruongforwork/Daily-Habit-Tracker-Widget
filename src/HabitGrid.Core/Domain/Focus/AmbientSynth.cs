namespace HabitGrid.Core.Domain.Focus;

/// <summary>Các kênh âm thanh nền của bàn trộn trang Focus.</summary>
public enum AmbientKind
{
    White,
    Pink,
    Brown,
    Rain,
    Waves
}

/// <summary>
/// Tạo âm thanh nền bằng code (không dùng file có bản quyền): tiếng ồn trắng/hồng/nâu, mưa (tiếng hồng + hạt mưa lách tách) và sóng
/// (tiếng nâu dâng hạ theo chu kỳ). Mỗi đoạn được nối mép bằng crossfade nên phát lặp liên tục không nghe thấy chỗ nối.
/// Hàm thuần, có hạt giống cố định: cùng tham số luôn ra cùng kết quả (kiểm thử được).
/// </summary>
public static class AmbientSynth
{
    public const int SampleRate = 22050;

    /// <summary>Độ dài một vòng lặp (giây). Chu kỳ sóng chia hết độ dài này để chỗ nối liền mạch.</summary>
    public const int LoopSeconds = 16;

    private const double WavePeriodSeconds = 8;
    private const int FadeSamples = SampleRate / 2;

    /// <summary>Mẫu âm thanh 16-bit mono cho một vòng lặp của kênh <paramref name="kind"/>.</summary>
    public static short[] Generate(AmbientKind kind, int seconds = LoopSeconds, int seed = 7)
    {
        var length = seconds * SampleRate;
        var raw = Raw(kind, length + FadeSamples, new Random(seed + (int)kind));
        var loop = new double[length];

        // Nối mép: đầu đoạn hòa dần vào phần dư sau cuối, nên mẫu cuối nối thẳng vào mẫu đầu.
        for (var i = 0; i < length; i++)
        {
            if (i < FadeSamples)
            {
                var t = (double)i / FadeSamples;
                loop[i] = (raw[i] * t) + (raw[length + i] * (1 - t));
            }
            else
            {
                loop[i] = raw[i];
            }
        }

        Normalize(loop, Level(kind));
        return loop.Select(v => (short)Math.Round(v * short.MaxValue)).ToArray();
    }

    /// <summary>Đường bao âm lượng (RMS, 0..1) theo từng khúc <paramref name="pointsPerSecond"/> lần mỗi giây, để vẽ sóng âm.</summary>
    public static double[] Envelope(short[] samples, int pointsPerSecond = 20)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var block = Math.Max(1, SampleRate / pointsPerSecond);
        var points = new double[Math.Max(1, samples.Length / block)];

        for (var p = 0; p < points.Length; p++)
        {
            double sum = 0;

            for (var i = p * block; i < (p + 1) * block && i < samples.Length; i++)
            {
                var v = samples[i] / (double)short.MaxValue;
                sum += v * v;
            }

            points[p] = Math.Sqrt(sum / block);
        }

        return points;
    }

    /// <summary>Đóng gói mẫu 16-bit mono thành file WAV (PCM) trong bộ nhớ.</summary>
    public static byte[] ToWav(short[] samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Đỉnh âm lượng của từng kênh (để các kênh to nhỏ tương đương khi trộn).</summary>
    private static double Level(AmbientKind kind) => kind switch
    {
        AmbientKind.White => 0.32,
        AmbientKind.Pink => 0.5,
        AmbientKind.Brown => 0.7,
        AmbientKind.Rain => 0.55,
        _ => 0.7
    };

    private static double[] Raw(AmbientKind kind, int count, Random random) => kind switch
    {
        AmbientKind.White => White(count, random),
        AmbientKind.Pink => Pink(count, random),
        AmbientKind.Brown => Brown(count, random),
        AmbientKind.Rain => Rain(count, random),
        _ => Waves(count, random)
    };

    private static double[] White(int count, Random random)
    {
        var data = new double[count];

        for (var i = 0; i < count; i++)
        {
            data[i] = (random.NextDouble() * 2) - 1;
        }

        return data;
    }

    /// <summary>Tiếng ồn hồng (bộ lọc của Paul Kellet): năng lượng giảm 3 dB mỗi quãng tám, êm hơn tiếng trắng.</summary>
    private static double[] Pink(int count, Random random)
    {
        var data = new double[count];
        double b0 = 0, b1 = 0, b2 = 0, b3 = 0, b4 = 0, b5 = 0, b6 = 0;

        for (var i = 0; i < count; i++)
        {
            var white = (random.NextDouble() * 2) - 1;
            b0 = (0.99886 * b0) + (white * 0.0555179);
            b1 = (0.99332 * b1) + (white * 0.0750759);
            b2 = (0.96900 * b2) + (white * 0.1538520);
            b3 = (0.86650 * b3) + (white * 0.3104856);
            b4 = (0.55000 * b4) + (white * 0.5329522);
            b5 = (-0.7616 * b5) - (white * 0.0168980);
            data[i] = b0 + b1 + b2 + b3 + b4 + b5 + b6 + (white * 0.5362);
            b6 = white * 0.115926;
        }

        return data;
    }

    /// <summary>Tiếng ồn nâu: tích phân của tiếng trắng có rò, trầm như tiếng gió hay thác xa.</summary>
    private static double[] Brown(int count, Random random)
    {
        var data = new double[count];
        double last = 0;

        for (var i = 0; i < count; i++)
        {
            var white = (random.NextDouble() * 2) - 1;
            last = (last + (0.02 * white)) / 1.02;
            data[i] = last;
        }

        return data;
    }

    /// <summary>Mưa: nền tiếng hồng nhẹ cộng các hạt mưa (xung ngắn tắt nhanh) rơi ngẫu nhiên.</summary>
    private static double[] Rain(int count, Random random)
    {
        var data = Pink(count, random);

        for (var i = 0; i < count; i++)
        {
            data[i] *= 0.45;
        }

        var drops = count / 180;

        for (var d = 0; d < drops; d++)
        {
            var start = random.Next(count);
            var strength = 0.15 + (random.NextDouble() * 0.5);
            var decay = 60 + random.Next(200);

            for (var k = 0; k < decay * 4 && start + k < count; k++)
            {
                data[start + k] += strength * Math.Exp(-(double)k / decay) * ((random.NextDouble() * 2) - 1);
            }
        }

        return data;
    }

    /// <summary>Sóng: tiếng nâu được nâng lên hạ xuống theo chu kỳ 8 giây như sóng vỗ (chu kỳ chia hết vòng lặp nên nối liền).</summary>
    private static double[] Waves(int count, Random random)
    {
        var data = Brown(count, random);

        for (var i = 0; i < count; i++)
        {
            var phase = 2 * Math.PI * i / (WavePeriodSeconds * SampleRate);
            var swell = 0.25 + (0.75 * Math.Pow((1 - Math.Cos(phase)) / 2, 1.6));
            data[i] *= swell;
        }

        return data;
    }

    private static void Normalize(double[] data, double peak)
    {
        var max = data.Length == 0 ? 0 : data.Max(Math.Abs);

        if (max <= 0)
        {
            return;
        }

        var scale = peak / max;

        for (var i = 0; i < data.Length; i++)
        {
            data[i] *= scale;
        }
    }
}
