using System.Windows.Controls;

namespace PulsePlugin.Views;

/// <summary>
/// Control bar ComboBox for selecting the Pulse beat rate divisor
/// (Every Beat / Every 2nd Beat / Every 3rd Beat / Every 4th Beat).
/// </summary>
public partial class BeatRateComboBox : UserControl
{
    public BeatRateComboBox()
    {
        InitializeComponent();
    }
}
