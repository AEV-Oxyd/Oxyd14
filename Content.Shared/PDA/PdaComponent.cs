using Content.Shared.AlertLevel;
using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.PDA
{
    [RegisterComponent, NetworkedComponent]
    public sealed partial class PdaComponent : Component
    {
        public const string PdaIdSlotId = "PDA-id";
        public const string PdaPenSlotId = "PDA-pen";
        public const string PdaPaiSlotId = "PDA-pai";
        public const string PdaDiskSlotId = "PDA-disk";

        [DataField]
        public ItemSlot IdSlot = new();

        [DataField]
        public ItemSlot PenSlot = new();

        [DataField]
        public ItemSlot PaiSlot = new();

        [DataField]
        public ItemSlot DiskSlot = new();

        [DataField("id")]
        public EntProtoId? IdCard;

        [ViewVariables]
        public EntityUid? ContainedId;

        [ViewVariables]
        public EntityUid? ContainedDisk;

        [ViewVariables]
        public bool FlashlightOn;

        [ViewVariables(VVAccess.ReadWrite)]
        public string? OwnerName;

        [ViewVariables(VVAccess.ReadWrite)]
        public EntityUid? PdaOwner;

        [ViewVariables]
        public string? StationName;

        [ViewVariables]
        public ProtoId<AlertLevelPrototype>? StationAlertLevel;

        [ViewVariables]
        public Color StationAlertColor = Color.White;
    }
}
