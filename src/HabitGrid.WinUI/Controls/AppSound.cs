using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using HabitGrid.Core.Domain;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace HabitGrid.WinUI.Controls;

/// <summary>Phát âm thanh check-in &amp; hoàn thành ngày tổng hợp bằng code (<see cref="SoundSynthesizer"/>).</summary>
public static class AppSound
{
    private static readonly ConcurrentDictionary<SoundToneKind, byte[]> Cache = new();
    private static MediaPlayer? _player;

    public static async Task PlayAsync(SoundToneKind kind)
    {
        try
        {
            var settings = App.Host.Get<ISettingsStore>();
            var raw = await settings.GetAsync(SettingKeys.SoundEffects);
            if (string.Equals(raw, "0", StringComparison.Ordinal) ||
                string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var wav = Cache.GetOrAdd(kind, SoundSynthesizer.SynthesizeWav);
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(wav.AsBuffer());
            stream.Seek(0);

            _player ??= new MediaPlayer { Volume = 0.45 };
            _player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
            _player.Play();
        }
        catch
        {
            // Máy không có thiết bị âm thanh hoặc chạy smoke headless: bỏ qua an toàn.
        }
    }
}
