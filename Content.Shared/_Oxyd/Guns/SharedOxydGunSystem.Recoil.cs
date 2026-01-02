namespace Content.Shared._Oxyd.OxydGunSystem;

/// <summary>
/// This handles...
/// </summary>
public partial class SharedOxydGunSystem : EntitySystem
{
    public void InitRecoil()
    {
        SubscribeLocalEvent<RecoilHandlerComponent, ComponentStartup>(onStart);
        SubscribeLocalEvent<RecoilHandlerComponent, RecoilChangedEvent>(onChange);
        SubscribeLocalEvent<RecoilHandlerComponent, GunAfterFireIndividualProjectileEvent>(onFireGun);
        SubscribeLocalEvent<RecoilHandlerComponent, GunGetInaccuracyEvent>(OnRequestRecoil);
    }

    public void onStart(Entity<RecoilHandlerComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<ActiveRecoilHandlerComponent>(ent);
    }
    public static Angle getRecoilDeviation(float curRecoil, float maxRecoil , Angle maxDev)
    {
        return (curRecoil + 1) / maxRecoil * maxDev;
    }

    public void OnRequestRecoil(Entity<RecoilHandlerComponent> ent, ref GunGetInaccuracyEvent args)
    {
        if(_gameTiming.CurTick.Value - args.simTick.Value == 0)
            args.addedInaccuracy += getRecoilDeviation(ent.Comp.currentRecoil, ent.Comp.maxRecoil, ent.Comp.MaxDeviation);
    }

    public void onChange(Entity<RecoilHandlerComponent> ent, ref RecoilChangedEvent args)
    {
        ent.Comp.currentRecoil = Math.Clamp(args.currentRecoil, 0, ent.Comp.maxRecoil);
    }


    public void onFireGun(Entity<RecoilHandlerComponent> ent, ref GunAfterFireIndividualProjectileEvent args)
    {
        if (!TryComp<OxydBulletOnFireRecoilComponent>(args.projectile, out var rec))
            return;
        RaiseLocalEvent(ent, new RecoilChangedEvent()
        {
            oldRecoil = ent.Comp.currentRecoil,
            currentRecoil = ent.Comp.currentRecoil + rec.recoil,
            fromTick = args.simTick
        });
    }
    public void HandleActiveRecoil()
    {
        var entQ = EntityQueryEnumerator<ActiveRecoilHandlerComponent>();
        var recoilCheck = GetEntityQuery<RecoilHandlerComponent>();
        //var removeAfter = new List<EntityUid>(16);
        while (entQ.MoveNext(out var id, out var comp))
        {
            //comp.activeTicks--;
            //if(comp.activeTicks <= 0)
            //    removeAfter.Add(id);
            if (!recoilCheck.TryComp(id, out var handler))
                continue;
            var oldR = handler.currentRecoil;
            var ev = new RecoilGetModifiersEvent();
            RaiseLocalEvent(id,ev);
            float newR;
            if (ev.set is not null)
            {
                newR = ev.set.Value;
            }
            else
            {
                newR = ((handler.currentRecoil + ev.add) - handler.lossPerTick) * ev.multiply;
            }
            RaiseLocalEvent(id, new RecoilChangedEvent()
            {
                oldRecoil = oldR,
                currentRecoil = newR,
                fromTick = _gameTiming.CurTick
            });
        }
        /*
        foreach (var ent in removeAfter)
        {
            RemComp<ActiveRecoilHandlerComponent>(ent);
        }
        */
    }
}
