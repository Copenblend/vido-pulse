using System.Windows.Controls;

namespace PulsePlugin.Views;

/// <summary>
/// Control bar ComboBox for selecting the Pulse beat rate divisor
/// (Every Beat / Every 2nd Beat / Every 3rd Beat / Every 4th Beat).
/// </summary>
public partial class BeatRateComboBox : UserControl
{
    /// <summary>Initializes a new instance of the beat-rate selector control.</summary>
    public BeatRateComboBox()
    {
        InitializeComponent();
    }
}
