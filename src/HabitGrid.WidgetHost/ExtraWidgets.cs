using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using HabitGrid.Core.Domain;
using HabitGrid.Core.Domain.Challenges;
using HabitGrid.Core.Domain.Gamification;
using HabitGrid.Core.Domain.Widgets;
using HabitGrid.Platform.Desktop;
using HabitGrid.Presentation.Services;
using HabitGrid.Presentation.ViewModels;

namespace HabitGrid.WidgetHost;

internal static class StackExtensions
{
    /// <summary>WPF StackPanel không có Spacing: thêm phần tử kèm khoảng cách phía trên (trừ phần tử đầu).</summary>
    public static void Put(this StackPanel panel, UIElement element, double gap = 8)
    {
        if (panel.Children.Count > 0 && element is FrameworkElement fe)
        {
            fe.Margin = new Thickness(fe.Margin.Left, gap, fe.Margin.Right, fe.Margin.Bottom);
        }

        panel.Children.Add(element);
    }
}

/// <summary>Mức hiển thị của một widget: nghỉ (nhỏ gọn), rê chuột (xem nhanh, chỉ widget có mức này) và bảng đầy đủ.</summary>
public enum ViewLevel
{
    Rest,
    Hover,
    Panel
}

/// <summary>
/// Nền chung của các widget Plus: cửa sổ không viền, nền tối bo góc (chữ trắng đọc được trên mọi hình nền), kéo được ở chế độ sắp xếp
/// (hoặc giữ Alt), nhớ vị trí qua <see cref="Moved"/>, ghim theo kiểu người dùng chọn.
/// <para>
/// Mỗi widget có hai dạng: <b>nhỏ gọn</b> (chiếm một góc nhỏ, chỉ số liệu quan trọng, tự đổi theo ngữ cảnh) và <b>mở rộng</b> (đầy đủ).
/// Rê chuột vào một lúc thì mở rộng, rời đi thì thu lại; bấm vào để giữ mở (bấm lần nữa để thu).
/// </para>
/// </summary>
public abstract class ExtraWidgetWindow : Window
{
    private static readonly Brush CardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xE6, 0x1B, 0x15, 0x30)));
    private static readonly Brush RimBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)));

    /// <summary>Lớp phủ nhẹ khi dùng kính mờ thật (hình nền phía sau được làm mờ và lộ ra).</summary>
    private static readonly Color GlassTint = Color.FromArgb(0x5A, 0x1B, 0x15, 0x30);
    private static readonly Brush GlassCardBrush = Freeze(new SolidColorBrush(Color.FromArgb(0x1F, 0x1B, 0x15, 0x30)));

    /// <summary>Kiểu Dynamic Island: thẻ gần đen, bo tròn theo chiều cao (viên thuốc) khi nhỏ gọn.</summary>
    private static readonly Brush IslandBrush = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0x0E, 0x0E, 0x12)));

    /// <summary>Bật kính mờ thật cho các widget Plus/gộp thay cho kiểu Dynamic Island (đặt trước khi tạo cửa sổ; chỉ có tác dụng trên Windows 11).</summary>
    public static bool UseGlass { get; set; }

    /// <summary>Tắt hoạt ảnh lò xo (giảm chuyển động của Windows hoặc của app): biến hình tức thì.</summary>
    public static bool Animations { get; set; } = SystemParameters.ClientAreaAnimation;

    private readonly bool _glass = UseGlass && GlassInterop.IsSupported;
    private readonly DispatcherTimer _expandTimer = new() { Interval = TimeSpan.FromMilliseconds(220) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private bool _pinnedOpen;
    private bool _dragged;
    private bool _sized;
    private Border? _card;
    private readonly SpringValue _w = new();
    private readonly SpringValue _h = new();
    private readonly SpringValue _l = new();
    private readonly SpringValue _t = new();
    private readonly SpringValue _r = new();
    private bool _morphing;
    private DateTime _lastFrame;
    private int _peekVersion;
    protected bool _peeking;
    private bool _stickyPeek;
    private Color? _peekTint;
    private SolidColorBrush? _tint;

    /// <summary>Màu nền gốc của thẻ kiểu Island (gần đen).</summary>
    protected static readonly Color IslandBase = Color.FromArgb(0xF0, 0x0E, 0x0E, 0x12);

    protected ExtraWidgetWindow(string title)
    {
        WindowStyle = WindowStyle.None;
        // Kính mờ thật chỉ chạy được trên cửa sổ không phân lớp (AllowsTransparency = false); không có thì dùng thẻ đặc trong suốt một phần.
        AllowsTransparency = !_glass;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Title = title;

        if (_glass)
        {
            SourceInitialized += (_, _) => GlassInterop.Enable(new WindowInteropHelper(this).Handle, GlassTint, dark: true);
        }

        // Kích thước thật đặt ở lần dựng nội dung đầu tiên; trước đó giữ cửa sổ cực nhỏ để không lóe lên 1152x592 (cỡ mặc định của Window).
        Width = 8;
        Height = 8;

        MouseLeftButtonDown += (_, _) =>
        {
            _dragged = false;

            if (!CanDrag || (!EditMode && !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt)))
            {
                return;
            }

            try
            {
                DragMove();
                _dragged = true;
                Moved?.Invoke(this, EventArgs.Empty);
            }
            catch (InvalidOperationException)
            {
                // Nhả chuột trước khi kéo.
            }
        };

        // Bấm = giữ mở / thu lại (không tính khi vừa kéo xong).
        MouseLeftButtonUp += (_, _) =>
        {
            if (_dragged || EditMode)
            {
                return;
            }

            if (HasHoverLevel)
            {
                // Có mức xem nhanh: bấm viên thuốc = mở/đóng bảng đầy đủ (giữ mở); đang ở bảng đã giữ thì bấm để thu về nghỉ.
                if (Level == ViewLevel.Panel && _pinnedOpen)
                {
                    _pinnedOpen = false;
                    SetLevel(ViewLevel.Rest);
                }
                else
                {
                    _pinnedOpen = true;
                    SetLevel(ViewLevel.Panel);
                }

                return;
            }

            _pinnedOpen = !_pinnedOpen;
            SetExpanded(_pinnedOpen);
        };

        MouseEnter += (_, _) =>
        {
            _collapseTimer.Stop();

            if (!IsExpanded && !(_peeking && _stickyPeek))
            {
                _expandTimer.Interval = HoverDelay;
                _expandTimer.Start();
            }
        };
        MouseLeave += (_, _) =>
        {
            _expandTimer.Stop();

            if (IsExpanded && !_pinnedOpen)
            {
                _collapseTimer.Interval = CollapseDelay;
                _collapseTimer.Start();
            }
        };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            SetExpanded(true);
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();

            if (!_pinnedOpen)
            {
                SetExpanded(false);
            }
        };
    }

    public bool EditMode { get; set; }

    /// <summary>Thu gọn và bỏ trạng thái "giữ mở" (nút thu gọn trên bảng đầy đủ).</summary>
    public void Collapse()
    {
        _pinnedOpen = false;
        SetExpanded(false);
    }

    /// <summary>Cửa sổ này đang dùng kính mờ thật hay thẻ đặc.</summary>
    public bool IsGlass => _glass;

    /// <summary>Mức hiển thị hiện tại.</summary>
    public ViewLevel Level { get; private set; }

    public bool IsExpanded => Level != ViewLevel.Rest;

    /// <summary>Độ trễ từ lúc rê chuột vào tới lúc mở.</summary>
    protected virtual TimeSpan HoverDelay => TimeSpan.FromMilliseconds(220);

    /// <summary>Độ trễ từ lúc chuột rời đi tới lúc thu lại.</summary>
    protected virtual TimeSpan CollapseDelay => TimeSpan.FromMilliseconds(900);

    /// <summary>True: rê chuột mở mức xem nhanh (<see cref="BuildHover"/>), bấm mới mở bảng đầy đủ.</summary>
    protected virtual bool HasHoverLevel => false;

    protected virtual Size HoverSize => ExpandedSize;

    /// <summary>Màu nền theo nội dung đang hiển thị (null = màu gốc). Chỉ áp cho kiểu Island đặc, không áp cho kính mờ.</summary>
    protected virtual Color? TintFor(ViewLevel level) => null;

    /// <summary>Đổi màu nền mượt (300 ms); tắt hoạt ảnh thì đổi tức thì.</summary>
    private void ApplyTint(Color? tint)
    {
        if (_glass || _tint is null)
        {
            return;
        }

        var target = tint ?? IslandBase;

        if (_tint.Color == target)
        {
            return;
        }

        if (!Animations)
        {
            _tint.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _tint.Color = target;
            return;
        }

        _tint.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(target, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    /// <summary>Gọi khi trạng thái nội dung đổi mà không dựng lại (ví dụ tới giờ "tối" làm thẻ chuyển màu cảnh báo).</summary>
    protected void RefreshTint() => ApplyTint(_peeking ? _peekTint : TintFor(Level));

    protected virtual UIElement BuildHover() => BuildExpanded();

    private Size SizeFor(ViewLevel level) => level switch
    {
        ViewLevel.Rest => CompactSize,
        ViewLevel.Hover => HoverSize,
        _ => ExpandedSize
    };

    private UIElement BuildFor(ViewLevel level) => level switch
    {
        ViewLevel.Rest => BuildCompact(),
        ViewLevel.Hover => BuildHover(),
        _ => BuildExpanded()
    };

    public event EventHandler? Moved;

    protected abstract Size CompactSize { get; }

    /// <summary>True: thẻ nở/thu quanh tâm ngang và cố định cạnh trên (viên thuốc giữa mép trên); false: giữ mép gần cạnh màn hình hơn.</summary>
    protected virtual bool CenterAnchored => false;

    /// <summary>False: không kéo được (vị trí do chương trình tính, ví dụ giữa mép trên).</summary>
    protected virtual bool CanDrag => true;

    protected abstract Size ExpandedSize { get; }

    protected abstract UIElement BuildCompact();

    protected abstract UIElement BuildExpanded();

    /// <summary>Kích thước dạng nhỏ gọn (dùng để xếp vị trí mặc định).</summary>
    public Size NominalSize => CompactSize;

    /// <summary>Bán kính góc: kính mờ do Windows 11 quyết định (~8 px); kiểu Island nhỏ gọn là viên thuốc, mở rộng bo mềm.</summary>
    private double CardRadius(Size size, bool expanded) =>
        _glass ? 8 : expanded ? 26 : Math.Min(size.Height / 2, 30);

    /// <summary>Gọi sau khi dữ liệu đổi: dựng lại nội dung theo dạng hiện tại (đang "nhô lên" thông báo thì để yên).</summary>
    protected void Render(bool crossfade = false)
    {
        if (_peeking)
        {
            return;
        }

        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var content = BuildFor(Level);
        var builtMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        SetCardContent(content, SizeFor(Level), crossfade, peek: false);
        FrameLog?.Add($"BUILD level={Level} build={builtMs:0.0}ms total={System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:0.0}ms");
    }

    private void SetCardContent(UIElement child, Size size, bool crossfade, bool peek)
    {
        if (!_sized)
        {
            Width = size.Width;
            Height = size.Height;

            if (CenterAnchored)
            {
                // Vị trí có thể đã được tính theo cỡ khác (trước khi có dữ liệu): căn lại quanh tâm màn hình chính.
                Left = (SystemParameters.PrimaryScreenWidth / 2) - (size.Width / 2);
            }

            _w.Snap(size.Width);
            _h.Snap(size.Height);
            _l.Snap(double.IsNaN(Left) ? 0 : Left);
            _t.Snap(double.IsNaN(Top) ? 0 : Top);
            _sized = true;
        }

        if (_card is null)
        {
            _card = new Border
            {
                CornerRadius = new CornerRadius(CardRadius(size, IsExpanded)),
                Background = _glass ? GlassCardBrush : (_tint = new SolidColorBrush(IslandBase)),
                BorderBrush = RimBrush,
                BorderThickness = new Thickness(1)
            };
            _r.Snap(CardRadius(size, IsExpanded));
            Content = _card;
        }

        var padding = IsExpanded ? new Thickness(16, 14, 16, 14) : peek ? new Thickness(14, 6, 14, 6) : new Thickness(8);
        _card.Padding = padding;

        // Nội dung có kích thước CỐ ĐỊNH bằng đích đến và bị cắt theo thẻ: khi thẻ co giãn bằng lò xo, WPF không phải đo/sắp xếp lại
        // cả cây giao diện ở mỗi khung hình (nguyên nhân chính của việc khựng), chỉ khung cửa sổ đổi.
        if (child is FrameworkElement fixedChild)
        {
            fixedChild.Width = Math.Max(1, size.Width - padding.Left - padding.Right - 2);
            fixedChild.Height = Math.Max(1, size.Height - padding.Top - padding.Bottom - 2);
            fixedChild.HorizontalAlignment = HorizontalAlignment.Left;
            fixedChild.VerticalAlignment = VerticalAlignment.Top;
        }

        _card.ClipToBounds = true;
        _card.Child = child;
        ApplyTint(peek ? _peekTint : TintFor(Level));

        if (crossfade && Animations)
        {
            // Nội dung mới hiện dần và phóng nhẹ từ 94%: tạo cảm giác "biến hình" thay vì đổi cái rụp.
            child.Opacity = 0;
            child.RenderTransformOrigin = new Point(0.5, 0.5);
            var scale = new ScaleTransform(0.94, 0.94);
            child.RenderTransform = scale;
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { BeginTime = TimeSpan.FromMilliseconds(70) };
            var grow = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(320)) { BeginTime = TimeSpan.FromMilliseconds(40), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            child.BeginAnimation(OpacityProperty, fade);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }

    /// <summary>Vị trí đích khi đổi sang kích thước mới: giữ mép gần cạnh màn hình hơn (widget ở bên phải thì giữ mép phải) và không tràn đáy.</summary>
    private (double Left, double Top) AnchorFor(Size target)
    {
        var work = SystemParameters.WorkArea;
        var currentLeft = _morphing ? _l.Target : Left;
        var currentWidth = _morphing ? _w.Target : Width;
        var currentTop = _morphing ? _t.Target : Top;

        if (CenterAnchored)
        {
            // Island luôn nở quanh tâm màn hình chính (không phải tâm cửa sổ hiện tại: lúc mới dựng cửa sổ còn rất nhỏ và vị trí có thể
            // được tính theo cỡ khác, nên dùng tâm cũ sẽ lệch khi trạng thái nghỉ đổi cỡ). Giữ nguyên cạnh trên; chỉ không để tràn đáy.
            var center = SystemParameters.PrimaryScreenWidth / 2;
            return (center - (target.Width / 2), Math.Min(currentTop, Math.Max(0, work.Bottom - target.Height - 8)));
        }

        var anchorRight = currentLeft + (currentWidth / 2) > work.Left + (work.Width / 2);
        var left = anchorRight ? currentLeft + currentWidth - target.Width : currentLeft;
        var top = Math.Min(currentTop, Math.Max(work.Top, work.Bottom - target.Height - 8));
        return (left, top);
    }

    /// <summary>Biến hình sang kích thước/vị trí/góc bo mới bằng lò xo (hoặc tức thì khi tắt hoạt ảnh).</summary>
    private void MorphTo(Size size, (double Left, double Top) at, double radius, bool animate)
    {
        _w.Target = Math.Max(8, size.Width);
        _h.Target = Math.Max(8, size.Height);
        _l.Target = at.Left;
        _t.Target = at.Top;
        _r.Target = radius;

        if (!animate || !Animations)
        {
            foreach (var spring in new[] { _w, _h, _l, _t, _r })
            {
                spring.Snap(spring.Target);
            }

            if (_morphing)
            {
                System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
                _morphing = false;
            }

            Apply();
            SettleWindow();
            return;
        }

        if (!_morphing)
        {
            _w.Value = Width;
            _h.Value = Height;
            _l.Value = Left;
            _t.Value = Top;
            _r.Value = _card?.CornerRadius.TopLeft ?? radius;
            _w.Velocity = _h.Velocity = _l.Velocity = _t.Velocity = _r.Velocity = 0;
            _box = new Rect(Left, Top, Width, Height);
            _morphing = true;
            _lastFrame = DateTime.UtcNow;
            System.Windows.Media.CompositionTarget.Rendering += OnFrame;
        }

        // Cửa sổ giữ nguyên MỘT khung bao (hợp của cỡ hiện tại và cỡ đích) suốt lúc co giãn; chỉ thẻ bên trong đổi cỡ.
        // Đổi kích thước cửa sổ phân lớp ở từng khung hình buộc DWM cấp lại bề mặt liên tục: đó là nguyên nhân khựng.
        if (!_glass)
        {
            _box = Rect.Union(_box, new Rect(_l.Target, _t.Target, _w.Target, _h.Target));
            ApplyBox();
        }
    }

    private Rect _box;

    /// <summary>Chẩn đoán: khi khác null, mỗi khung hình co giãn ghi một dòng (dt giữa hai khung, thời gian Apply).</summary>
    public static List<string>? FrameLog { get; set; }

    private void ApplyBox()
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero || !IsVisible)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        const uint noZOrder = 0x0004, noActivate = 0x0010, noCopyBits = 0x0100;
        SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(_box.Left * dpi), (int)Math.Round(_box.Top * dpi), (int)Math.Round(_box.Width * dpi), (int)Math.Round(_box.Height * dpi), noZOrder | noActivate | noCopyBits);
    }

    /// <summary>Hết co giãn: cửa sổ về đúng cỡ thẻ, thẻ lại phủ kín cửa sổ.</summary>
    private void SettleWindow()
    {
        if (_glass)
        {
            return;
        }

        if (_card is not null)
        {
            _card.Margin = new Thickness(0);
            _card.Width = double.NaN;
            _card.Height = double.NaN;
            _card.HorizontalAlignment = HorizontalAlignment.Stretch;
            _card.VerticalAlignment = VerticalAlignment.Stretch;
        }

        _box = new Rect(_l.Value, _t.Value, Math.Max(8, _w.Value), Math.Max(8, _h.Value));
        ApplyBox();
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = Math.Min((now - _lastFrame).TotalSeconds, 1.0 / 20);
        _lastFrame = now;
        var moving = false;

        foreach (var spring in new[] { _w, _h, _l, _t, _r })
        {
            moving |= spring.Step(dt);
        }

        var applyStart = System.Diagnostics.Stopwatch.GetTimestamp();
        Apply();

        if (FrameLog is not null)
        {
            var applyMs = System.Diagnostics.Stopwatch.GetElapsedTime(applyStart).TotalMilliseconds;
            FrameLog.Add($"dt={dt * 1000:0.0}ms apply={applyMs:0.0}ms");
        }

        if (!moving)
        {
            System.Windows.Media.CompositionTarget.Rendering -= OnFrame;
            _morphing = false;
            SettleWindow();
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private void Apply()
    {
        var handle = new WindowInteropHelper(this).Handle;

        // Kiểu kính mờ: chính cửa sổ là phần kính nên phải đổi cỡ cửa sổ theo từng khung (khung bao sẽ làm kính bung ra ngay).
        if (!_glass && _morphing && _card is not null && handle != IntPtr.Zero && IsVisible)
        {
            // Đang co giãn: cửa sổ đứng yên, chỉ thẻ đổi cỡ/vị trí bên trong khung bao.
            _card.HorizontalAlignment = HorizontalAlignment.Left;
            _card.VerticalAlignment = VerticalAlignment.Top;
            _card.Width = Math.Max(8, _w.Value);
            _card.Height = Math.Max(8, _h.Value);
            _card.Margin = new Thickness(_l.Value - _box.Left, _t.Value - _box.Top, 0, 0);
        }
        else if (handle != IntPtr.Zero && IsVisible)
        {
            // Một lần SetWindowPos cho cả vị trí lẫn kích thước (đặt từng thuộc Left/Top/Width/Height làm 4 lần đổi cửa sổ + đo bố cục mỗi khung hình).
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            const uint noZOrder = 0x0004, noActivate = 0x0010, noCopyBits = 0x0100;
            SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(_l.Value * dpi), (int)Math.Round(_t.Value * dpi), (int)Math.Round(Math.Max(8, _w.Value) * dpi), (int)Math.Round(Math.Max(8, _h.Value) * dpi), noZOrder | noActivate | noCopyBits);
        }
        else
        {
            Width = Math.Max(8, _w.Value);
            Height = Math.Max(8, _h.Value);
            Left = _l.Value;
            Top = _t.Value;
        }

        if (_card is not null)
        {
            _card.CornerRadius = new CornerRadius(Math.Max(0, _r.Value));
        }
    }

    /// <summary>
    /// Mở rộng/thu gọn kiểu Dynamic Island: thẻ biến hình bằng lò xo (nảy nhẹ), nội dung đổi kèm hiệu ứng mờ-phóng.
    /// </summary>
    public void SetExpanded(bool expanded, bool animate = true) =>
        SetLevel(expanded ? (HasHoverLevel ? ViewLevel.Hover : ViewLevel.Panel) : ViewLevel.Rest, animate);

    /// <summary>Đổi mức hiển thị (nghỉ / xem nhanh / bảng đầy đủ) bằng một lần biến hình.</summary>
    public void SetLevel(ViewLevel level, bool animate = true)
    {
        _peekVersion++;
        _peeking = false;

        if (Level == level && _sized)
        {
            return;
        }

        Level = level;
        var target = SizeFor(level);
        var at = AnchorFor(target);
        Render(crossfade: animate);
        MorphTo(target, at, CardRadius(target, level != ViewLevel.Rest), animate);
    }

    /// <summary>Chiều cao/rộng của dạng hiện tại đổi vì dữ liệu (ví dụ thêm một việc): biến hình theo kích thước mới.</summary>
    protected void RefreshLayout()
    {
        if (_peeking || !_sized)
        {
            return;
        }

        var target = SizeFor(Level);

        if (Math.Abs(_w.Target - target.Width) > 0.5 || Math.Abs(_h.Target - target.Height) > 0.5)
        {
            MorphTo(target, AnchorFor(target), CardRadius(target, IsExpanded), animate: true);
        }
    }

    /// <summary>
    /// "Nhô lên" một thông báo ngắn (kiểu Live Activity): thẻ phình ra, hiện nội dung, giữ <paramref name="holdMilliseconds"/> ms rồi thu về dạng cũ.
    /// Người dùng rê chuột/bấm trong lúc đó thì thông báo nhường chỗ.
    /// </summary>
    protected async void Peek(UIElement content, Size size, int holdMilliseconds = 2600, bool sticky = false, Color? tint = null)
    {
        if (EditMode || !_sized)
        {
            return;
        }

        var version = ++_peekVersion;
        var baseSize = SizeFor(Level);

        // Đang mở rộng thì không cần nhô lên thêm.
        if (IsExpanded)
        {
            return;
        }

        _peeking = true;
        _stickyPeek = sticky;
        _peekTint = tint;
        SetCardContent(content, size, crossfade: true, peek: true);
        MorphTo(size, AnchorFor(size), CardRadius(size, expanded: false), animate: true);
        await Task.Delay(holdMilliseconds);

        // Thẻ có nút bấm (nhắc nhở) không biến mất khi chuột đang nằm trên nó.
        while (version == _peekVersion && sticky && IsMouseOver)
        {
            await Task.Delay(400);
        }

        if (version != _peekVersion)
        {
            return;
        }

        EndPeek(baseSize);
    }

    private void EndPeek(Size baseSize)
    {
        _peeking = false;
        _stickyPeek = false;
        _peekTint = null;
        Render(crossfade: true);
        MorphTo(baseSize, AnchorFor(baseSize), CardRadius(baseSize, IsExpanded), animate: true);
    }

    /// <summary>Có thể nhô lên một thông báo ngay bây giờ không (đang nghỉ, không ở chế độ sắp xếp, đã có kích thước).</summary>
    public bool CanPeek => !EditMode && _sized && !IsExpanded;

    public bool IsPeeking => _peeking;

    /// <summary>Kết thúc thông báo đang nhô lên (ví dụ sau khi bấm "Xong"/"Để sau") và thu về trạng thái nghỉ.</summary>
    public void DismissPeek()
    {
        if (!_peeking)
        {
            return;
        }

        _peekVersion++;
        EndPeek(SizeFor(Level));
    }

    /// <summary>Đặt thẳng cửa sổ tại vị trí (DIP) không qua hoạt ảnh, kể cả khi đang biến hình (đích của lò xo cũng được dời).</summary>
    public void PlaceAt(double left, double top)
    {
        Left = left;
        Top = top;
        _l.Snap(left);
        _t.Snap(top);
    }

    public DesktopHostResult Pin(DesktopHostService host, DesktopPinningMode mode) =>
        host.Attach(new WindowInteropHelper(this).EnsureHandle(), mode);

    protected static TextBlock Text(string text, double size, bool bold = false, double opacity = 1, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.White,
        Opacity = opacity,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
        TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis
    };

    /// <summary>Biểu tượng thói quen để hiện trong widget: emoji thì bỏ tiền tố <c>emoji:</c>; biểu tượng Fluent (không có chữ) thì dùng dấu chấm.</summary>
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// <summary>
    /// Cho cửa sổ nhận bàn phím (gõ vào ô nhập) hoặc trả về không nhận. Widget mặc định không kích hoạt để không cướp focus;
    /// chỉ khi người dùng bấm vào ô nhập mới tạm bỏ cờ đó, và trả lại khi rời ô.
    /// </summary>
    protected void AllowTyping(bool allow)
    {
        var handle = new WindowInteropHelper(this).Handle;

        if (handle == IntPtr.Zero)
        {
            return;
        }

        const int exStyle = -20;
        const long noActivate = 0x08000000;
        var value = GetWindowLongPtr(handle, exStyle).ToInt64();
        value = allow ? value & ~noActivate : value | noActivate;
        SetWindowLongPtr(handle, exStyle, new IntPtr(value));

        if (allow)
        {
            Activate();
        }
    }

    protected static string IconText(string icon) =>
        icon.StartsWith(HabitIcon.EmojiPrefix, StringComparison.Ordinal) ? icon[HabitIcon.EmojiPrefix.Length..] : "•";

    /// <summary>WPF chỉ vẽ emoji đơn sắc: ép màu trắng để thấy rõ trên nền tối thay vì mặc định đen.</summary>
    protected static TextBlock Emoji(string emoji, double size) => new() { Text = emoji, FontSize = size, Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI Emoji") };

    protected static Border Bar(double fraction, double width, Color from, Color to, double height = 7)
    {
        var track = new Border { Height = height, Width = width, CornerRadius = new CornerRadius(height / 2), Background = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), HorizontalAlignment = HorizontalAlignment.Left };
        track.Child = new Border
        {
            CornerRadius = new CornerRadius(height / 2),
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = Math.Max(0, Math.Min(1, fraction)) * width,
            Background = new LinearGradientBrush(from, to, 0)
        };
        return track;
    }

    /// <summary>Vòng tiến độ tròn (0..1) quanh một phần tử ở giữa.</summary>
    protected static Grid Ring(double fraction, double size, double thickness, Color color, UIElement center)
    {
        var grid = new Grid { Width = size, Height = size };
        grid.Children.Add(new Ellipse { Stroke = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF)), StrokeThickness = thickness });

        var f = Math.Max(0, Math.Min(0.9999, fraction));

        if (f > 0)
        {
            var radius = (size - thickness) / 2;
            var middle = size / 2;
            var angle = f * 2 * Math.PI;
            var end = new Point(middle + radius * Math.Sin(angle), middle - radius * Math.Cos(angle));
            var figure = new PathFigure { StartPoint = new Point(middle, middle - radius) };
            figure.Segments.Add(new ArcSegment(end, new Size(radius, radius), 0, f > 0.5, SweepDirection.Clockwise, true));
            grid.Children.Add(new Path
            {
                Stroke = new SolidColorBrush(color),
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = new PathGeometry([figure])
            });
        }

        if (center is FrameworkElement fe)
        {
            fe.HorizontalAlignment = HorizontalAlignment.Center;
            fe.VerticalAlignment = VerticalAlignment.Center;
        }

        grid.Children.Add(center);
        return grid;
    }

    protected static Color Hex(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch (FormatException)
        {
            return Color.FromRgb(139, 92, 246);
        }
    }

    private static Brush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// Widget Buddy. Nhỏ gọn: vòng tiến độ hôm nay quanh Buddy. Vòng đổi màu theo ngữ cảnh: tím bình thường, vàng và nhấp nháy khi tối mà còn việc chưa xong
