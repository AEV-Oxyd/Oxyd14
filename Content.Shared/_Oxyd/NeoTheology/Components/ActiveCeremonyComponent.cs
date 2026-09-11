using Content.Shared._Oxyd.NeoTheology.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._Oxyd.NeoTheology.Components;

/// <summary>
/// The live group-ritual session (Eris <c>datum/core_module/group_ritual</c>). The component
/// lives on the ritual starter. The phrase list advances like the Eris module: the current
/// participant phrase is <see cref="Phrases"/>[0], the starter's advance phrase is
/// <see cref="Phrases"/>[1]. A successful starter phrase pops the first entry and swaps in the
/// round's correct participants.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveCeremonyComponent : Component
{
    /// <summary>The litany that started the rite.</summary>
    public ProtoId<LitanyPrototype> Ritual;

    /// <summary>The starter's cruciform, recorded at start so rank and set checks stay cheap.</summary>
    public EntityUid Cruciform;

    /// <summary>Remaining phrases. Eris <c>phrases</c> after every <c>next_phrase</c>.</summary>
    public List<string> Phrases = new();

    /// <summary>True until the first starter advance; anyone with a cruciform may join the first round.</summary>
    public bool First = true;

    /// <summary>Followers that continue the rite (Eris <c>participants</c>).</summary>
    public HashSet<EntityUid> Participants = new();

    /// <summary>Followers that spoke the current phrase correctly (Eris <c>correct_participants</c>).</summary>
    public HashSet<EntityUid> CorrectParticipants = new();

    /// <summary>The litany range the follower must stay inside. Named divergence: Eris hears every phrase.</summary>
    public float Range;

    /// <summary>
    /// Named divergence: Eris keeps the module until a wrong phrase, so a stalled rite lives
    /// forever. The fork ends it after five minutes.
    /// </summary>
    public TimeSpan ExpiresAt;
}
