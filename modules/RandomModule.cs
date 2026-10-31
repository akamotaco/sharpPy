using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SharpPy.Modules
{
    /// <summary>
    /// _random (Modules/_randommodule.c): the Mersenne Twister behind Lib/random.py, which subclasses
    /// Random and builds everything else on random() and getrandbits(). The same seed has to give the
    /// same numbers as CPython, because a game saves a seed and expects its world back.
    /// </summary>
    public static class RandomModule
    {
        private const int N = 624;
        private const int M = 397;
        private const uint MatrixA = 0x9908b0df;
        private const uint UpperMask = 0x80000000;
        private const uint LowerMask = 0x7fffffff;

        private sealed class State
        {
            internal readonly uint[] Words = new uint[N];
            internal int Index = N + 1;
        }

        // Held per instance and dropped with it. A plain Dictionary kept every Random alive for good.
        private static readonly ConditionalWeakTable<PyObject, State> States = new();

        public static PyModule CreateRandomModule()
        {
            var module = new PyModule("_random");
            var randomClass = new PyClass("Random", new[] { PyType.ObjectType }, new Dictionary<string, PyObject>());
            randomClass.ClassDict["random"] = new PyBuiltinMethod("random", RandomMethod);
            randomClass.ClassDict["seed"] = new PyBuiltinMethod("seed", SeedMethod);
            randomClass.ClassDict["getstate"] = new PyBuiltinMethod("getstate", GetStateMethod);
            randomClass.ClassDict["setstate"] = new PyBuiltinMethod("setstate", SetStateMethod);
            randomClass.ClassDict["getrandbits"] = new PyBuiltinMethod("getrandbits", GetRandBitsMethod);
            module.ModuleDict["Random"] = randomClass;
            return module;
        }

        private static State StateOf(PyObject self)
        {
            if (States.TryGetValue(self, out var state)) return state;
            state = new State();
            States.Add(self, state);
            SeedFromKey(state, new[] { (uint)Environment.TickCount, (uint)DateTime.UtcNow.Ticks });
            return state;
        }

        // genrand_uint32
        private static uint Next(State s)
        {
            var mt = s.Words;
            if (s.Index >= N)
            {
                int kk;
                uint y;
                for (kk = 0; kk < N - M; kk++)
                {
                    y = (mt[kk] & UpperMask) | (mt[kk + 1] & LowerMask);
                    mt[kk] = mt[kk + M] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0);
                }
                for (; kk < N - 1; kk++)
                {
                    y = (mt[kk] & UpperMask) | (mt[kk + 1] & LowerMask);
                    mt[kk] = mt[kk + (M - N)] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0);
                }
                y = (mt[N - 1] & UpperMask) | (mt[0] & LowerMask);
                mt[N - 1] = mt[M - 1] ^ (y >> 1) ^ ((y & 1) != 0 ? MatrixA : 0);
                s.Index = 0;
            }
            uint r = mt[s.Index++];
            r ^= r >> 11;
            r ^= (r << 7) & 0x9d2c5680;
            r ^= (r << 15) & 0xefc60000;
            r ^= r >> 18;
            return r;
        }

        // init_genrand
        private static void SeedFromValue(State s, uint seed)
        {
            var mt = s.Words;
            mt[0] = seed;
            for (uint i = 1; i < N; i++)
                mt[i] = 1812433253u * (mt[i - 1] ^ (mt[i - 1] >> 30)) + i;
            s.Index = N;
        }

        // init_by_array
        private static void SeedFromKey(State s, uint[] key)
        {
            SeedFromValue(s, 19650218u);
            var mt = s.Words;
            uint i = 1, j = 0;
            int keyLength = key.Length;
            for (int k = Math.Max(N, keyLength); k > 0; k--)
            {
                mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1664525u)) + key[j] + j;
                i++;
                j++;
                if (i >= N) { mt[0] = mt[N - 1]; i = 1; }
                if (j >= keyLength) j = 0;
            }
            for (int k = N - 1; k > 0; k--)
            {
                mt[i] = (mt[i] ^ ((mt[i - 1] ^ (mt[i - 1] >> 30)) * 1566083941u)) - i;
                i++;
                if (i >= N) { mt[0] = mt[N - 1]; i = 1; }
            }
            mt[0] = 0x80000000u;
        }

        // random_random: 53 bits from two words.
        private static PyObject RandomMethod(PyObject self, PyObject[] args)
        {
            var s = StateOf(self);
            uint a = Next(s) >> 5, b = Next(s) >> 6;
            return new PyFloat((a * 67108864.0 + b) * (1.0 / 9007199254740992.0));
        }

        // random_seed: the absolute value of an int, little-endian 32-bit words. Anything else hashes.
        private static PyObject SeedMethod(PyObject self, PyObject[] args)
        {
            var s = StateOf(self);
            if (args.Length == 0 || args[0] is PyNone)
            {
                SeedFromKey(s, new[] { (uint)Environment.TickCount, (uint)DateTime.UtcNow.Ticks });
                return PyNone.Instance;
            }
            BigInteger n = args[0] is PyInt value ? BigInteger.Abs(value.Value) : (uint)args[0].ToHash();
            var words = new List<uint>();
            for (; n > 0; n >>= 32) words.Add((uint)(n & uint.MaxValue));
            if (words.Count == 0) words.Add(0);
            SeedFromKey(s, words.ToArray());
            return PyNone.Instance;
        }

        // random_getstate: the 624 words, then the index.
        private static PyObject GetStateMethod(PyObject self, PyObject[] args)
        {
            var s = StateOf(self);
            var items = new PyObject[N + 1];
            for (int i = 0; i < N; i++) items[i] = new PyInt(s.Words[i]);
            items[N] = new PyInt(s.Index);
            return new PyTuple(items);
        }

        private static PyObject SetStateMethod(PyObject self, PyObject[] args)
        {
            if (args.Length != 1 || !(args[0] is PyTuple state))
                throw PyTypeError.Create("state vector must be a tuple");
            if (state.Items.Length != N + 1)
                throw PyValueError.Create("state vector is the wrong size");
            var s = StateOf(self);
            for (int i = 0; i < N; i++)
                s.Words[i] = (uint)((PyInt)state.Items[i]).Value;
            var index = ((PyInt)state.Items[N]).Value;
            if (index < 0 || index > N)
                throw PyValueError.Create("invalid state");
            s.Index = (int)index;
            return PyNone.Instance;
        }

        // _random_Random_getrandbits_impl: one word for k <= 32, otherwise little-endian words.
        private static PyObject GetRandBitsMethod(PyObject self, PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("getrandbits() missing required argument: 'k' (pos 1)");
            var requested = ((PyInt)args[0]).Value;
            if (requested < 0)
                throw PyValueError.Create("number of bits must be >= 0");
            if (requested == 0) return new PyInt(0);
            int k = (int)requested;
            var s = StateOf(self);
            if (k <= 32) return new PyInt(Next(s) >> (32 - k));
            BigInteger result = 0;
            int shift = 0;
            for (int left = k; left > 0; left -= 32, shift += 32)
            {
                uint word = Next(s);
                if (left < 32) word >>= 32 - left;
                result |= (BigInteger)word << shift;
            }
            return new PyInt(result);
        }
    }
}
