using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Timing;

namespace Content.Client._Oxyd.UI;

public sealed partial class ViewportFitterController : UIController
{
    [Dependency] private OxTagController tagging = default!;

    public override void Initialize()
    {
        
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        if (!tagging.map.TryGetValue("FitToParentViewport", out var targets) || targets.Count == 0)
            return;
        foreach (var target in targets)
        {
            if (target is not MainViewport view)
                continue;
            if (view.Parent is not Control sizeData)
                continue;
            var curControl = sizeData.Size;
            var curView = view.Viewport.ViewportSize * view.ViewportFixedScaleFactor;
            var ratio = curView.X / curControl.X;
            Vector2i newRatio = new();
            if (ratio > 1f)
            {
                newRatio.X = (int) (curControl.X / view.ViewportFixedScaleFactor);
                newRatio.Y = (int) (curControl.Y /  view.ViewportFixedScaleFactor);
                view.Viewport.ViewportSize = newRatio;
                Log.Debug($"Control size {sizeData} , compared to {curView}, {ratio} , newR {newRatio}" );
            }


        }
    }
}