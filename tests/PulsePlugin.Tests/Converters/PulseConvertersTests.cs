using System.Globalization;
using System.Windows;
using System.Windows.Media;
using PulsePlugin.Converters;
using Xunit;

namespace PulsePlugin.Tests.Converters;

public class PulseConvertersTests
{
    [Fact]
    public void BoolToVisibilityConverter_Convert_TrueReturnsVisible()
    {
        var sut = new BoolToVisibilityConverter();

        var result = sut.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void BoolToVisibilityConverter_Convert_FalseReturnsCollapsed()
    {
        var sut = new BoolToVisibilityConverter();

        var result = sut.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture);

        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void BoolToVisibilityConverter_ConvertBack_VisibleReturnsTrue()
    {
        var sut = new BoolToVisibilityConverter();

        var result = sut.ConvertBack(Visibility.Visible, typeof(bool), null!, CultureInfo.InvariantCulture);

        Assert.Equal(true, result);
    }

    [Fact]
    public void StateColorToBrushConverter_Convert_UsesExpectedColorKeys()
    {
        var sut = new StateColorToBrushConverter();

        var green = (SolidColorBrush)sut.Convert("Green", typeof(SolidColorBrush), null!, CultureInfo.InvariantCulture);
        var yellow = (SolidColorBrush)sut.Convert("Yellow", typeof(SolidColorBrush), null!, CultureInfo.InvariantCulture);
        var red = (SolidColorBrush)sut.Convert("Red", typeof(SolidColorBrush), null!, CultureInfo.InvariantCulture);
        var grey = (SolidColorBrush)sut.Convert("Unknown", typeof(SolidColorBrush), null!, CultureInfo.InvariantCulture);

        Assert.Equal(Color.FromRgb(0x4E, 0xC9, 0xB0), green.Color);
        Assert.Equal(Color.FromRgb(0xDC, 0xDC, 0xAA), yellow.Color);
        Assert.Equal(Color.FromRgb(0xC4, 0x2B, 0x1C), red.Color);
        Assert.Equal(Color.FromRgb(0x60, 0x60, 0x60), grey.Color);
    }

    [Fact]
    public void StateColorToBrushConverter_ConvertBack_ThrowsNotSupported()
    {
        var sut = new StateColorToBrushConverter();

        Assert.Throws<NotSupportedException>(() =>
            sut.ConvertBack(new SolidColorBrush(Colors.White), typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FractionToPercentConverter_Convert_ConvertsFractionToPercent()
    {
        var sut = new FractionToPercentConverter();

        var result = sut.Convert(0.35d, typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Equal(35.0d, result);
    }

    [Fact]
    public void FractionToPercentConverter_ConvertBack_ConvertsPercentToFraction()
    {
        var sut = new FractionToPercentConverter();

        var result = sut.ConvertBack(25.0d, typeof(double), null!, CultureInfo.InvariantCulture);

        Assert.Equal(0.25d, result);
    }
}
