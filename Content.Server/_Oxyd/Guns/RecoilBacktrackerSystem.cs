using System.Linq;
using Content.Shared._Oxyd.OxydGunSystem;
using Robust.Shared.Timing;

namespace Content.Server._Oxyd.Guns;

/// <summary>
/// This handles...
/// </summary>
public sealed class RecoilBacktrackerSystem : EntitySystem
{
    [Dependency] public readonly IGameTiming _timing = default!;
    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerRecoilBacktrackerComponent, RecoilChangedEvent>(OnRecoilChange);
        SubscribeLocalEvent<PlayerRecoilBacktrackerComponent, ComponentStartup>(OnStart);
        SubscribeLocalEvent<PlayerRecoilBacktrackerComponent, GunGetInaccuracyEvent>(OnRequestRecoil);

    }

    public void OnRequestRecoil(Entity<PlayerRecoilBacktrackerComponent> ent, ref GunGetInaccuracyEvent args)
    {
        // same tick is handled by normal recoil!
        if (_timing.CurTick.Value - args.simTick.Value == 0)
            return;
        if (!TryComp<RecoilHandlerComponent>(ent, out var rcomp))
            return;
        args.addedInaccuracy += SharedOxydGunSystem.getRecoilDeviation(rcomp.currentRecoil, rcomp.maxRecoil, rcomp.MaxDeviation);
    }

    public void OnStart(Entity<PlayerRecoilBacktrackerComponent> ent, ref ComponentStartup args)
    {
        if (!TryComp<RecoilHandlerComponent>(ent, out var recoil))
            return;
        var ticksToDo = (uint)ServerOxydGunSystem.MaxTicksIncosistencyBehind;
        while (ticksToDo > 0)
        {
            ent.Comp.recoils.Add(_timing.CurTick.Value-ticksToDo, recoil.currentRecoil);
            ticksToDo--;
        }
    }

    public void OnRecoilChange(Entity<PlayerRecoilBacktrackerComponent> ent, ref RecoilChangedEvent args)
    {
        if (!TryComp<RecoilHandlerComponent>(ent, out var recoilCom))
            return;
        var tickDiff = _timing.CurTick.Value - args.fromTick.Value;
        if (tickDiff > 0 && ent.Comp.recoils.ContainsKey(args.fromTick.Value))
        {
            Log.Debug($"Before with tick diff of {tickDiff}");
            var outstf = ent.Comp.recoils.Select((key, value) => { return $"k:{key.Key} v:{key.Value} ";});
            Log.Debug($"{string.Join("", outstf)}");

            var deltaDiff = ent.Comp.recoils[args.fromTick.Value] - args.currentRecoil;

            ent.Comp.recoils[args.fromTick.Value] = args.currentRecoil;
            tickDiff--;
            while (tickDiff > 0)
            {
                var recoilLoss = 0f;
                var targetTick = _timing.CurTick.Value - tickDiff;
                if (tickDiff > 1)
                {
                    var stepChange = ent.Comp.recoils[targetTick] - ent.Comp.recoils[targetTick + 1];
                    if (stepChange < recoilCom.lossPerTick)
                        recoilLoss = recoilCom.lossPerTick - stepChange;
                }

                ent.Comp.recoils[targetTick] += Math.Clamp(deltaDiff - recoilLoss, 0, recoilCom.maxRecoil);
                tickDiff--;
            }
            Log.Debug("After");
            var outsta = ent.Comp.recoils.Select((key, value) => { return $"k:{key.Key} v:{key.Value} ";});
            Log.Debug($"{string.Join("", outsta)}");


        }
        else
        {
            if (ent.Comp.recoils.ContainsKey(args.fromTick.Value))
                ent.Comp.recoils[args.fromTick.Value] = Math.Clamp(args.currentRecoil, 0, recoilCom.maxRecoil);
            else
                ent.Comp.recoils.Add(args.fromTick.Value, Math.Clamp(args.currentRecoil, 0, recoilCom.maxRecoil));
        }

        if (ent.Comp.recoils.Count > ServerOxydGunSystem.MaxTicksIncosistencyBehind * 2)
        {
            ent.Comp.recoils = ent.Comp.recoils.Where((pair, i) =>
            {
                return !(_timing.CurTick.Value - pair.Key > ServerOxydGunSystem.MaxTicksIncosistencyBehind);
            }).ToDictionary();
        }
    }

}
