using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace HabitGrid.WinUI.Controls;

/// <summary>Nhóm chip chọn một (thay RadioButtons/ComboBox): chip được chọn đổ gradient. Tự xuống dòng khi hết chỗ.</summary>
public sealed class ChipGroup : UserControl
{
    private readonly ItemsRepeater _repeater = new();
    private readonly List<ToggleButton> _chips = [];
    private int _selected = -1;

    public ChipGroup()
    {
        IsTabStop = false;
        _repeater.Layout = new UniformGridLayout
        {
            MinItemWidth = 96,
            MinColumnSpacing = 8,
            MinRowSpacing = 8,
            ItemsStretch = UniformGridLayoutItemsStretch.Fill
        };
        _repeater.ItemsSource = _chips;
        Content = _repeater;
    }

    /// <summary>Đổi lựa chọn (do người dùng bấm).</summary>
    public event EventHandler? SelectionChanged;

    public double MinChipWidth
    {
        get => ((UniformGridLayout)_repeater.Layout).MinItemWidth;
        set => ((UniformGridLayout)_repeater.Layout).MinItemWidth = value;
    }

    public int SelectedIndex
    {
        get => _selected;
        set
        {
            _selected = value;

            for (var i = 0; i < _chips.Count; i++)
            {
                _chips[i].IsChecked = i == value;
            }
        }
    }

    public int Count => _chips.Count;

    public void Add(string text)
    {
        var index = _chips.Count;
        var chip = new ToggleButton
        {
            // Chữ tự xuống dòng thay vì bị cắt khi chip hẹp hơn nhãn (lỗi "Be Vietnam Pro (mặ", "Luôn trên cùng (khuyên dùn").
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.None },
            Style = (Style)Application.Current.Resources["ChipStyle"],
            IsChecked = index == _selected,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(chip, text);
        chip.Click += (_, _) =>
        {
            // Chọn một: bấm lại chip đang chọn thì giữ nguyên.
            var changed = _selected != index;
            SelectedIndex = index;

            if (changed)
            {
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        _chips.Add(chip);
        _repeater.ItemsSource = null;
        _repeater.ItemsSource = _chips;
    }
}
