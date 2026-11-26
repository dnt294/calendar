using Calendar.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace Calendar.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; }

    public MainWindow(ShellViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Shell.IsPaneOpen = !Shell.IsPaneOpen;

    private async void OnRemoveAccountClick(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not AccountGroupViewModel group || !ViewModel.IsIdle)
            return;

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "Xoá tài khoản?",
            Content = $"Xoá {group.Email} khỏi app: thu hồi quyền truy cập, xoá token và lịch đã lưu trên máy.\n\n" +
                      "Lịch trên Google không bị ảnh hưởng. Bạn có thể thêm lại tài khoản này bất cứ lúc nào.",
            PrimaryButtonText = "Xoá",
            CloseButtonText = "Huỷ",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await ViewModel.RemoveAccountCommand.ExecuteAsync(group);
    }

    /// <summary>Closes the palette flyout after a color is picked (the command does the work).</summary>
    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(((FrameworkElement)sender).XamlRoot))
        {
            if (popup.Child is FlyoutPresenter)
                popup.IsOpen = false;
        }
    }
}
