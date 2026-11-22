using Content.Server._Oxyd.Computers.ScreenSharing;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Power.Pow3r;
using Content.Shared._Oxyd.Computers;
using Content.Shared._Oxyd.Computers.Packets;
using Content.Shared.Access.Systems;
using Content.Shared.Administration.Logs;
using Content.Shared.APC;
using Content.Shared.Database;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.DeviceLinking.Systems;
using Content.Shared.Emag.Systems;
using Content.Shared.Emp;
using Content.Shared.Lock;
using Content.Shared.Popups;
using Content.Shared.Power;
using Content.Shared.Rounding;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.Power.EntitySystems;

/// <summary>
/// A system for interactions with APCs.
/// APCs are wall-mounted battery banks and breakers for stepping down MV to LV power.
/// </summary>
/// <seealso cref="ApcComponent"/>
public sealed partial class ApcSystem : EntitySystem
{
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private IGameTiming _gameTiming = default!;
    [Dependency] private EmagSystem _emag = default!;
    [Dependency] private LockSystem _lock = default!;
    [Dependency] private DeviceLinkSystem _deviceLink = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private ServerRemoteScreenSystem _remoteScreen = default!;

    private static readonly ProtoId<SinkPortPrototype> PortApcCommandSink = "ApcCommandSink";
    private static readonly ProtoId<SinkPortPrototype> PortMachineCommandReceiver = "MachineCommandReceiver";
    private static readonly ProtoId<SourcePortPrototype> PortApcStatusSource = "ApcStatusSource";
    private static readonly ProtoId<SourcePortPrototype> PortMachineStatusSender = "MachineStatusSender";

    /// <inheritdoc />
    public override void Initialize()
    {
        base.Initialize();

        UpdatesAfter.Add(typeof(PowerNetSystem));
        SubscribeLocalEvent<ApcComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<ApcComponent, SignalReceivedEvent<ComputerDeviceLinkPayload>>(OnComputerDeviceLinkSignal);
        SubscribeLocalEvent<ApcComponent, SignalReceivedEvent<ComputerPacket>>(OnComputerPacketSignal);
    }

    private void OnSignalReceived(Entity<ApcComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != PortApcCommandSink && args.Port != PortMachineCommandReceiver)
            return;

