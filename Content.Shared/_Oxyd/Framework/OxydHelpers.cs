using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.Intrinsics;
using Content.Shared.Damage;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Whitelist;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Shared._Oxyd.Framework;


public static class DamageHelpers
{
    public static DamageSpecifier multiply(DamageSpecifier first, DamageSpecifier second)
    {
        var newSpec = new DamageSpecifier(first);
        foreach (var (damageType, multiplier) in first.DamageDict)
        {
            if (second.DamageDict.ContainsKey(damageType))
                newSpec.DamageDict[damageType] *= multiplier;
            else
                newSpec.DamageDict.Add(
                    damageType,
                    second.DamageDict.GetValueOrDefault(damageType) * multiplier
                );
        }

        return newSpec;
    }
}


public class SharedOxydHelpers : EntitySystem
{
    [Dependency] private  INetManager _netManager = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private  IConfigurationManager _config = default!;
    [Dependency] private  EntityWhitelistSystem _whitelistSystem = default!;
    public HashSet<EntityUid> queued = new HashSet<EntityUid>();

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(SharedTransformSystem));
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public float getRangeToPvsMultiplier(float range)
    {
        return 2*range / _config.GetCVar(CVars.NetMaxUpdateRange);
    }

    public void QueueDel(EntityUid uid)
    {
        if (_netManager.IsClient)
        {
            queued.Add(uid);
        }
        else
            EntityManager.QueueDeleteEntity(uid);
    }

    public bool GetParentWithComp<T>(EntityUid uid,[NotNullWhen(true)] out Entity<T>? ent) where T : Component
    {
        ent = null;
        var target = uid;
        while (!TerminatingOrDeleted(target))
        {
            if (TryComp<T>(target, out var comp))
            {
                ent = new Entity<T>(target, comp);
                return true;
            }
            if (!TryComp(uid, out TransformComponent? transform))
                break;
            if(HasComp<MapGridComponent>(target) || HasComp<MapComponent>(target))
                break;
            if (target == transform.ParentUid)
                break;
            target = transform.ParentUid;
        }
        return false;
    }
    public static bool checkIntersect(Vector2 p, Box2Rotated b)
    {
        var r = (-b.Rotation).RotateVec(p - b.Box.Center) + b.Box.Center;
        var t = b.Box;
        return r.X > t.Left && r.Y > t.Bottom && r.X < t.Right && r.Y < t.Top;
    }

    public static Box2 buildWorldBox(float x1, float y1, float x2, float y2)
    {
        return new Box2(x1 > x2 ? x2 : x1, y1 > y2 ? y2 : y1, x1 < x2 ? x2 : x1, y1 < y2 ? y2 : y1);
    }

    /// <summary>
    /// Returns a list of ItemSlots that the given item can be inserted into,
    /// checking ONLY whitelist and blacklist.
    /// </summary>
    public List<ItemSlot> GetValidSlots(EntityUid item, Entity<ItemSlotsComponent?> target)
    {
        var validSlots = new List<ItemSlot>();

        if (!Resolve(target, ref target.Comp, false))
            return validSlots;

        foreach (var (_, slot) in target.Comp.Slots)
        {
            if (_whitelistSystem.IsWhitelistFail(slot.Whitelist, item) ||
                _whitelistSystem.IsWhitelistPass(slot.Blacklist, item))
            {
                continue;
            }

            validSlots.Add(slot);
        }

        return validSlots.OrderByDescending(s => s.Priority).ToList();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        foreach (var uid in queued)
        {
            SetPaused(uid, true);
            _transform.DetachEntity(uid);
        }
        queued.Clear();
    }
}
