namespace MatchShift.SyntheticData;

/// <summary>
/// xoshiro256** PRNG seeded through SplitMix64.
///
/// We deliberately do not use <see cref="System.Random"/>: its seeded algorithm
/// is an implementation detail of the runtime. MatchShift promises that the same
/// seed produces the same match on every machine and every .NET version, so the
/// algorithm is owned here.
/// </summary>
public sealed class DeterministicRandom
{
    private ulong _s0, _s1, _s2, _s3;

    public DeterministicRandom(long seed)
    {
        var sm = unchecked((ulong)seed);
        _s0 = SplitMix64(ref sm);
        _s1 = SplitMix64(ref sm);
        _s2 = SplitMix64(ref sm);
        _s3 = SplitMix64(ref sm);
    }

    private static ulong SplitMix64(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        var result = Rotl(_s1 * 5, 7) * 9;
        var t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
    public int Next(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive) return minInclusive;
        return minInclusive + (int)(NextDouble() * (maxExclusive - minInclusive));
    }

    public double Range(double min, double max) => min + NextDouble() * (max - min);

    public bool Chance(double probability) => NextDouble() < probability;

    /// <summary>Standard normal via Box-Muller.</summary>
    public double NextGaussian(double mean = 0, double stdDev = 1)
    {
        var u1 = 1.0 - NextDouble();
        var u2 = NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(0, items.Count)];

    /// <summary>Weighted pick; weights need not sum to one.</summary>
    public int PickWeighted(IReadOnlyList<double> weights)
    {
        var total = 0.0;
        foreach (var w in weights) total += Math.Max(0, w);
        if (total <= 0) return Next(0, weights.Count);
        var roll = NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0, weights[i]);
            if (roll < 0) return i;
        }
        return weights.Count - 1;
    }

    /// <summary>Derive an independent child generator (e.g. one per match in a dataset).</summary>
    public DeterministicRandom Fork() => new(unchecked((long)NextULong()));
}
