using Calendar.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Calendar.App;

public sealed partial class MainWindow : Window
{
    public ShellViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
    }
}
