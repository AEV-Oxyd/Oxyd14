using System.Collections.Generic;
using System.Linq;
using Content.Shared._Oxyd.NeoTheology;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.Server._Oxyd.NeoTheology;

/// <summary>
/// Validates the loaded NeoTheology catalog before entity systems can use it and
/// maintains the server-side phrase/prototype index used by future cast systems.
/// </summary>
public sealed partial class LitanyPrototypeValidationSystem : EntitySystem
{
    private readonly Dictionary<string, LitanyPrototype> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LitanyPrototype> _byPhrase = new(StringComparer.Ordinal);

    public bool CatalogReady { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        ValidateAndIndex(initialLoad: true);
    }

    /// <summary>
    /// Returns a validated catalog entry, including dependency-gated reference rows.
    /// Callers must still check <see cref="LitanyPrototype.IsAvailable"/> and handler
    /// readiness before starting a cast.
    /// </summary>
    public bool TryGetLitany(ProtoId<LitanyPrototype> id, out LitanyPrototype litany)
    {
        litany = null!;
        if (!CatalogReady || !_byId.TryGetValue(id.Id, out var found))
            return false;

        litany = found;
        return true;
    }

    /// <summary>
    /// Matches an invariant accepted speech string against the validated phrase map.
    /// Targeted phrase extraction remains the responsibility of LitanyPhraseParser.
    /// </summary>
    public bool TryMatchPhrase(string phrase, out LitanyPrototype litany)
    {
        litany = null!;
        if (!CatalogReady || !_byPhrase.TryGetValue(LitanyPhraseParser.Normalize(phrase), out var found))
            return false;

        litany = found;
        return true;
    }

    /// <summary>
    /// Returns true only for an enabled catalog entry with an actual server handler.
    /// Pending foundation metadata is deliberately not treated as a handler.
    /// </summary>
    public bool IsCastable(LitanyPrototype litany)
    {
        return CatalogReady &&
               litany.IsAvailable &&
               LitanyHandlerCatalog.Implemented.Contains(litany.Effect);
    }

    public IEnumerable<LitanyPrototype> EnumerateCatalog()
    {
        return _byId.Values;
    }

    public IEnumerable<LitanyPrototype> EnumerateCastable()
    {
        return _byId.Values.Where(IsCastable);
    }

    [SubscribeLocalEvent]
    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        // Reloads happen after initial startup. Fail closed rather than allowing a
        // partially rebuilt catalog to authorize a cast; the next valid reload can
        // restore readiness without retaining stale indexes.
        try
        {
            ValidateAndIndex(initialLoad: false);
        }
        catch (InvalidOperationException exception)
        {
            CatalogReady = false;
            _byId.Clear();
            _byPhrase.Clear();
            Log.Error($"NeoTheology prototype reload left the litany catalog unavailable: {exception}");
        }
    }

    private void ValidateAndIndex(bool initialLoad)
    {
        var errors = LitanyCatalogValidator.Validate(ProtoMan, Loc);

        if (errors.Count != 0)
        {
            CatalogReady = false;
            _byId.Clear();
            _byPhrase.Clear();

            var details = string.Join(Environment.NewLine, errors.Select(error => $" - {error}"));
            var message = $"NeoTheology litany catalog validation failed:{Environment.NewLine}{details}";
            if (initialLoad)
                throw new InvalidOperationException(message);

            throw new InvalidOperationException(message);
        }

        _byId.Clear();
        _byPhrase.Clear();
        foreach (var litany in ProtoMan.EnumeratePrototypes<LitanyPrototype>())
        {
            _byId.Add(litany.ID, litany);
            _byPhrase.Add(LitanyPhraseParser.Normalize(litany.Phrase), litany);
        }

        CatalogReady = true;

    }
}
