using Calendar.App.Helpers;
using Calendar.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.UI;

namespace Calendar.App.ViewModels;

/// <summary>One account in the sidebar with its calendars.</summary>
public sealed class AccountGroupViewModel(Account account, IReadOnlyList<CalendarItemViewModel> calendars)
{
    public Account Account { get; } = account;
    public string Email => Account.Email;
    public IReadOnlyList<CalendarItemViewModel> Calendars { get; } = calendars;
}

/// <summary>A color the user can pick for a calendar.</summary>
public sealed class ColorSwatchViewModel(string? hex, string name, Color color, IRelayCommand select)
{
    /// <summary>Null = back to the provider color.</summary>
    public string? Hex { get; } = hex;
    public string Name { get; } = name;
    public Color Color { get; } = color;
    public IRelayCommand Select { get; } = select;
}

/// <summary>
/// A calendar row: checkbox (shown or not) and color dot. Changes are reported to
/// <paramref name="onChanged"/>, which persists them as local preferences.
/// </summary>
public sealed partial class CalendarItemViewModel : ObservableObject
{
    /// <summary>Google Calendar's own palette, so picked colors feel familiar.</summary>
    private static readonly (string Hex, string Name)[] Palette =
    [
        ("#D50000", "Cà chua"), ("#E67C73", "Hồng hạc"), ("#F4511E", "Quýt"), ("#F6BF26", "Chuối"),
        ("#33B679", "Xô thơm"), ("#0B8043", "Húng quế"), ("#039BE5", "Công"), ("#3F51B5", "Việt quất"),
        ("#7986CB", "Oải hương"), ("#8E24AA", "Nho"), ("#616161", "Than chì"),
    ];

    private readonly Func<CalendarItemViewModel, Task>? _onChanged;

    public CalendarItemViewModel(CalendarInfo info, Func<CalendarItemViewModel, Task> onChanged)
    {
        Info = info;
        // Set backing state before hooking the callback, so building the row does not trigger a save.
        IsEnabled = info.IsEnabled;
        ColorOverride = info.ColorOverride;
        _onChanged = onChanged;

        var swatches = Palette
            .Select(p => new ColorSwatchViewModel(p.Hex, p.Name, ColorHex.Parse(p.Hex), new RelayCommand(() => SetColor(p.Hex))))
            .ToList();
        swatches.Insert(0, new ColorSwatchViewModel(null, "Màu gốc", ColorHex.Parse(info.Color), new RelayCommand(() => SetColor(null))));
        Swatches = swatches;
    }

    public CalendarInfo Info { get; }
    public string Name => Info.Name;
    public IReadOnlyList<ColorSwatchViewModel> Swatches { get; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; }

    public string? ColorOverride { get; private set; }

    public Color Color => ColorHex.Parse(ColorOverride ?? Info.Color);

    partial void OnIsEnabledChanged(bool value) => _onChanged?.Invoke(this);

    private void SetColor(string? hex)
    {
        if (hex == ColorOverride)
            return;
        ColorOverride = hex;
        OnPropertyChanged(nameof(Color));
        _onChanged?.Invoke(this);
    }
}
