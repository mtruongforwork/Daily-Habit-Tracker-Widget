using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HabitGrid.Core.Domain.Moments;
using HabitGrid.Platform.Desktop;

namespace HabitGrid.WidgetHost;

/// <summary>
/// Widget "Khoảnh khắc" kiểu Locket (Plus): ảnh khoảnh khắc mới nhất trên desktop kèm tên thói quen và ngày;
/// bấm để đổi sang "ngày này tháng trước". Ảnh nạp vào bộ nhớ (không giữ khóa file) ở kích thước nhỏ.
/// </summary>
public sealed class MomentWidgetWindow : Window
{
    private readonly Image _image = new() { Stretch = Stretch.UniformToFill };
    private readonly TextBlock _placeholder = new() { FontSize = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Text = "📷" };
    private readonly TextBlock _title = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _subtitle = new() { FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) };
    private Moment? _latest;
    private Moment? _monthAgo;
    private IReadOnlyDictionary<Guid, string> _names = new Dictionary<Guid, string>();
    private bool _showingMonthAgo;

    public MomentWidgetWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 160;
        Height = 190;
        Title = "HabitGrid Moments";

        var photo = new Grid { ClipToBounds = true };
        photo.Children.Add(new Border { Background = new LinearGradientBrush(Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153), 45) });
        photo.Children.Add(_placeholder);
        photo.Children.Add(_image);

        var caption = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12, 0, 12, 10) };
        caption.Children.Add(_title);
        caption.Children.Add(_subtitle);

        var shade = new Border
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Height = 70,
            Background = new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 0, 0, 0), 90)
        };

        var root = new Grid();
        root.Children.Add(photo);
        root.Children.Add(shade);
        root.Children.Add(caption);

        Content = new Border
        {
            CornerRadius = new CornerRadius(22),
            BorderThickness = new Thickness(3),
            BorderBrush = Brushes.White,
            Background = Brushes.Black,
            Child = new Border { CornerRadius = new CornerRadius(19), ClipToBounds = true, Child = root }
        };

        Cursor = Cursors.Hand;
        MouseLeftButtonDown += (_, e) =>
        {
            if (EditMode || Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            {
                try
                {
                    DragMove();
                    Moved?.Invoke(this, EventArgs.Empty);
                }
                catch (InvalidOperationException)
                {
                    // Nhả chuột trước khi kéo.
                }

                return;
            }

            if (_monthAgo is not null)
            {
                _showingMonthAgo = !_showingMonthAgo;
                Render();
            }
        };
    }

    public bool EditMode { get; set; }

    public event EventHandler? Moved;

    public void Update(Moment? latest, Moment? monthAgo, IReadOnlyDictionary<Guid, string> habitNames)
    {
        var changed = latest?.Path != _latest?.Path || monthAgo?.Path != _monthAgo?.Path;
        _latest = latest;
        _monthAgo = monthAgo;
        _names = habitNames;

        if (_monthAgo is null)
        {
            _showingMonthAgo = false;
        }

        if (changed)
        {
            Render();
        }
    }

    public DesktopHostResult Pin(DesktopHostService host, DesktopPinningMode mode) =>
        host.Attach(new WindowInteropHelper(this).EnsureHandle(), mode);

    private void Render()
    {
        var moment = _showingMonthAgo ? _monthAgo : _latest;

        if (moment is null)
        {
            _image.Source = null;
            _placeholder.Visibility = Visibility.Visible;
            _title.Text = HostText.MomentEmpty;
            _subtitle.Text = HostText.MomentEmptyHint;
            return;
        }

        _image.Source = Load(moment.Path);
        _placeholder.Visibility = _image.Source is null ? Visibility.Visible : Visibility.Collapsed;
        _title.Text = _names.TryGetValue(moment.HabitId, out var name) ? name : string.Empty;
        _subtitle.Text = (_showingMonthAgo ? HostText.MomentMonthAgo + " · " : string.Empty)
                         + moment.Day.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)
                         + (_monthAgo is not null ? "   ⇄" : string.Empty);
        ToolTip = _monthAgo is not null ? HostText.MomentToggleHint : null;
    }

    private static BitmapImage? Load(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 440;
            image.StreamSource = new MemoryStream(File.ReadAllBytes(path));
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