/// ("sắp mất chuỗi"), xanh khi xong hết. Mở rộng: tên, cấp độ, tiến độ và token đóng băng.
/// </summary>
public sealed class BuddyWidgetWindow : ExtraWidgetWindow
{
    private BuddyWidgetData? _data;

    public BuddyWidgetWindow() : base("HabitGrid Buddy")
    {
    }

    /// <summary>Giờ hiện tại (đặt được khi kiểm tra để cố định ngữ cảnh).</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    protected override Size CompactSize => new(92, 92);

    protected override Size ExpandedSize => new(230, 150);

    /// <summary>Ngữ cảnh hiển thị của Buddy.</summary>
    public enum Mood
    {
        Normal,
        AtRisk,
        AllDone
    }

    public static Mood MoodFor(BuddyWidgetData data, DateTime now) =>
        data.TodayDue > 0 && data.TodayDone >= data.TodayDue ? Mood.AllDone
        : data.TodayDue > 0 && now.Hour >= 18 && data.TodayDone < data.TodayDue ? Mood.AtRisk
        : Mood.Normal;

    public void Update(BuddyWidgetData data)
    {
        _data = data;
        Render();
    }

    protected override UIElement BuildCompact()
    {
        var data = _data!;
        var mood = MoodFor(data, Now());
        var color = mood switch
        {
            Mood.AllDone => Color.FromRgb(52, 211, 153),
            Mood.AtRisk => Color.FromRgb(251, 191, 36),
            _ => Color.FromRgb(167, 139, 250)
        };

        var fraction = data.TodayDue == 0 ? 0 : data.TodayFraction;
        var ring = Ring(mood == Mood.AllDone ? 1 : fraction, 76, 5, color, BuildAvatar(data, 0.62));

        if (mood == Mood.AtRisk)
        {
            ring.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, new Duration(TimeSpan.FromMilliseconds(900))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }

        var layout = new Grid();
        layout.Children.Add(ring);

        if (mood == Mood.AllDone)
        {
            var badge = Emoji("✔", 14);
            badge.Foreground = new SolidColorBrush(color);
            badge.HorizontalAlignment = HorizontalAlignment.Right;
            badge.VerticalAlignment = VerticalAlignment.Top;
            layout.Children.Add(badge);
        }

        return layout;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var avatar = BuildAvatar(data, 1);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        info.Children.Add(Text(data.Name, 18, bold: true));
        info.Children.Add(Text(HostText.BuddyLevel(data.Level), 12, opacity: 0.8));
        info.Children.Add(new Border { Height = 8 });
        info.Children.Add(Text(data.TodayDue == 0 ? HostText.NothingDue : $"{HostText.Today}  {HostText.Progress(data.TodayDone, data.TodayDue)}", 12, opacity: 0.9));
        info.Children.Add(new Border { Height = 4 });
        info.Children.Add(Bar(data.TodayFraction, 110, Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153)));

