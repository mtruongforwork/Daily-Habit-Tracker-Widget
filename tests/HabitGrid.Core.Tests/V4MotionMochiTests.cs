using System.Text;
using HabitGrid.Core.Domain;
using Xunit;

namespace HabitGrid.Core.Tests;

public class V4MotionMochiTests
{
    [Theory]
    [InlineData(SoundToneKind.CheckInPop)]
    [InlineData(SoundToneKind.Increment)]
    [InlineData(SoundToneKind.PerfectDayChime)]
    public void SoundSynthesizer_ProducesValidPcmWav(SoundToneKind kind)
    {
        var wav = SoundSynthesizer.SynthesizeWav(kind);

        Assert.True(wav.Length > 44);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

        var dataSize = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataSize);

        // Verify non-silent audio samples exist
        var hasNonZero = false;
        for (var i = 44; i + 1 < wav.Length; i += 2)
        {
            if (BitConverter.ToInt16(wav, i) != 0)
            {
                hasNonZero = true;
                break;
            }
        }

        Assert.True(hasNonZero);
    }

    [Fact]
    public void SoundSynthesizer_PerfectDayChimeIsLongerThanIncrement()
    {
        var inc = SoundSynthesizer.SynthesizeWav(SoundToneKind.Increment);
        var chime = SoundSynthesizer.SynthesizeWav(SoundToneKind.PerfectDayChime);

        Assert.True(chime.Length > inc.Length);
    }

    [Fact]
    public void MochiInteraction_TracksProgressAndUnlocksDiscoveryAtPerfectDay()
    {
        var start = MochiInteractionEngine.React(0, 0.0, isPerfectDay: false, isNight: false);
        Assert.Equal("mochi.talk.start", start.DialogueKey);
        Assert.False(start.IsDiscoveryUnlocked);

        var mid = MochiInteractionEngine.React(0, 0.5, isPerfectDay: false, isNight: false);
        Assert.Equal("mochi.talk.mid", mid.DialogueKey);

        var almost = MochiInteractionEngine.React(0, 0.85, isPerfectDay: false, isNight: false);
        Assert.Equal("mochi.talk.almost", almost.DialogueKey);

        var perfect = MochiInteractionEngine.React(0, 1.0, isPerfectDay: true, isNight: false);
        Assert.Equal("mochi.talk.perfect", perfect.DialogueKey);
        Assert.True(perfect.IsDiscoveryUnlocked);
        Assert.Equal("mochi.discovery.body", perfect.DiscoveryKey);
        Assert.Equal(MochiExpression.Celebrate, perfect.Expression);
    }

    [Fact]
    public void MochiInteraction_CyclesExpressionsOnTap()
    {
        var r1 = MochiInteractionEngine.React(1, 0.25, isPerfectDay: false, isNight: false);
        var r2 = MochiInteractionEngine.React(2, 0.25, isPerfectDay: false, isNight: false);
        var r3 = MochiInteractionEngine.React(3, 0.25, isPerfectDay: false, isNight: false);

        Assert.NotEqual(r1.Expression, r2.Expression);
        Assert.NotEqual(r2.Expression, r3.Expression);
        Assert.Equal("mochi.talk.pet1", r1.DialogueKey);
        Assert.Equal("mochi.talk.pet2", r2.DialogueKey);
    }
}
