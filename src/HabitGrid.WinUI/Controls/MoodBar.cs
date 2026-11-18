using HabitGrid.Presentation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace HabitGrid.WinUI.Controls;

/// <summary>
/// Thanh "Hôm nay bạn thấy sao?" (F4): 5 emoji một chạm (chạm lại để bỏ chọn), chọn rồi mới hiện ô ghi chú ngắn (≤ 140 ký tự)
/// lưu khi rời ô hoặc nhấn Enter. Dùng cho ngày đang xem (không cho ngày tương lai).
/// </summary>
public sealed class MoodBar : UserControl
{
    private static readonly string[] Faces = ["😞", "🙁", "😐", "🙂", "😄"];

    private readonly TextBlock _title = new();
    private readonly List<ToggleButton> _buttons = [];
    private readonly TextBox _note = new() { MaxLength = DailyMoodService.MaxNote, Visibility = Visibility.Collapsed };
    private readonly Border _root = new();
    private DateOnly _day;
    private int? _mood;
    private bool _loading;

    public MoodBar()
    {
        IsTabStop = false;
        _title.Style = (Style)Application.Current.Resources["BodySecondaryTextStyle"];

        // Năm cột bằng nhau co theo bề rộng ô (v3: ô Tâm trạng chỉ rộng 1 cột Bento; hàng ngang cố định làm mặt thứ 5 bị cắt).
        var row = new Grid { ColumnSpacing = 6 };

        for (var i = 0; i < Faces.Length; i++)
        {
            var value = i + 1;
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var button = new ToggleButton { Content = Faces[i], FontSize = 22, Padding = new Thickness(0, 2, 0, 2), MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, Style = (Style)Application.Current.Resources["ChipStyle"] };
            AutomationProperties.SetName(button, Faces[i]);
            button.Click += async (_, _) => await OnPickedAsync(value);
            Grid.SetColumn(button, i);
            _buttons.Add(button);
            row.Children.Add(button);
        }

        _note.LostFocus += async (_, _) => await SaveAsync();
        _note.KeyDown += async (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                await SaveAsync();
            }
        };

        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(_title);
        stack.Children.Add(row);
        stack.Children.Add(_note);

        _root.Style = (Style)Application.Current.Resources["CardBorder"];
        _root.Child = stack;
        Content = _root;
    }

    public async Task LoadAsync(DateOnly day)
    {
        _day = day;
        _loading = true;
        var current = await App.Host.Get<DailyMoodService>().GetAsync(day);
        _mood = current?.Mood;
        _note.Text = current?.Note ?? string.Empty;
        _title.Text = Ui.T(day == DateOnly.FromDateTime(DateTime.Today) ? "mood.title.today" : "mood.title.past");
        _note.PlaceholderText = Ui.T("mood.note");
        AutomationProperties.SetName(_note, Ui.T("mood.note"));
        Render();
        _loading = false;
    }

    /// <summary>Chọn một mức (dùng cho smoke test).</summary>
    public Task PickForTestAsync(int mood) => OnPickedAsync(mood);

    public int? SelectedMood => _mood;

    private async Task OnPickedAsync(int value)
    {
        if (_loading)
        {
            return;
        }

        _mood = _mood == value ? null : value;
        Render();
        await App.Host.Get<DailyMoodService>().SetAsync(_day, _mood, _note.Text);
    }

    private async Task SaveAsync()
    {
        if (_loading || _mood is null)
        {
            return;
        }

        await App.Host.Get<DailyMoodService>().SetAsync(_day, _mood, _note.Text);
    }

    private void Render()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].IsChecked = _mood == i + 1;
        }

        _note.Visibility = _mood is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
