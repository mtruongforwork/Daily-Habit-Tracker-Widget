using FluentAssertions;
using HabitGrid.Core.Domain.Focus;
using Xunit;

namespace HabitGrid.Core.Tests.Domain;

public class AmbientSynthTests
{
    [Theory]
    [InlineData(AmbientKind.White)]
    [InlineData(AmbientKind.Pink)]
    [InlineData(AmbientKind.Brown)]
    [InlineData(AmbientKind.Rain)]
    [InlineData(AmbientKind.Waves)]
    public void Generate_MakesAFullLoop_WithoutClipping(AmbientKind kind)
    {
        var samples = AmbientSynth.Generate(kind, seconds: 4);

        samples.Should().HaveCount(4 * AmbientSynth.SampleRate);
        samples.Max(s => Math.Abs((int)s)).Should().BeLessThan(short.MaxValue, "không được chạm trần (gây méo tiếng)");
        samples.Count(s => s != 0).Should().BeGreaterThan(samples.Length / 2, "phải có âm thanh thật");
    }

    [Fact]
    public void Generate_IsDeterministic() =>
        AmbientSynth.Generate(AmbientKind.Rain, seconds: 2).Should().Equal(AmbientSynth.Generate(AmbientKind.Rain, seconds: 2));

    [Theory]
    [InlineData(AmbientKind.Brown)]
    [InlineData(AmbientKind.Waves)]
    public void Generate_LoopSeamIsSmooth(AmbientKind kind)
    {
        // Tiếng trầm (nâu, sóng) thay đổi chậm: bước nhảy từ mẫu cuối về mẫu đầu không được lớn hơn các bước bình thường bên trong.
        var samples = AmbientSynth.Generate(kind, seconds: 4);
        var steps = Enumerable.Range(1, samples.Length - 1).Select(i => Math.Abs(samples[i] - samples[i - 1])).ToList();
        var seam = Math.Abs(samples[0] - samples[^1]);

        seam.Should().BeLessThanOrEqualTo(steps.Max());
    }

    [Fact]
    public void Brown_IsDeeperThanWhite()
    {
        // Tiếng nâu đổi dấu (qua 0) ít hơn hẳn tiếng trắng: năng lượng dồn về tần số thấp.
        static int Crossings(short[] s) => Enumerable.Range(1, s.Length - 1).Count(i => (s[i] >= 0) != (s[i - 1] >= 0));

        Crossings(AmbientSynth.Generate(AmbientKind.Brown, seconds: 2)).Should().BeLessThan(Crossings(AmbientSynth.Generate(AmbientKind.White, seconds: 2)) / 4);
    }

    [Fact]
    public void Waves_SwellAndFall()
    {
        var envelope = AmbientSynth.Envelope(AmbientSynth.Generate(AmbientKind.Waves, seconds: 16));

        envelope.Max().Should().BeGreaterThan(envelope.Min() * 3, "sóng phải lúc dâng lúc hạ");
    }

    [Fact]
    public void ToWav_WritesAValidPcmHeader()
    {
        var samples = new short[] { 0, 1000, -1000, 0 };
        var wav = AmbientSynth.ToWav(samples);

        wav.Should().HaveCount(44 + 8);
        System.Text.Encoding.ASCII.GetString(wav, 0, 4).Should().Be("RIFF");
        System.Text.Encoding.ASCII.GetString(wav, 8, 4).Should().Be("WAVE");
        BitConverter.ToInt32(wav, 24).Should().Be(AmbientSynth.SampleRate);
        BitConverter.ToInt32(wav, 40).Should().Be(8);
    }
}
