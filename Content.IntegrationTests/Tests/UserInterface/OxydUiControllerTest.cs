using Content.Client;
using Content.Client.UserInterface.Systems.Viewport;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Robust.Client.UserInterface;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.UserInterface;

[TestFixture]
public sealed class OxydUiControllerTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = false };

    [Test]
    public async Task DisconnectedControllersAndResize()
    {
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            var scaler = ui.GetUIController<UIStyler>();
            Assert.DoesNotThrow(() =>
                ui.GetUIController<ViewportUIController>().FrameUpdate(new FrameEventArgs(0)));
            Assert.DoesNotThrow(() => scaler.FrameUpdate(new FrameEventArgs(0)));
        });
    }
}
