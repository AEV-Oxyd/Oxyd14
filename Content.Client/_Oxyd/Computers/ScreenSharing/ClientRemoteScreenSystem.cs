using Content.Shared._Oxyd.Computers.ScreenSharing;

namespace Content.Client._Oxyd.Computers.ScreenSharing;

public sealed partial class ClientRemoteScreenSystem : SharedRemoteScreenSystem
{
    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        PipScreenSurface.BeginUiFrame();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        PipScreenSurface.FlushDeferredDisposals();
    }
}
