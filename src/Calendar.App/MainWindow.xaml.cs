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
