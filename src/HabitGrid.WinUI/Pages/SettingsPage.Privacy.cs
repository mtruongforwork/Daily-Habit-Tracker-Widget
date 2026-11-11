using HabitGrid.Data;
using HabitGrid.Data.V3;
using HabitGrid.Presentation.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace HabitGrid.WinUI.Pages;

/// <summary>
/// Cài đặt → Quyền riêng tư &amp; dữ liệu: nơi dữ liệu nằm, và nút xóa toàn bộ dữ liệu trên máy (quyền yêu cầu xóa dữ liệu).
/// Xóa phải gõ từ xác nhận; sau khi xóa app đóng lại để lần mở sau bắt đầu như mới cài.
/// </summary>
public sealed partial class SettingsPage
{
    private void BuildPrivacyCard()
    {
        var card = Card(Ui.T("privacy.title"));
        card.Children.Add(new TextBlock { Text = Ui.Loc.Format("privacy.where", AppPaths.DataDir), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        card.Children.Add(new TextBlock { Text = Ui.T("privacy.nosend"), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });

        var erase = new Button { Content = Ui.T("privacy.erase"), Style = Res("GhostButtonStyle"), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(erase, Ui.T("privacy.erase"));
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed, Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["WarningBrush"] };
        erase.Click += async (_, _) =>
        {
            try
            {
                if (!await ConfirmEraseAsync())
                {
                    return;
                }

                await EraseEverythingAsync();
            }
            catch (Exception ex)
            {
                App.Host.Get<IAppLogger>().Error("Xóa dữ liệu lỗi", ex);
                status.Text = Ui.T("privacy.erase.failed");
                status.Visibility = Visibility.Visible;
            }
        };

        card.Children.Add(erase);
        card.Children.Add(status);
        Wrap(card);
    }

    /// <summary>Hộp thoại xác nhận: phải gõ đúng từ xác nhận thì nút xóa mới bấm được. Mặc định đóng (không bấm nhầm bằng Enter).</summary>
    private async Task<bool> ConfirmEraseAsync()
    {
        var word = Ui.T("privacy.erase.word");
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock { Text = Ui.T("privacy.erase.body"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(new TextBlock { Text = Ui.Loc.Format("privacy.erase.type", word), Style = Res("BodySecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        var input = new TextBox { PlaceholderText = word, MaxLength = 20 };
        AutomationProperties.SetName(input, Ui.Loc.Format("privacy.erase.type", word));
        body.Children.Add(input);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Ui.T("privacy.erase.title"),
            Content = body,
            PrimaryButtonText = Ui.T("privacy.erase.confirm"),
            CloseButtonText = Ui.T("plus.close"),
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = false
        };
        input.TextChanged += (_, _) => dialog.IsPrimaryButtonEnabled = string.Equals(input.Text.Trim(), word, StringComparison.CurrentCultureIgnoreCase);

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>Xóa dữ liệu trong CSDL, ảnh khoảnh khắc, bản sao lưu và nhật ký trên máy; rồi thông báo và đóng app.</summary>
    private async Task EraseEverythingAsync()
    {
        await DataEraser.EraseAllAsync(App.Host.Get<HabitGridDatabase>());

        var backups = App.Host.Get<BackupManager>().Directory;
        var folders = new[] { Path.Combine(AppPaths.DataDir, "moments"), backups, AppPaths.LogDir };

        foreach (var folder in folders)
        {
            DeleteFolderContents(folder);
        }

        await new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = Ui.T("privacy.erase.done.title"),
            Content = new TextBlock { Text = Ui.T("privacy.erase.done"), TextWrapping = TextWrapping.Wrap },
            CloseButtonText = Ui.T("plus.close")
        }.ShowAsync();

        Application.Current.Exit();
    }

    /// <summary>Xóa từng file trong thư mục (file đang bị giữ như nhật ký của lần chạy này thì bỏ qua, không làm hỏng cả quá trình).</summary>
    private static void DeleteFolderContents(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Đang được dùng: để lại, không chặn các file còn lại.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