        if (data.FreezeTokens > 0)
        {
            info.Children.Add(new Border { Height = 6 });
            info.Children.Add(Text($"❄ {data.FreezeTokens}", 12, opacity: 0.8));
        }

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.Children.Add(avatar);
        Grid.SetColumn(info, 1);
        row.Children.Add(info);
        return row;
    }

    internal static Grid BuildAvatar(BuddyWidgetData data, double scale)
    {
        var avatar = new Grid { Width = 84 * scale, Height = 96 * scale };
        var bodySprite = BuddySprites.TryLoad(BuddySprites.StageId(data.Stage));
        FrameworkElement body = bodySprite is not null
            ? new System.Windows.Controls.Image { Source = bodySprite, Width = 72 * scale, Height = 72 * scale }
            : Emoji(StageEmoji(data.Stage), 58 * scale);
        body.HorizontalAlignment = HorizontalAlignment.Center;
        body.VerticalAlignment = VerticalAlignment.Center;
        avatar.Children.Add(body);

        foreach (var (slot, id, emoji) in data.Worn)
        {
            var sprite = BuddySprites.TryLoad(id);
            var size = (slot == AccessorySlot.Head ? 40 : 30) * scale;
            FrameworkElement piece = sprite is not null
                ? new System.Windows.Controls.Image { Source = sprite, Width = size, Height = size }
                : Emoji(emoji, (slot == AccessorySlot.Head ? 34 : 26) * scale);
            piece.HorizontalAlignment = HorizontalAlignment.Center;
            piece.VerticalAlignment = slot switch { AccessorySlot.Head => VerticalAlignment.Top, AccessorySlot.Face => VerticalAlignment.Center, _ => VerticalAlignment.Bottom };
            piece.Margin = slot switch
            {
                AccessorySlot.Head => new Thickness(0, -10 * scale, 0, 0),
                AccessorySlot.Face => new Thickness(0, 8 * scale, 0, 0),
                _ => new Thickness(0, 0, 0, -6 * scale)
            };
            avatar.Children.Add(piece);
        }

        return avatar;
    }

    private static string StageEmoji(BuddyStage stage) => stage switch
    {
        BuddyStage.Egg => "🥚",
        BuddyStage.Baby => "🐣",
        BuddyStage.Teen => "🐥",
        BuddyStage.Adult => "🦉",
        _ => "🐉"
    };
}