        ApcToggleBreaker(ent.Owner, ent.Comp, user: args.Trigger);
    }

    private void OnComputerPacketSignal(Entity<ApcComponent> ent, ref SignalReceivedEvent<ComputerPacket> args)
    {
        EntityUid? actor = args.Data.ProtectedReferences.TryGetValue("Actor", out var actUid) ? actUid : args.Trigger;
        HandleDeviceLinkCommand(ent, args.Data.PacketType, args.Data.GetString("Argument", string.Empty) ?? string.Empty, actor);
    }

    private void OnComputerDeviceLinkSignal(Entity<ApcComponent> ent, ref SignalReceivedEvent<ComputerDeviceLinkPayload> args)
    {
        HandleDeviceLinkCommand(ent, args.Data.Command, args.Data.Argument, args.Trigger);
    }

    private void HandleDeviceLinkCommand(Entity<ApcComponent> ent, string? rawCmd, string rawArg, EntityUid? triggerOrActor)
    {
        var cmd = rawCmd?.Trim().ToLowerInvariant() ?? string.Empty;
        if (cmd is "status" or "status_request")
        {
            EmitApcStatusPacket(ent.Owner, ent.Comp);
            return;
        }

        if (cmd is "apc_toggle_breaker" or "toggle_breaker" or "toggle" or "breaker")
        {
            if (_lock.IsLocked(ent.Owner))
            {
                EmitApcStatusPacket(ent.Owner, ent.Comp);
                return;
            }

            if (!HasComp<LockComponent>(ent.Owner) && !IsDeviceLinkActorAllowed(ent.Owner, triggerOrActor))
                return;

            ApcToggleBreaker(ent.Owner, ent.Comp, user: triggerOrActor);
        }
        else if (cmd is "apc_set_breaker" or "set_breaker")
        {
            if (_lock.IsLocked(ent.Owner))
            {
                EmitApcStatusPacket(ent.Owner, ent.Comp);
                return;
            }

            var wantEnabled = rawArg is "1" or "true" or "on";
            if (ent.Comp.MainBreakerEnabled != wantEnabled)
            {
                if (!HasComp<LockComponent>(ent.Owner) && !IsDeviceLinkActorAllowed(ent.Owner, triggerOrActor))
                    return;

                ApcToggleBreaker(ent.Owner, ent.Comp, user: triggerOrActor);
            }
        }
    }

    private bool IsDeviceLinkActorAllowed(EntityUid apcUid, EntityUid? actor)
    {
        return actor == null ||
               actor == apcUid ||
               (TryComp<EmbeddedComputerHostComponent>(apcUid, out var hostComp) && hostComp.ComputerEntity == actor) ||
               _accessReader.IsAllowed(actor.Value, apcUid);
    }

    private void EmitApcStatusPacket(EntityUid uid, ApcComponent apc, PowerNetworkBatteryComponent? netBat = null)
    {
        if (TerminatingOrDeleted(uid)
            || !TryComp<DeviceLinkSourceComponent>(uid, out var srcComp)
            || !Resolve(uid, ref netBat, false))
            return;

        var battery = netBat.NetworkBattery;
        var pct = battery.Capacity > 0 ? Math.Clamp(battery.CurrentStorage / battery.Capacity * 100f, 0f, 100f) : 0f;

        var pkt = new ComputerPacket("ApcStatus");
        pkt.Set("AreaName", MetaData(uid).EntityName);
        pkt.Set("MainBreaker", apc.MainBreakerEnabled.ToString());
        pkt.Set("BreakerTripped", apc.TripFlag.ToString());
        pkt.Set("Locked", _lock.IsLocked(uid).ToString());
        pkt.Set("ChargePercent", pct.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        pkt.Set("LoadWatts", (int) MathF.Ceiling(battery.CurrentSupply));
        pkt.Set("ExternalPower", apc.LastExternalState.ToString());

        _deviceLink.InvokePort((uid, srcComp), PortApcStatusSource, ref pkt);
        _deviceLink.InvokePort((uid, srcComp), PortMachineStatusSender, ref pkt);
    }

    private bool HasOpenApcUi(EntityUid uid, UserInterfaceComponent ui)
    {
        if (_ui.IsUiOpen((uid, ui), ApcUiKey.Key))
            return true;

        return TryComp<EmbeddedComputerHostComponent>(uid, out var host) &&
               host.ComputerEntity is { } compUid &&
               _ui.IsUiOpen(compUid, ComputerUiKey.Key);
    }

    /// <inheritdoc />
    public override void Update(float deltaTime)
    {
        var query = EntityQueryEnumerator<ApcComponent, PowerNetworkBatteryComponent, UserInterfaceComponent>();
        var curTime = _gameTiming.CurTime;
        while (query.MoveNext(out var uid, out var apc, out var battery, out var ui))
        {
            if (apc.LastUiUpdate + ApcComponent.VisualsChangeDelay < curTime && HasOpenApcUi(uid, ui))
            {
                apc.LastUiUpdate = curTime;
                UpdateUIState(uid, apc, battery);
            }

            // Overload
            var tripped = false;
            if (apc.MainBreakerEnabled && battery.CurrentSupply > apc.MaxLoad)
            {
                // Not already overloaded, start timer
                if (apc.TripStartTime == null)
                {
                    apc.TripStartTime = curTime;
                }
                else if (curTime - apc.TripStartTime > apc.TripTime)
                {
                    apc.TripFlag = true;
                    ApcToggleBreaker(uid, apc, battery); // off, we already checked MainBreakerEnabled above
                    apc.NeedStateUpdate = true; // Force an update.
                    tripped = true;
                }
            }
            else
            {
                apc.TripStartTime = null;
            }

            // Check battery update (more frequent changes)
            if (!apc.NeedStateUpdate &&
                CalcChannelState((uid, apc), battery.NetworkBattery) != apc.LastChannelState)
            {
                apc.NeedStateUpdate = true;
            }

            if (apc.NeedStateUpdate)
            {
                UpdateApcState(uid, apc, battery, forceChargeCheck: tripped);
            }
        }
    }

    #region Event Handlers
    // Change the APC's state only when the battery state changes, or when it's first created.
    [SubscribeLocalEvent]
    private void OnBatteryChargeChanged(Entity<ApcComponent> ent, ref ChargeChangedEvent args)
    {
        // Defer until the next tick.
        ent.Comp.NeedStateUpdate = true;
    }

    [SubscribeLocalEvent]
    private void OnApcStartup(Entity<ApcComponent> ent, ref ComponentStartup args)
    {
        // We cannot update immediately, as various network/battery state is not valid yet.
        // Defer until the next tick.
        ent.Comp.NeedStateUpdate = true;
    }

    [SubscribeLocalEvent]
    private void OnBoundUiOpen(Entity<ApcComponent> ent, ref BoundUIOpenedEvent args)
    {
        UpdateApcState(ent, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnLockToggled(Entity<ApcComponent> ent, ref LockToggledEvent args)
    {
        EmitApcStatusPacket(ent.Owner, ent.Comp);
    }

    [SubscribeLocalEvent]
    private void OnToggleMainBreaker(Entity<ApcComponent> ent, ref ApcToggleMainBreakerMessage args)
    {
        var attemptEv = new ApcToggleMainBreakerAttemptEvent();
        RaiseLocalEvent(ent, ref attemptEv);

        if (attemptEv.Cancelled)
        {
            _popup.PopupCursor(Loc.GetString("apc-component-on-toggle-cancel"),
                args.Actor, PopupType.Medium);
            return;
        }

        if (HasComp<LockComponent>(ent.Owner))
        {
            if (!_lock.IsLocked(ent.Owner))
            {
                ApcToggleBreaker(ent, ent.Comp, user: args.Actor);
            }
            else
            {
                _popup.PopupCursor(Loc.GetString("apc-component-insufficient-access"),
                    args.Actor, PopupType.Medium);
            }
            return;
        }

        if (_accessReader.IsAllowed(args.Actor, ent) || _remoteScreen.IsRemoteScreenAuthorized(args.Actor, ent.Owner))
        {
            ApcToggleBreaker(ent, ent.Comp, user: args.Actor);
        }
        else
        {
            _popup.PopupCursor(Loc.GetString("apc-component-insufficient-access"),
                args.Actor, PopupType.Medium);
        }
    }

    [SubscribeLocalEvent]
    private void OnEmagged(Entity<ApcComponent> ent, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (_emag.CheckFlag(ent, EmagType.Interaction))
            return;

        args.Handled = true;
    }

    // TODO: This subscription should be in shared.
    // But I am not moving ApcComponent to shared, this PR already got soaped enough and that component uses several layers of OOP.
    // At least the EMP visuals won't mispredict, since all APCs also have the BatteryComponent, which also has a EMP effect and is in shared.
    [SubscribeLocalEvent]
    private void OnEmpPulse(Entity<ApcComponent> ent, ref EmpPulseEvent args)
    {
        if (ent.Comp.MainBreakerEnabled)
        {
            args.Affected = true;
            args.Disabled = true;
            ApcToggleBreaker(ent, ent.Comp);
        }
    }
    #endregion Event Handlers

    #region Public API
    /// <summary>
    /// Toggles the enabled state of the APC's main breaker.
    /// </summary>
    public void ApcToggleBreaker(
        EntityUid uid,
        ApcComponent? apc = null,
        PowerNetworkBatteryComponent? battery = null,
        EntityUid? user = null)
    {
        if (!Resolve(uid, ref apc, ref battery))
            return;

        apc.MainBreakerEnabled = !apc.MainBreakerEnabled;
        battery.CanDischarge = apc.MainBreakerEnabled;

        if (apc.MainBreakerEnabled)
            apc.TripFlag = false;

        UpdateApcState(uid, apc, battery);
        UpdateUIState(uid, apc);

        var audioParams = (apc.OnReceiveMessageSound?.Params ?? AudioParams.Default).AddVolume(-2f);
        _audio.PlayPvs(apc.OnReceiveMessageSound, uid, audioParams);

        if (user != null)
        {
            var humanReadableState = apc.MainBreakerEnabled ? "Enabled" : "Disabled";
            _adminLogger.Add(LogType.ItemConfigure, LogImpact.Medium,
                $"{ToPrettyString(user):user} set the main breaker state of {ToPrettyString(uid):entity} to {humanReadableState:state}.");
        }
    }

    /// <summary>
    /// Updates the UI and appearance of the given APC based on its status in the power network.
    /// </summary>
    public void UpdateApcState(EntityUid uid,
        ApcComponent? apc = null,
        PowerNetworkBatteryComponent? battery = null,
        bool forceChargeCheck = false)
    {
        if (!Resolve(uid, ref apc, ref battery, false))
            return;

        var uiDirty = false;
        if (apc.LastChargeStateTime == null || forceChargeCheck || apc.LastChargeStateTime + ApcComponent.VisualsChangeDelay < _gameTiming.CurTime)
        {
            var newState = CalcChargeState((uid, apc), battery.NetworkBattery);
            if (newState != apc.LastChargeState)
            {
                apc.LastChargeState = newState;
                apc.LastChargeStateTime = _gameTiming.CurTime;

                _appearance.SetData(uid, ApcVisuals.ChargeState, newState);
                uiDirty = true;
            }
        }

        var channelState = CalcChannelState((uid, apc), battery.NetworkBattery);
        if (channelState != apc.LastChannelState)
        {
            apc.LastChannelState = channelState;
            _appearance.SetData(uid, ApcVisuals.ChannelState, channelState);
            uiDirty = true;
        }

        var extPowerState = CalcExtPowerState(uid, battery.NetworkBattery);
        if (extPowerState != apc.LastExternalState)
        {
            apc.LastExternalState = extPowerState;
            uiDirty = true;
        }

        if (uiDirty)
            UpdateUIState(uid, apc, battery);

        apc.NeedStateUpdate = false;
    }

    /// <summary>
    /// Updates the UI of the given API.
    /// Sends off a new ApcBoundInterfaceState to anyone with the UI open.
    /// </summary>
    public void UpdateUIState(EntityUid uid,
        ApcComponent? apc = null,
        PowerNetworkBatteryComponent? netBat = null,
        UserInterfaceComponent? ui = null)
    {
        if (!Resolve(uid, ref apc, ref netBat, ref ui))
            return;

        var battery = netBat.NetworkBattery;
        const int chargeAccuracy = 5;

        // TODO: Fix ContentHelpers or make a new one coz this is cooked.
        var charge = ContentHelpers.RoundToNearestLevels(battery.CurrentStorage / battery.Capacity, 1.0, 100 / chargeAccuracy) / 100f * chargeAccuracy;

        var state = new ApcBoundInterfaceState(apc.MainBreakerEnabled,
            (int)MathF.Ceiling(battery.CurrentSupply), apc.LastExternalState,
            charge,
            apc.MaxLoad,
            apc.TripFlag);

        _ui.SetUiState((uid, ui), ApcUiKey.Key, state);
        EmitApcStatusPacket(uid, apc, netBat);
    }
    #endregion Public API

    #region Internal
    private ApcChargeState CalcChargeState(Entity<ApcComponent> ent, PowerState.Battery battery)
    {
        if (_emag.CheckFlag(ent, EmagType.Interaction))
            return ApcChargeState.Emag;

        if (ent.Comp.TripFlag)
            return ApcChargeState.Tripped;

        if (battery.CurrentStorage / battery.Capacity > ApcComponent.HighPowerThreshold)
            return ApcChargeState.Full;

        var delta = battery.CurrentSupply - battery.CurrentReceiving;
        return delta < 0 ? ApcChargeState.Charging : ApcChargeState.Lack;
    }

    private ApcExternalPowerState CalcExtPowerState(EntityUid uid, PowerState.Battery battery)
    {
        if (battery.CurrentReceiving == 0 && !MathHelper.CloseTo(battery.CurrentStorage / battery.Capacity, 1))
        {
            return ApcExternalPowerState.None;
        }

        var delta = battery.CurrentSupply - battery.CurrentReceiving;
        if (!MathHelper.CloseToPercent(delta, 0, 0.1f) && delta < 0)
        {
            return ApcExternalPowerState.Low;
        }

        return ApcExternalPowerState.Good;
    }

    /// <summary>
    /// Generates the channel state for a given APC - whether the APC is providing (or can provide) power to the network.
    /// </summary>
    private ApcChannelState CalcChannelState(Entity<ApcComponent> ent, PowerState.Battery battery)
    {
        if (ent.Comp.TripFlag)
            return ApcChannelState.BreakerTripped;
        else if (!ent.Comp.MainBreakerEnabled)
            return ApcChannelState.BreakerOpen;

        return battery.CurrentSupply > 0 ? ApcChannelState.On : ApcChannelState.Off;
    }
    #endregion Internal
}

/// <summary>
/// A cancellable event raised to check if the main breaker of an APC can be toggled.
/// </summary>
[ByRefEvent]
public record struct ApcToggleMainBreakerAttemptEvent(bool Cancelled);
