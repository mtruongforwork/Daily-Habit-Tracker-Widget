using System.Diagnostics;
using System.Globalization;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Focus;
using Windows.Media.Core;
using Windows.Media.Playback;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace HabitGrid.WinUI.Focus;

/// <summary>
/// Bàn trộn âm thanh nền của trang Focus: mỗi kênh (trắng, hồng, nâu, mưa, sóng) là một đoạn WAV tự tạo bằng <see cref="AmbientSynth"/>,
/// phát lặp bằng một MediaPlayer riêng với âm lượng riêng. Âm thanh tiếp tục khi rời trang hoặc thu nhỏ app (dừng khi đóng app).
/// Âm lượng từng kênh được lưu trong cài đặt (<c>focus.ambient</c>).
/// </summary>
public sealed class AmbientMixer
{
    public const string SettingKey = "focus.ambient";

    private static readonly Lazy<AmbientMixer> LazyInstance = new(() => new AmbientMixer());

    private readonly Dictionary<AmbientKind, double> _volumes = Enum.GetValues<AmbientKind>().ToDictionary(k => k, _ => 0.0);
    private readonly Dictionary<AmbientKind, MediaPlayer> _players = [];
    private readonly Dictionary<AmbientKind, double[]> _envelopes = [];
    private readonly Stopwatch _clock = new();
    private bool _loaded;

    public static AmbientMixer Instance => LazyInstance.Value;

    public bool IsPlaying { get; private set; }

    public event EventHandler? Changed;

    public double VolumeOf(AmbientKind kind) => _volumes[kind];

    /// <summary>Đọc âm lượng đã lưu (lần đầu); mặc định bật nhẹ tiếng mưa để bấm Phát là nghe ngay.</summary>
    public async Task LoadAsync(ISettingsStore settings)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        var raw = await settings.GetAsync(SettingKey);

        if (raw is null)
        {
            _volumes[AmbientKind.Rain] = 0.6;
            return;
        }

        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=');

            if (pair.Length == 2 && Enum.TryParse<AmbientKind>(pair[0], out var kind) && double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var volume))
            {
                _volumes[kind] = Math.Clamp(volume, 0, 1);
            }
        }
    }

    public async Task SetVolumeAsync(AmbientKind kind, double volume, ISettingsStore settings)
    {
        _volumes[kind] = Math.Clamp(volume, 0, 1);

        if (IsPlaying)
        {
            await ApplyAsync(kind);
        }

        await settings.SetAsync(SettingKey, string.Join(';', _volumes.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Key}={p.Value:0.##}"))));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task PlayAsync()
    {
        IsPlaying = true;
        _clock.Start();

        foreach (var kind in _volumes.Keys)
        {
            await ApplyAsync(kind);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Pause()
    {
        IsPlaying = false;
        _clock.Stop();

        foreach (var player in _players.Values)
        {
            player.Pause();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Mức âm hiện tại (0..1) để vẽ sóng âm: trộn đường bao của các kênh đang bật theo âm lượng, tại thời điểm <paramref name="offset"/> giây
    /// tính từ vị trí đang phát. Không phát thì 0.
    /// </summary>
    public double LevelAt(double offset)
    {
        if (!IsPlaying)
        {
            return 0;
        }

        var seconds = (_clock.Elapsed.TotalSeconds + offset) % AmbientSynth.LoopSeconds;
        var level = 0.0;

        foreach (var (kind, volume) in _volumes)
        {
            if (volume > 0 && _envelopes.TryGetValue(kind, out var envelope) && envelope.Length > 0)
            {
                var index = (int)(seconds / AmbientSynth.LoopSeconds * envelope.Length) % envelope.Length;
                level += envelope[index] * volume;
            }
        }

        return Math.Clamp(level * 2.2, 0, 1);
    }

    private async Task ApplyAsync(AmbientKind kind)
    {
        var volume = _volumes[kind];

        if (volume <= 0)
        {
            if (_players.TryGetValue(kind, out var idle))
            {
                idle.Pause();
            }

            return;
        }

        var player = await PlayerAsync(kind);
        player.Volume = volume;

        if (IsPlaying)
        {
            player.Play();
        }
    }

    /// <summary>Tạo âm cho kênh (lần đầu, trên luồng nền) và dựng MediaPlayer phát lặp.</summary>
    private async Task<MediaPlayer> PlayerAsync(AmbientKind kind)
    {
        if (_players.TryGetValue(kind, out var existing))
        {
            return existing;
        }

        var (wav, envelope) = await Task.Run(() =>
        {
            var samples = AmbientSynth.Generate(kind);
            return (AmbientSynth.ToWav(samples), AmbientSynth.Envelope(samples));
        });

        var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(wav.AsBuffer());
        stream.Seek(0);

        var player = new MediaPlayer
        {
            Source = MediaSource.CreateFromStream(stream, "audio/wav"),
            IsLoopingEnabled = true,
            AudioCategory = MediaPlayerAudioCategory.Other
        };

        // Không hiện bảng điều khiển phương tiện của Windows cho tiếng nền.
        player.CommandManager.IsEnabled = false;
        _players[kind] = player;
        _envelopes[kind] = envelope;
        return player;
    }
}