/// <summary>
/// Widget Mục tiêu. Nhỏ gọn: một dòng, tự luân phiên giữa ý định tuần và từng thử thách mỗi 5 giây. Mở rộng: toàn bộ ý định và tối đa 3 thử thách.
/// </summary>
public sealed class GoalsWidgetWindow : ExtraWidgetWindow
{
    private readonly DispatcherTimer _rotate = new() { Interval = TimeSpan.FromSeconds(5) };
    private GoalsWidgetData? _data;
    private int _index;

    public GoalsWidgetWindow() : base("HabitGrid Goals")
    {
        _rotate.Tick += (_, _) =>
        {
            _index++;

            if (!IsExpanded && _data is not null && Rotation(_data).Count > 1)
            {
                Render();
            }
        };
        _rotate.Start();
        Closed += (_, _) => _rotate.Stop();
    }

    protected override Size CompactSize => new(190, 52);

    protected override Size ExpandedSize => new(270, 190);

    public void Update(GoalsWidgetData data)
    {
        _data = data;
        Render();
    }

    /// <summary>Các mục luân phiên ở dạng nhỏ gọn: (nhãn, tiến độ 0..1, mô tả ngắn).</summary>
    public static IReadOnlyList<(string Label, double Fraction, string Detail)> Rotation(GoalsWidgetData data)
    {
        var items = new List<(string, double, string)>();

        if (data.Intent is { } intent)
        {
            items.Add(($"🎯 {intent.Name}", intent.Fraction, HostText.Progress(intent.Done, intent.Due)));
        }

        foreach (var c in data.Challenges)
        {
            items.Add(($"🏁 {c.Challenge.Title}", c.Fraction, HostText.GoalsDay(c.DayNumber, c.Challenge.Days)));
        }

        return items;
    }

