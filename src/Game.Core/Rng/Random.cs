using System.Security.Cryptography;
using System.Text;

namespace Game.Core.Rng;

public interface IRandomSource
{
    /// <summary>Uniform integer in [0, exclusiveMax).</summary>
    int Next(int exclusiveMax);
}

/// <summary>Serializable PCG32 state. Lives inside the authoritative game state only.</summary>
public sealed class RngState
{
    public ulong S { get; set; }
    public ulong Inc { get; set; }

    public RngState Clone() => new() { S = S, Inc = Inc };
}

/// <summary>PCG32 (XSH-RR). Identical output on every platform for a given seed.</summary>
public sealed class Pcg32 : IRandomSource
{
    private readonly RngState _state;

    public Pcg32(RngState state) => _state = state;

    public Pcg32(ulong seed, ulong stream = 54u) : this(Seed(seed, stream)) { }

    public static RngState Seed(ulong seed, ulong stream = 54u)
    {
        var st = new RngState { S = 0, Inc = (stream << 1) | 1u };
        var rng = new Pcg32(st);
        rng.NextUInt();
        st.S += seed;
        rng.NextUInt();
        return st;
    }

    public uint NextUInt()
    {
        ulong old = _state.S;
        _state.S = unchecked(old * 6364136223846793005UL + _state.Inc);
        uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
        int rot = (int)(old >> 59);
        return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
    }

    public int Next(int exclusiveMax)
    {
        if (exclusiveMax <= 1) return 0;
        uint bound = (uint)exclusiveMax;
        uint threshold = (uint)(-bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold) return (int)(r % bound);
        }
    }
}

/// <summary>Fixed sequence, for tests that need exact dice.</summary>
public sealed class ScriptedRandom : IRandomSource
{
    private readonly Queue<int> _values;
    private readonly IRandomSource? _fallback;

    public ScriptedRandom(IEnumerable<int> values, IRandomSource? fallback = null)
    {
        _values = new Queue<int>(values);
        _fallback = fallback;
    }

    public void Enqueue(params int[] values)
    {
        foreach (int v in values) _values.Enqueue(v);
    }

    /// <summary>Queue a dice roll (two values in 1..6).</summary>
    public void EnqueueDice(int d1, int d2) => Enqueue(d1 - 1, d2 - 1);

    public int Next(int exclusiveMax)
    {
        if (_values.Count > 0) return Math.Min(_values.Dequeue(), exclusiveMax - 1);
        return _fallback?.Next(exclusiveMax) ?? 0;
    }
}

/// <summary>
/// Combines one seed contribution per participant into a single match seed so that no single
/// device chooses the dice stream on its own.
/// </summary>
public static class FairSeed
{
    public static string Commit(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)));

    public static bool Verify(string seed, string commitment) =>
        string.Equals(Commit(seed), commitment, StringComparison.OrdinalIgnoreCase);

    public static ulong Combine(IEnumerable<string> seeds, string matchId)
    {
        var sb = new StringBuilder();
        foreach (string s in seeds) sb.Append(s).Append('|');
        sb.Append(matchId);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }
}
