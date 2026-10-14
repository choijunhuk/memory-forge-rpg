// Memory Forge v0.7 core — deterministic RNG shared by battle, run and gacha.
// Bit-for-bit identical to engine/rules-core.js (Rng). Verified by MemoryForge.CoreChecks against
// engine/tests/vectors/rng_gacha_vectors.json. C# 8 / netstandard2.1 (Unity-compatible).
using System;

namespace MemoryForge.Core
{
    public sealed class RuleException : Exception
    {
        public string Code { get; }
        public RuleException(string code) : base(code) { Code = code; }
    }

    /// <summary>xoshiro128** with a splitmix32 seeder. The seed is chosen by the server, never the client.</summary>
    public sealed class Rng
    {
        private uint s0, s1, s2, s3;

        public Rng(uint seed)
        {
            var s = SeedState(seed);
            s0 = s[0]; s1 = s[1]; s2 = s[2]; s3 = s[3];
        }

        public Rng(uint[] state)
        {
            if (state == null || state.Length != 4) throw new RuleException("RNG_STATE");
            s0 = state[0]; s1 = state[1]; s2 = state[2]; s3 = state[3];
        }

        public uint[] State => new[] { s0, s1, s2, s3 };

        public static uint[] SeedState(uint seed)
        {
            uint a = seed;
            uint Next()
            {
                unchecked
                {
                    a += 0x9e3779b9u;
                    uint t = a ^ (a >> 16);
                    t *= 0x21f0aaadu;
                    t ^= t >> 15;
                    t *= 0x735a2d97u;
                    return t ^ (t >> 15);
                }
            }
            var s = new[] { Next(), Next(), Next(), Next() };
            if (s[0] == 0 && s[1] == 0 && s[2] == 0 && s[3] == 0) s[0] = 1;
            return s;
        }

        private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint Next()
        {
            unchecked
            {
                uint result = Rotl(s1 * 5u, 7) * 9u;
                uint t = s1 << 9;
                s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3;
                s2 ^= t; s3 = Rotl(s3, 11);
                return result;
            }
        }

        /// <summary>Uniform integer in [0, n) by rejection sampling (no modulo bias).</summary>
        public int Below(long n)
        {
            if (n <= 0 || n > 0x100000000L) throw new RuleException("RNG_BOUND");
            ulong limit = 0x100000000UL - (0x100000000UL % (ulong)n);
            for (; ; )
            {
                uint x = Next();
                if (x < limit) return (int)(x % (ulong)n);
            }
        }

        public bool ChanceBps(int bps) => Below(10000) < bps;

        public T Pick<T>(System.Collections.Generic.IReadOnlyList<T> list)
        {
            if (list == null || list.Count == 0) throw new RuleException("EMPTY_POOL");
            return list[Below(list.Count)];
        }
    }
}