    protected override UIElement BuildCompact()
    {
        var items = Rotation(_data!);
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (items.Count == 0)
        {
            stack.Children.Add(Text("🎯 " + HostText.GoalsTitle, 12, bold: true));
            stack.Children.Add(Text(HostText.GoalsNone, 11, opacity: 0.75));
            return stack;
        }

        var (label, fraction, detail) = items[_index % items.Count];
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(Text(label, 12, bold: true));
        var right = Text(detail, 11, opacity: 0.85);
        right.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(right, 1);
        head.Children.Add(right);
        stack.Children.Add(head);
        stack.Put(Bar(fraction, 168, Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 5), 6);
        return stack;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var stack = new StackPanel();
        stack.Put(Text("🎯 " + HostText.GoalsTitle, 14, bold: true));

        if (data.IsEmpty)
        {
            stack.Put(Text(HostText.GoalsEmpty, 12, opacity: 0.8, wrap: true));
        }

        if (data.Intent is { } intent)
        {
            stack.Put(Text($"{HostText.GoalsIntention}: {IconText(intent.Icon)} {intent.Name}", 12));
            stack.Put(Row(intent.Fraction, HostText.Progress(intent.Done, intent.Due), Hex(intent.ColorHue)));
        }

        foreach (var challenge in data.Challenges)
        {
            var emoji = ChallengeCatalog.Find(challenge.Challenge.TemplateId)?.Emoji ?? "🏁";
            stack.Put(Text($"{emoji} {challenge.Challenge.Title}", 12));
            stack.Put(Row(challenge.Fraction, HostText.GoalsDay(challenge.DayNumber, challenge.Challenge.Days), Color.FromRgb(139, 92, 246)));
        }

        return stack;
    }

