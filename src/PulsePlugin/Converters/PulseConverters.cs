using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PulsePlugin.Converters;

/// <summary>Converts a bool to <see cref="Visibility"/>. True = Visible, False = Collapsed.</summary>
internal sealed class BoolToVisibilityConverter : IValueConverter
{
    /// <summary>Converts a boolean value to a <see cref="Visibility"/> value.</summary>
    /// <param name="value">Input value expected to be a boolean.</param>
    /// <param name="targetType">Requested target type.</param>
    /// <param name="parameter">Optional converter parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns><see cref="Visibility.Visible"/> when true; otherwise <see cref="Visibility.Collapsed"/>.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Converts a <see cref="Visibility"/> value back to a boolean.</summary>
    /// <param name="value">Input value expected to be a <see cref="Visibility"/>.</param>
    /// <param name="targetType">Requested target type.</param>
    /// <param name="parameter">Optional converter parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>True only when value is <see cref="Visibility.Visible"/>.</returns>
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

    /// <summary>Converts a state color key to a frozen <see cref="SolidColorBrush"/> instance.</summary>
    /// <param name="value">State key string (for example: Green, Yellow, Red).</param>
    /// <param name="targetType">Requested target type.</param>
    /// <param name="parameter">Optional converter parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>A brush matching the supplied key; defaults to Grey.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value as string) switch
        {
            "Green" => Green,
            "Yellow" => Yellow,
            "Red" => Red,
            _ => Grey
        };

    /// <summary>Reverse conversion is not supported.</summary>
    /// <param name="value">Input value.</param>
    /// <param name="targetType">Requested target type.</param>
    /// <param name="parameter">Optional converter parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always thrown for reverse conversion attempts.</exception>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Converts a double (0.0–1.0) to percentage (0–100) for the progress bar.
/// </summary>
internal sealed class FractionToPercentConverter : IValueConverter
{
    /// <summary>Converts a fraction from 0.0–1.0 to a percentage from 0–100.</summary>
    /// <param name="value">Input fraction.</param>
    /// <param name="targetType">Requested target type.</param>
    /// <param name="parameter">Optional converter parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>Percentage value as double.</returns>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d * 100.0 : 0.0;

    /// <summary>Converts a percentage from 0–100 back to a fraction from 0.0–1.0.</summary>
    /// <param name="value">Input percentage.</param>
    /// <param name="targetType">Requested target type.</param>
    /// <param name="parameter">Optional converter parameter.</param>
    /// <param name="culture">Culture information.</param>
    /// <returns>Fraction value as double.</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d / 100.0 : 0.0;
}
