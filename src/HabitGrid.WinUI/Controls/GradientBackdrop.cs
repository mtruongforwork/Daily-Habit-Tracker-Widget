using System.Numerics;
using HabitGrid.Presentation.Theming;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Nền canvas + 3 "đốm" màu lớn mờ trôi rất chậm (03-UI-REBUILD-GLOW2 §4.1). Màu lấy từ theme, hoặc từ màu
/// các thói quen của người dùng để nền "mang màu" của chính họ. Chuyển động dừng khi cửa sổ không active
/// hoặc khi tắt hiệu ứng.
/// </summary>
public sealed class GradientBackdrop : UserControl
{
    private const double BlobSize = 720;

    private readonly Canvas _root = new();
    private readonly List<Ellipse> _blobs = [];
    private readonly List<AnimationController?> _controllers = [];
    private bool _animated;

    // Vị trí tương đối (0..1) của tâm mỗi đốm và quỹ đạo trôi (px).
    private static readonly (double X, double Y, Vector3 Drift, double Seconds)[] Layout =
    [
        (0.12, 0.10, new Vector3(140, 90, 0), 70),
        (0.92, 0.30, new Vector3(-120, 140, 0), 85),
        (0.45, 1.00, new Vector3(110, -120, 0), 95)
    ];

    public GradientBackdrop()
    {
        IsHitTestVisible = false;
        IsTabStop = false;
        _root.Background = (Brush)Application.Current.Resources["CanvasBrush"];
        Content = _root;

        foreach (var _ in Layout)
        {
            var blob = new Ellipse
            {
                Width = BlobSize,
                Height = BlobSize
            };
            _blobs.Add(blob);
            _root.Children.Add(blob);
        }

        SizeChanged += (_, _) => PositionBlobs();
        Unloaded += (_, _) => Pause();
    }

    /// <summary>Đặt màu đốm theo bảng màu (và tùy chọn theo màu thói quen của người dùng).</summary>
    private Windows.UI.Color _canvas = Windows.UI.Color.FromArgb(255, 14, 14, 20);

    /// <summary>
    /// Độ đục của lớp nền canvas. 255 = đặc (kính tắt); thấp hơn thì kính mờ Acrylic của cửa sổ lộ ra phía sau
    /// (cửa sổ phải đặt <c>SystemBackdrop</c>, nếu không phần trong suốt sẽ chỉ thấy nền đen).
    /// </summary>
    public void SetGlass(byte alpha)
    {
        _root.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(alpha, _canvas.R, _canvas.G, _canvas.B));
    }

    /// <summary>Độ đục đang áp (để dựng lại nền khi đổi theme).</summary>
    public byte GlassAlpha { get; private set; } = 255;

    public void SetGlassAlpha(byte alpha)
    {
        GlassAlpha = alpha;
        SetGlass(alpha);
    }

    public void Apply(ThemePalette palette, IReadOnlyList<string>? habitColors = null)
    {
        _canvas = Ui.ParseColor(palette.Canvas, _canvas);
        SetGlass(GlassAlpha);

        var colors = habitColors is { Count: >= 2 } ? habitColors : palette.Blobs;

        for (var i = 0; i < _blobs.Count; i++)
        {
            var hex = colors[i % colors.Count];
            var center = Ui.ParseColor(hex);
            var alpha = (byte)Math.Round(255 * palette.BlobOpacity);

            var brush = new RadialGradientBrush();
            brush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(alpha, center.R, center.G, center.B), Offset = 0 });
            brush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb((byte)(alpha / 3), center.R, center.G, center.B), Offset = 0.45 });
            brush.GradientStops.Add(new GradientStop { Color = Windows.UI.Color.FromArgb(0, center.R, center.G, center.B), Offset = 1 });
            _blobs[i].Fill = brush;
        }
    }

    /// <summary>Bật/tắt chuyển động (theo trạng thái active của cửa sổ và cài đặt giảm chuyển động).</summary>
    public void SetAnimated(bool animated)
    {
        if (animated == _animated)
        {
            return;
        }

        _animated = animated;

        if (animated)
        {
            Start();
        }
        else
        {
            Pause();
        }
    }

    private void PositionBlobs()
    {
        for (var i = 0; i < _blobs.Count; i++)
        {
            var (x, y, _, _) = Layout[i];
            Canvas.SetLeft(_blobs[i], x * ActualWidth - BlobSize / 2);
            Canvas.SetTop(_blobs[i], y * ActualHeight - BlobSize / 2);
        }

        // Cắt đúng khung điều khiển để đốm màu không tràn ra ngoài (kể cả khi chụp ảnh).
        Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, ActualWidth, ActualHeight) };
    }

    private void Start()
    {
        for (var i = 0; i < _blobs.Count; i++)
        {
            if (i < _controllers.Count && _controllers[i] is { } existing)
            {
                existing.Resume();
                continue;
            }

            var visual = ElementCompositionPreview.GetElementVisual(_blobs[i]);
            var (_, _, drift, seconds) = Layout[i];

            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(0f, Vector3.Zero);
            animation.InsertKeyFrame(0.5f, drift);
            animation.InsertKeyFrame(1f, Vector3.Zero);
            animation.Duration = TimeSpan.FromSeconds(seconds);
            animation.IterationBehavior = AnimationIterationBehavior.Forever;
            animation.Target = "Translation";

            ElementCompositionPreview.SetIsTranslationEnabled(_blobs[i], true);
            visual.StartAnimation("Translation", animation);

            var controller = visual.TryGetAnimationController("Translation");
            if (i < _controllers.Count)
            {
                _controllers[i] = controller;
            }
            else
            {
                _controllers.Add(controller);
            }
        }
    }

    private void Pause()
    {
        foreach (var controller in _controllers)
        {
            controller?.Pause();
        }
    }
}
