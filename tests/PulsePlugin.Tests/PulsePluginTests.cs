using Moq;
using Vido.Core.Events;
using Vido.Core.Logging;
using Vido.Core.Playback;
using Vido.Core.Plugin;
using Xunit;

namespace PulsePlugin.Tests;

public class PulsePluginTests
{
    private static Mock<IPluginContext> CreateMockContext()
    {
        var context = new Mock<IPluginContext>();

        var videoEngine = new Mock<IVideoEngine>();
        videoEngine.Setup(v => v.CurrentMetadata).Returns((VideoMetadata?)null);
        context.Setup(c => c.VideoEngine).Returns(videoEngine.Object);

        var eventBus = new Mock<IEventBus>();
        eventBus.Setup(e => e.Subscribe(It.IsAny<Action<VideoLoadedEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        eventBus.Setup(e => e.Subscribe(It.IsAny<Action<VideoUnloadedEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        eventBus.Setup(e => e.Subscribe(It.IsAny<Action<PlaybackStateChangedEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        eventBus.Setup(e => e.Subscribe(It.IsAny<Action<Vido.Haptics.HapticTransportStateEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        eventBus.Setup(e => e.Subscribe(It.IsAny<Action<Vido.Haptics.HapticScriptsChangedEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        eventBus.Setup(e => e.Subscribe(It.IsAny<Action<Vido.Haptics.HapticAxisConfigEvent>>()))
            .Returns(Mock.Of<IDisposable>());
        context.Setup(c => c.Events).Returns(eventBus.Object);

        var logger = new Mock<ILogService>();
        context.Setup(c => c.Logger).Returns(logger.Object);

        var settings = new Mock<IPluginSettingsStore>();
        context.Setup(c => c.Settings).Returns(settings.Object);

        return context;
    }

    [Fact]
    public void Activate_StoresContext()
    {
        var context = CreateMockContext();
        var plugin = new PulsePlugin();

        plugin.Activate(context.Object);

        // Plugin should activate without throwing
    }

    [Fact]
    public void Deactivate_CleansUp()
    {
        var context = CreateMockContext();
        var plugin = new PulsePlugin();

        plugin.Activate(context.Object);
        plugin.Deactivate();

        // Plugin should deactivate without throwing
    }

    [Fact]
    public void Activate_Deactivate_Cycle_Succeeds()
    {
        var context = CreateMockContext();
        var plugin = new PulsePlugin();

        // Multiple activate/deactivate cycles should work
        plugin.Activate(context.Object);
        plugin.Deactivate();

        plugin.Activate(context.Object);
        plugin.Deactivate();
    }
}
