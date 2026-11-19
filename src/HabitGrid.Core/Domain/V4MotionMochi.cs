using System.Text;

namespace HabitGrid.Core.Domain;

/// <summary>Loại âm thanh phản hồi tổng hợp bằng code (không dùng file có bản quyền).</summary>
public enum SoundToneKind
{
    CheckInPop = 0,
    Increment = 1,
    PerfectDayChime = 2
}

/// <summary>Tổng hợp âm thanh WAV PCM 16-bit mono (22050 Hz) hoàn toàn bằng code.</summary>
public static class SoundSynthesizer
{
    public const int SampleRate = 22050;

    public static byte[] SynthesizeWav(SoundToneKind kind)
    {
        var samples = kind switch
        {
            SoundToneKind.CheckInPop => RenderPop(baseFreq: 520.0, endFreq: 780.0, durationMs: 95, amplitude: 0.28),
            SoundToneKind.Increment => RenderPop(baseFreq: 660.0, endFreq: 880.0, durationMs: 75, amplitude: 0.22),
            SoundToneKind.PerfectDayChime => RenderChime([523.25, 659.25, 783.99, 1046.50], stepMs: 75, tailMs: 280, amplitude: 0.24),
            _ => RenderPop(520.0, 780.0, 95, 0.25)
        };

        return EncodePcm16Wav(samples, SampleRate);
    }

    private static short[] RenderPop(double baseFreq, double endFreq, int durationMs, double amplitude)
    {
        var count = Math.Max(1, SampleRate * durationMs / 1000);
        var buffer = new short[count];
        double phase = 0;

        for (var i = 0; i < count; i++)
        {
            var t = (double)i / count;
            var freq = baseFreq + (endFreq - baseFreq) * t;
            phase += 2.0 * Math.PI * freq / SampleRate;
            var attack = Math.Min(1.0, i / (SampleRate * 0.008));
            var decay = Math.Pow(1.0 - t, 1.8);
            var sample = Math.Sin(phase) * amplitude * attack * decay;
            buffer[i] = (short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return buffer;
    }

    private static short[] RenderChime(double[] notes, int stepMs, int tailMs, double amplitude)
    {
        var stepSamples = SampleRate * stepMs / 1000;
        var tailSamples = SampleRate * tailMs / 1000;
        var total = stepSamples * notes.Length + tailSamples;
        var mix = new double[total];

        for (var n = 0; n < notes.Length; n++)
        {
            var start = n * stepSamples;
            var noteLen = total - start;
            var freq = notes[n];

            for (var i = 0; i < noteLen; i++)
            {
                var t = (double)i / noteLen;
                var attack = Math.Min(1.0, i / (SampleRate * 0.01));
                var decay = Math.Exp(-t * 4.2);
                var wave = Math.Sin(2.0 * Math.PI * freq * i / SampleRate) * 0.8 +
                           Math.Sin(2.0 * Math.PI * freq * 2.0 * i / SampleRate) * 0.2;
                mix[start + i] += wave * amplitude * attack * decay / Math.Sqrt(notes.Length);
            }
        }

        var buffer = new short[total];
        for (var i = 0; i < total; i++)
        {
            buffer[i] = (short)Math.Clamp(mix[i] * short.MaxValue, short.MinValue, short.MaxValue);
        }

        return buffer;
    }

    private static byte[] EncodePcm16Wav(short[] samples, int sampleRate)
    {
        var dataBytes = samples.Length * 2;
        using var ms = new MemoryStream(44 + dataBytes);
        using var bw = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // PCM chunk size
        bw.Write((short)1); // AudioFormat = PCM
        bw.Write((short)1); // Mono
        bw.Write(sampleRate);
        bw.Write(sampleRate * 2); // ByteRate
        bw.Write((short)2); // BlockAlign
        bw.Write((short)16); // BitsPerSample
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);

        foreach (var s in samples)
        {
            bw.Write(s);
        }

        bw.Flush();
        return ms.ToArray();
    }
}

public enum MochiExpression
{
    Calm = 0,
    Happy = 1,
    Blush = 2,
    Sparkle = 3,
    Wink = 4,
    Celebrate = 5,
    Cozy = 6
}

public sealed record MochiReaction(
    MochiExpression Expression,
    string Emoji,
    string DialogueKey,
    bool IsDiscoveryUnlocked,
    string? DiscoveryKey);

/// <summary>Vòng lặp tương tác Mochi: chạm/vuốt ve đổi biểu cảm, thoại theo tiến độ ngày &amp; khám phá nhỏ khi đạt 100%.</summary>
public static class MochiInteractionEngine
{
    private static readonly MochiExpression[] PetCycle =
    [
        MochiExpression.Happy,
        MochiExpression.Blush,
        MochiExpression.Sparkle,
        MochiExpression.Wink
    ];

    public static MochiReaction React(int tapCount, double dayProgress, bool isPerfectDay, bool isNight)
    {
        var clampedProgress = Math.Clamp(dayProgress, 0.0, 1.0);

        if (isPerfectDay || clampedProgress >= 0.999)
        {
            var expr = tapCount % 2 == 0 ? MochiExpression.Celebrate : MochiExpression.Sparkle;
            return new MochiReaction(
                expr,
                EmojiFor(expr),
                "mochi.talk.perfect",
                IsDiscoveryUnlocked: true,
                DiscoveryKey: "mochi.discovery.body");
        }

        if (tapCount > 0)
        {
            var expr = PetCycle[(tapCount - 1) % PetCycle.Length];
            var talkKey = (tapCount % 3) switch
            {
                1 => "mochi.talk.pet1",
                2 => "mochi.talk.pet2",
                _ => ProgressDialogueKey(clampedProgress, isNight)
            };

            return new MochiReaction(expr, EmojiFor(expr), talkKey, false, null);
        }

        var baseExpr = isNight
            ? MochiExpression.Cozy
            : clampedProgress >= 0.5 ? MochiExpression.Happy : MochiExpression.Calm;

        return new MochiReaction(
            baseExpr,
            EmojiFor(baseExpr),
            ProgressDialogueKey(clampedProgress, isNight),
            false,
            null);
    }

    private static string ProgressDialogueKey(double progress, bool isNight)
    {
        if (isNight && progress < 0.5)
        {
            return "mochi.talk.cozy";
        }

        if (progress <= 0.01)
        {
            return "mochi.talk.start";
        }

        if (progress < 0.75)
        {
            return "mochi.talk.mid";
        }

        return "mochi.talk.almost";
    }

    public static string EmojiFor(MochiExpression expression) => expression switch
    {
        MochiExpression.Happy => "😊",
        MochiExpression.Blush => "🥰",
        MochiExpression.Sparkle => "✨",
        MochiExpression.Wink => "😉",
        MochiExpression.Celebrate => "🎉",
        MochiExpression.Cozy => "🌙",
        _ => "🫧"
    };
}
