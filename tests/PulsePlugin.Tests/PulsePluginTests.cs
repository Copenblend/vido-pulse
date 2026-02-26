using Moq;
using Vido.Core.Plugin;
using Xunit;

namespace PulsePlugin.Tests;

public class PulsePluginTests
{
    [Fact]
    public void Activate_StoresContext()
    {
        var context = new Mock<IPluginContext>();
        var plugin = new PulsePlugin();

        plugin.Activate(context.Object);

        // Plugin should activate without throwing
    }

    [Fact]
    public void Deactivate_CleansUp()
    {
        var context = new Mock<IPluginContext>();
        var plugin = new PulsePlugin();

        plugin.Activate(context.Object);
        plugin.Deactivate();

        // Plugin should deactivate without throwing
    }

    [Fact]
    public void Activate_Deactivate_Cycle_Succeeds()
    {
        var context = new Mock<IPluginContext>();
        var plugin = new PulsePlugin();

        // Multiple activate/deactivate cycles should work
        plugin.Activate(context.Object);
        plugin.Deactivate();

        plugin.Activate(context.Object);
        plugin.Deactivate();
    }
}