    private static UIElement Row(double fraction, string label, Color accent)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Bar(fraction, 170, accent, Color.FromRgb(236, 72, 153)));
        var text = Text(label, 11, opacity: 0.85);
        text.Margin = new Thickness(8, 0, 0, 0);
        row.Children.Add(text);
        return row;
    }
}

/// <summary>
/// Widget Multi Grid. Nhỏ gọn: mỗi thói quen một dải 13 ngày gần nhất. Mở rộng: heatmap 13 tuần kèm tên và chuỗi.
/// </summary>
public sealed class MultiGridWidgetWindow : ExtraWidgetWindow
{
    private const double Cell = 8;
    private const double Gap = 2;
    private MultiGridData? _data;

    public MultiGridWidgetWindow() : base("HabitGrid Multi Grid")
    {
    }

    protected override Size CompactSize => new(146, 22 + Math.Max(1, _data?.Rows.Count ?? 1) * 12);

    protected override Size ExpandedSize
    {
        get
        {
            var count = Math.Max(1, _data?.Rows.Count ?? 1);
            return new Size(300, 34 + count * 70 + (count - 1) * 8);
        }
    }

    public void Update(MultiGridData data)
    {
        _data = data;
        Render();
        RefreshLayout();
    }

    /// <summary>13 ngày gần nhất tính đến hôm nay (bỏ các ô tương lai ở cuối lưới) của một hàng; dùng cho dạng nhỏ gọn.</summary>
    public static IReadOnlyList<int> LastDays(MultiGridRow row, int days = 13)
    {
        var today = -1;

        for (var i = row.Cells.Count - 1; i >= 0; i--)
        {
            if (row.Cells[i] != -1)
            {
                today = i;
                break;
            }
        }

        if (today < 0)
        {
            return [];
        }

        // Ô tương lai (-1) nằm sau hôm nay; ô trước ngày bắt đầu theo dõi cũng là -1 nhưng nằm trước, nên giữ nguyên.
        var take = Math.Min(days, today + 1);
        return row.Cells.Skip(today + 1 - take).Take(take).ToList();
    }

