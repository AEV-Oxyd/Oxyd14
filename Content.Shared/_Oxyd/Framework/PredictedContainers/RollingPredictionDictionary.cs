using System.Collections;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Robust.Shared.Timing;

namespace Content.Shared;

public sealed class RollingPredictionDictionary<T>
{
    [ViewVariables]
    SortedDictionary<int, T> dict = new();
    [ViewVariables]
    public int limit = 25;
    
    public void Insert(int tick, T value)
    {
        dict.Add(tick, value);
        while (dict.Count > limit)
        {
            dict.Remove(dict.Keys.First());
        }
    }

    public bool Get(int tick, [NotNullWhen(true)] out T? value)
    {
        value = default(T);
        if(dict.TryGetValue(tick, out var val))
        {
            value = val!;
            return true;
        }
        return false;
    }
}