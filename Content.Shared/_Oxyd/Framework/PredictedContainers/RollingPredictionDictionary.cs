using System.Collections;
using System.Collections.Frozen;
using System.Linq;
using Robust.Shared.Timing;

namespace Content.Shared;

public sealed class RollingPredictionDictionary<T>
{
    // MAXIMUM NUMBER OF TICKS BEHIND THAT CAN BE PREDICTED.
    // If you mess this up you'll lose prediction data and have
    // mispredicted ticks or errors
    public const int maxOffset = 25;
    public int indexTick = 0;

    public FrozenDictionary<int, Queue<T>> data;
    
    public RollingPredictionDictionary()
    {
        Dictionary<int, Queue<T>> creat = new();
        for (var i = 0; i < maxOffset; i++)
        {
            creat[i] = new Queue<T>();
        }
        data = creat.ToFrozenDictionary();
    }

    public void Insert(GameTick tick, T value)
    {
        var tv = (int)tick.Value;
        var calcDiff = tv - indexTick;
        if (calcDiff > maxOffset)
        {
            
        }
        data[tv % maxOffset].Enqueue(value);
    }
}