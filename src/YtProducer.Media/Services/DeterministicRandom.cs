namespace YtProducer.Media.Services;

public sealed class DeterministicRandom
{
    private uint _state;

    public DeterministicRandom(int seed)
    {
        _state = unchecked((uint)seed);
        if (_state == 0)
        {
            _state = 0x9E3779B9;
        }
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");
        }

        var range = (uint)(maxExclusive - minInclusive);
        var value = NextUInt();
        return minInclusive + (int)(value % range);
    }

    public float NextFloat()
    {
        return (NextUInt() & 0x00FFFFFF) / 16777216f;
    }

    public float NextFloat(float minInclusive, float maxInclusive)
    {
        if (minInclusive > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxInclusive), "maxInclusive must be >= minInclusive.");
        }

        return minInclusive + (maxInclusive - minInclusive) * NextFloat();
    }

    private uint NextUInt()
    {
        _state = unchecked(_state * 1664525u + 1013904223u);
        return _state;
    }
}
