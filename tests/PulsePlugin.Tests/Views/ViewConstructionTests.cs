using System.Runtime.ExceptionServices;
using PulsePlugin.Views;
using Xunit;

namespace PulsePlugin.Tests.Views;

public class ViewConstructionTests
{
    [Fact]
    public void BeatRateComboBox_CanBeConstructedOnStaThread()
    {
        RunOnSta(() =>
        {
            var view = new BeatRateComboBox();
            Assert.NotNull(view);
        });
    }

    [Fact]
    public void WaveformPanelView_CanBeConstructedOnStaThread()
    {
        RunOnSta(() =>
        {
            var view = new WaveformPanelView();
            Assert.NotNull(view);
        });
    }

    private static void RunOnSta(Action action)
    {
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
            ExceptionDispatchInfo.Capture(exception).Throw();
    }
}