    /// <summary>Dải 13 ngày gần nhất của một thói quen (dùng ở dạng nhỏ gọn và trong widget gộp).</summary>
    internal static Canvas Strip(MultiGridRow row)
    {
        var strip = new Canvas { Width = 13 * (Cell + Gap), Height = Cell + 2 };
        var days = LastDays(row);

        for (var i = 0; i < days.Count; i++)
        {
            if (days[i] < 0)
            {
                continue;
            }

            var rect = new Rectangle { Width = Cell, Height = Cell, RadiusX = 2, RadiusY = 2, Fill = Fill(row, days[i]) };
            Canvas.SetLeft(rect, i * (Cell + Gap));
            Canvas.SetTop(rect, 1);
            strip.Children.Add(rect);
        }

        return strip;
    }

    protected override UIElement BuildCompact()
    {
        var data = _data!;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (data.Rows.Count == 0)
        {
            stack.Children.Add(Text(HostText.NothingDue, 11, opacity: 0.8));
            return stack;
        }

        foreach (var row in data.Rows)
        {
            stack.Put(Strip(row), 4);
        }

        return stack;
    }

    protected override UIElement BuildExpanded()
    {
        var data = _data!;
        var stack = new StackPanel();

        if (data.Rows.Count == 0)
        {
            stack.Put(Text(HostText.NothingDue, 12, opacity: 0.8));
        }

        foreach (var row in data.Rows)
        {
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
            line.ColumnDefinitions.Add(new ColumnDefinition());

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(Text($"{IconText(row.Icon)} {row.Name}", 12, bold: true));
            titles.Children.Add(Text($"🔥 {row.Streak}", 11, opacity: 0.8));
            line.Children.Add(titles);

            var map = Heatmap(row, data.Weeks);
            Grid.SetColumn(map, 1);
            line.Children.Add(map);
            stack.Put(line, 10);
        }

        return stack;
    }

