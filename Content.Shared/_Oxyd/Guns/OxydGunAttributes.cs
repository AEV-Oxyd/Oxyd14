namespace Content.Shared._Oxyd.OxydGunSystem;

// Ensures that GetInvolvedComponents checks this variable/list for deeper ents in getRelated
[AttributeUsage(AttributeTargets.Field)]
public class CheckForGunUpdateAttribute(bool indexing = false) : Attribute
{
    public bool indexed = indexing;
}
