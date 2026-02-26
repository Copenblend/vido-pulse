using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PulsePlugin.Converters;

/// <summary>Converts a bool to <see cref="Visibility"/>. True = Visible, False = Collapsed.</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>
/// Converts a state color key string ("Green", "Yellow", "Grey", "Red") to a <see cref="SolidColorBrush"/>.
/// </summary>
internal sealed class StateColorToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Green = new(Color.FromRgb(0x4E, 0xC9, 0xB0));   // Teal green
    private static readonly SolidColorBrush Yellow = new(Color.FromRgb(0xDC, 0xDC, 0xAA));   // Gold
    private static readonly SolidColorBrush Grey = new(Color.FromRgb(0x60, 0x60, 0x60));      // Disabled
    private static readonly SolidColorBrush Red = new(Color.FromRgb(0xC4, 0x2B, 0x1C));       // Pulse red

    static StateColorToBrushConverter()
    {
        Green.Freeze();
        Yellow.Freeze();
        Grey.Freeze();
        Red.Freeze();
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "Green" => Green,
            "Yellow" => Yellow,
            "Red" => Red,
            _ => Grey
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a double (0.0–1.0) to percentage (0–100) for the progress bar.
/// </summary>
internal sealed class FractionToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d * 100.0 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d / 100.0 : 0.0;
}