    internal static Brush Fill(MultiGridRow row, int value) => value switch
    {
        1 => new SolidColorBrush(Hex(row.ColorHue)),
        2 => new SolidColorBrush(Color.FromRgb(56, 189, 248)),
        _ => new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF))
    };

    private static Canvas Heatmap(MultiGridRow row, int weeks)
    {
        var canvas = new Canvas { Width = weeks * (Cell + Gap), Height = 7 * (Cell + Gap), HorizontalAlignment = HorizontalAlignment.Right };

        for (var i = 0; i < row.Cells.Count; i++)
        {
            if (row.Cells[i] < 0)
            {
                continue;
            }

            var rect = new Rectangle { Width = Cell, Height = Cell, RadiusX = 2, RadiusY = 2, Fill = Fill(row, row.Cells[i]) };
            Canvas.SetLeft(rect, i / 7 * (Cell + Gap));
            Canvas.SetTop(rect, i % 7 * (Cell + Gap));
            canvas.Children.Add(rect);
        }

        return canvas;
    }
}

/// <summary>Phiên Focus đang chạy để hiện trên widget: <see cref="Display"/> là chữ trên viên thuốc, <see cref="Fraction"/> là tiến độ 0..1 của vòng.</summary>
public sealed record FocusSnapshot(string Display, double Fraction, string? Habit, bool Paused, bool IsBreak, string Clock = "");

/// <summary>Dữ liệu của widget gộp: Buddy, danh sách Hôm nay (check được), Mục tiêu và lưới thói quen (hai mục sau chỉ có khi Plus).</summary>
public sealed record HubData(BuddyWidgetData Buddy, IReadOnlyList<QuickItem> Today, GoalsWidgetData? Goals, MultiGridData? Grid, WidgetStatsData? Stats = null);
