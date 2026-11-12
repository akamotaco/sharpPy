using System;
using static System.Numerics.BitOperations;

namespace SharpPy.Modules
{
    /// <summary>
    /// CPython's built-in hash modules, which hashlib.py uses when there is no _hashlib (OpenSSL):
    /// _md5 (Modules/md5module.c), _sha1 (sha1module.c), _sha2 (sha2module.c), _sha3
    /// (sha3module.c over Modules/_hacl/Hacl_Hash_SHA3.c) and _blake2 (Modules/_blake2 over
    /// blake2b-ref.c and blake2s-ref.c). The algorithms are written here in plain C# - no
    /// platform cryptography - so they run wherever SharpPy does.
    /// </summary>
    internal static class HashModules
    {
        // ---- the shared surface of every hash object ----

        /// <summary>A hash object: the state of one algorithm, fed and read by the methods below.</summary>
        internal abstract class HashObject : PyObject
        {
            private readonly PyType _type;
            protected HashObject(PyType type) => _type = type;
            public override PyType GetPyType() => _type;
            internal abstract string Name { get; }
            internal abstract int DigestSize { get; }
            internal abstract int BlockSize { get; }
            internal abstract void Update(byte[] data);
            internal abstract byte[] Digest();
            internal abstract HashObject Copy();
        }

        // GET_BUFFER_VIEW_OR_ERROUT (Modules/hashlib.h): a str is refused, then anything without a buffer.
        private static byte[] DataOf(PyObject value)
        {
            if (value is PyString)
                throw PyTypeError.Create("Strings must be encoded before hashing");
            return BytesMethods.Buffer(value) ?? throw PyTypeError.Create("object supporting the buffer API required");
        }

        private static string Hex(byte[] data)
        {
            var text = new System.Text.StringBuilder(data.Length * 2);
            foreach (var b in data) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        // update (METH_O), digest, hexdigest and copy (METH_NOARGS), and the name, digest_size and
        // block_size getsets every one of these types has.
        private static void InstallCommon(PyType type, bool shake = false)
        {
            PyArgs.Define(type, "update", PyCall.One, (self, args) =>
            {
                ((HashObject)self).Update(DataOf(args[0]));
                return PyNone.Instance;
            });
            if (!shake)
            {
                PyArgs.Define(type, "digest", PyCall.NoArgs, (self, args) => PyBytes.From(((HashObject)self).Digest()));
                PyArgs.Define(type, "hexdigest", PyCall.NoArgs, (self, args) => PyString.From(Hex(((HashObject)self).Digest())));
            }
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => ((HashObject)self).Copy());
            type.TypeDict["name"] = new PyGetSetDescriptor("name", type, self => PyString.From(((HashObject)self).Name));
            type.TypeDict["digest_size"] = new PyGetSetDescriptor("digest_size", type, self => PyInt.From(((HashObject)self).DigestSize));
            type.TypeDict["block_size"] = new PyGetSetDescriptor("block_size", type, self => PyInt.From(((HashObject)self).BlockSize));
        }

        // The constructor of _md5, _sha1 and _sha2: (string=b'', *, usedforsecurity=True).
        private static PyBuiltinFunction Constructor(string name, Func<HashObject> make) =>
            new(name, (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack(name, args, kwargs, new[] { "string", "usedforsecurity" }, 0, 0, 1);
                parsed[1]?.IsTrue();
                var hash = make();
                if (parsed[0] != null)
                    hash.Update(DataOf(parsed[0]));
                return hash;
            });

        // ---- MD5, SHA-1, SHA-2: Merkle-Damgard over a block with a length suffix ----

        private enum MdFamily { MD5, SHA1, SHA256, SHA512 }

        private sealed class MdHash : HashObject
        {
            private readonly MdFamily _family;
            private readonly string _name;
            private readonly int _digestSize, _blockSize;
            private readonly byte[] _state;
            private readonly byte[] _buffer;
            private int _filled;
            private ulong _length;

            internal MdHash(PyType type, MdFamily family, string name, int digestSize, byte[] iv) : base(type)
            {
                _family = family;
                _name = name;
                _digestSize = digestSize;
                _blockSize = family == MdFamily.SHA512 ? 128 : 64;
                _state = (byte[])iv.Clone();
                _buffer = new byte[_blockSize];
            }

            private MdHash(MdHash other) : base(other.GetPyType())
            {
                _family = other._family;
                _name = other._name;
                _digestSize = other._digestSize;
                _blockSize = other._blockSize;
                _state = (byte[])other._state.Clone();
                _buffer = (byte[])other._buffer.Clone();
                _filled = other._filled;
                _length = other._length;
            }

            internal override string Name => _name;
            internal override int DigestSize => _digestSize;
            internal override int BlockSize => _blockSize;
            internal override HashObject Copy() => new MdHash(this);

            private void Compress(byte[] state, byte[] block)
            {
                switch (_family)
                {
                    case MdFamily.MD5: Md.MD5Compress(state, block); break;
                    case MdFamily.SHA1: Md.SHA1Compress(state, block); break;
                    case MdFamily.SHA256: Md.SHA256Compress(state, block); break;
                    default: Md.SHA512Compress(state, block); break;
                }
            }

            internal override void Update(byte[] data)
            {
                _length += (ulong)data.Length;
                int offset = 0;
                while (offset < data.Length)
                {
                    int take = Math.Min(_blockSize - _filled, data.Length - offset);
                    Array.Copy(data, offset, _buffer, _filled, take);
                    _filled += take;
                    offset += take;
                    if (_filled == _blockSize)
                    {
                        Compress(_state, _buffer);
                        _filled = 0;
                    }
                }
            }

            internal override byte[] Digest()
            {
                var state = (byte[])_state.Clone();
                var block = (byte[])_buffer.Clone();
                int filled = _filled;
                int lengthField = _blockSize == 128 ? 16 : 8;
                ulong bits = _length * 8;
                block[filled++] = 0x80;
                if (filled > _blockSize - lengthField)
                {
                    Array.Clear(block, filled, _blockSize - filled);
                    Compress(state, block);
                    filled = 0;
                }
                Array.Clear(block, filled, _blockSize - filled);
                bool littleEndian = _family == MdFamily.MD5;
                for (int i = 0; i < 8; i++)
                {
                    byte value = (byte)(bits >> (8 * i));
                    if (littleEndian) block[_blockSize - lengthField + i] = value;
                    else block[_blockSize - 1 - i] = value;
                }
                Compress(state, block);
                var digest = new byte[_digestSize];
                Array.Copy(state, digest, _digestSize);
                return digest;
            }
        }

        private static readonly PyType MD5Type = new("md5", new[] { PyType.ObjectType }, "_md5");
        private static readonly PyType SHA1Type = new("sha1", new[] { PyType.ObjectType }, "_sha1");
        private static readonly PyType SHA224Type = new("SHA224Type", new[] { PyType.ObjectType }, "_sha2");
        private static readonly PyType SHA256Type = new("SHA256Type", new[] { PyType.ObjectType }, "_sha2");
        private static readonly PyType SHA384Type = new("SHA384Type", new[] { PyType.ObjectType }, "_sha2");
        private static readonly PyType SHA512Type = new("SHA512Type", new[] { PyType.ObjectType }, "_sha2");

        internal static PyModule CreateMd5()
        {
            InstallCommon(MD5Type);
            var module = new PyModule("_md5");
            module.ModuleDict["MD5Type"] = MD5Type;
            module.ModuleDict["md5"] = Constructor("md5", () => new MdHash(MD5Type, MdFamily.MD5, "md5", 16, Md.MD5_IV));
            return module;
        }

        internal static PyModule CreateSha1()
        {
            InstallCommon(SHA1Type);
            var module = new PyModule("_sha1");
            module.ModuleDict["SHA1Type"] = SHA1Type;
            module.ModuleDict["sha1"] = Constructor("sha1", () => new MdHash(SHA1Type, MdFamily.SHA1, "sha1", 20, Md.SHA1_IV));
            return module;
        }

        internal static PyModule CreateSha2()
        {
            foreach (var type in new[] { SHA224Type, SHA256Type, SHA384Type, SHA512Type })
                InstallCommon(type);
            var module = new PyModule("_sha2");
            module.ModuleDict["SHA224Type"] = SHA224Type;
            module.ModuleDict["SHA256Type"] = SHA256Type;
            module.ModuleDict["SHA384Type"] = SHA384Type;
            module.ModuleDict["SHA512Type"] = SHA512Type;
            module.ModuleDict["sha224"] = Constructor("sha224", () => new MdHash(SHA224Type, MdFamily.SHA256, "sha224", 28, Md.SHA224_IV));
            module.ModuleDict["sha256"] = Constructor("sha256", () => new MdHash(SHA256Type, MdFamily.SHA256, "sha256", 32, Md.SHA256_IV));
            module.ModuleDict["sha384"] = Constructor("sha384", () => new MdHash(SHA384Type, MdFamily.SHA512, "sha384", 48, Md.SHA384_IV));
            module.ModuleDict["sha512"] = Constructor("sha512", () => new MdHash(SHA512Type, MdFamily.SHA512, "sha512", 64, Md.SHA512_IV));
            return module;
        }

        // ---- SHA-3 and SHAKE: Keccak-f[1600] (Hacl_Hash_SHA3.c) ----

        private sealed class Sha3Hash : HashObject
        {
            private readonly string _name;
            private readonly int _rate, _digestSize;
            private readonly byte _suffix;
            private readonly ulong[] _lanes = new ulong[25];
            private readonly byte[] _buffer;
            private int _filled;

            internal Sha3Hash(PyType type, string name, int rate, int digestSize, byte suffix) : base(type)
            {
                _name = name;
                _rate = rate;
                _digestSize = digestSize;
                _suffix = suffix;
                _buffer = new byte[rate];
            }

            private Sha3Hash(Sha3Hash other) : base(other.GetPyType())
            {
                _name = other._name;
                _rate = other._rate;
                _digestSize = other._digestSize;
                _suffix = other._suffix;
                Array.Copy(other._lanes, _lanes, 25);
                _buffer = (byte[])other._buffer.Clone();
                _filled = other._filled;
            }

            internal override string Name => _name;
            internal override int DigestSize => _digestSize;
            internal override int BlockSize => _rate;
            internal int Rate => _rate;
            internal byte Suffix => _suffix;
            internal override HashObject Copy() => new Sha3Hash(this);

            private static void Absorb(ulong[] lanes, byte[] block, int rate)
            {
                for (int i = 0; i < rate / 8; i++)
                {
                    ulong lane = 0;
                    for (int b = 0; b < 8; b++)
                        lane |= (ulong)block[8 * i + b] << (8 * b);
                    lanes[i] ^= lane;
                }
                Keccak.Permute(lanes);
            }

            internal override void Update(byte[] data)
            {
                int offset = 0;
                while (offset < data.Length)
                {
                    int take = Math.Min(_rate - _filled, data.Length - offset);
                    Array.Copy(data, offset, _buffer, _filled, take);
                    _filled += take;
                    offset += take;
                    if (_filled == _rate)
                    {
                        Absorb(_lanes, _buffer, _rate);
                        _filled = 0;
                    }
                }
            }

            internal override byte[] Digest() => Squeeze(_digestSize);

            // The padding of FIPS 202 (the domain suffix, then the final 0x80), then the output.
            internal byte[] Squeeze(int length)
            {
                var lanes = (ulong[])_lanes.Clone();
                var block = new byte[_rate];
                Array.Copy(_buffer, block, _filled);
                block[_filled] ^= _suffix;
                block[_rate - 1] ^= 0x80;
                Absorb(lanes, block, _rate);
                var output = new byte[length];
                int produced = 0;
                while (true)
                {
                    for (int i = 0; i < _rate && produced < length; i++, produced++)
                        output[produced] = (byte)(lanes[i / 8] >> (8 * (i % 8)));
                    if (produced >= length)
                        return output;
                    Keccak.Permute(lanes);
                }
            }
        }

        private static class Keccak
        {
            private static readonly ulong[] RoundConstants =
            {
                0x0000000000000001UL, 0x0000000000008082UL, 0x800000000000808aUL, 0x8000000080008000UL,
                0x000000000000808bUL, 0x0000000080000001UL, 0x8000000080008081UL, 0x8000000000008009UL,
                0x000000000000008aUL, 0x0000000000000088UL, 0x0000000080008009UL, 0x000000008000000aUL,
                0x000000008000808bUL, 0x800000000000008bUL, 0x8000000000008089UL, 0x8000000000008003UL,
                0x8000000000008002UL, 0x8000000000000080UL, 0x000000000000800aUL, 0x800000008000000aUL,
                0x8000000080008081UL, 0x8000000000008080UL, 0x0000000080000001UL, 0x8000000080008008UL,
            };

            // The rho offsets and pi positions, in the order of the lane walk (1, 10, 7, 11, ...).
            private static readonly int[] Rotations = { 1, 3, 6, 10, 15, 21, 28, 36, 45, 55, 2, 14, 27, 41, 56, 8, 25, 43, 62, 18, 39, 61, 20, 44 };
            private static readonly int[] PiLanes = { 10, 7, 11, 17, 18, 3, 5, 16, 8, 21, 24, 4, 15, 23, 19, 13, 12, 2, 20, 14, 22, 9, 6, 1 };

            internal static void Permute(ulong[] a)
            {
                var c = new ulong[5];
                for (int round = 0; round < 24; round++)
                {
                    // theta
                    for (int x = 0; x < 5; x++)
                        c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
                    for (int x = 0; x < 5; x++)
                    {
                        ulong d = c[(x + 4) % 5] ^ RotateLeft(c[(x + 1) % 5], 1);
                        for (int y = 0; y < 25; y += 5)
                            a[y + x] ^= d;
                    }
                    // rho and pi
                    ulong current = a[1];
                    for (int i = 0; i < 24; i++)
                    {
                        int j = PiLanes[i];
                        ulong next = a[j];
                        a[j] = RotateLeft(current, Rotations[i]);
                        current = next;
                    }
                    // chi
                    for (int y = 0; y < 25; y += 5)
                    {
                        ulong a0 = a[y], a1 = a[y + 1], a2 = a[y + 2], a3 = a[y + 3], a4 = a[y + 4];
                        a[y] = a0 ^ (~a1 & a2);
                        a[y + 1] = a1 ^ (~a2 & a3);
                        a[y + 2] = a2 ^ (~a3 & a4);
                        a[y + 3] = a3 ^ (~a4 & a0);
                        a[y + 4] = a4 ^ (~a0 & a1);
                    }
                    // iota
                    a[0] ^= RoundConstants[round];
                }
            }
        }

        internal static PyModule CreateSha3()
        {
            var module = new PyModule("_sha3");
            foreach (var (name, rate, digest, shake) in new[]
            {
                ("sha3_224", 144, 28, false), ("sha3_256", 136, 32, false), ("sha3_384", 104, 48, false),
                ("sha3_512", 72, 64, false), ("shake_128", 168, 0, true), ("shake_256", 136, 0, true),
            })
            {
                var type = new PyType(name, new[] { PyType.ObjectType }, "_sha3");
                byte suffix = shake ? (byte)0x1f : (byte)0x06;
                string label = name;
                int r = rate, d = digest;
                // py_sha3_new: one clinic function, named sha3_224, for all six (data=b'', /, *, usedforsecurity=True).
                type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
                {
                    var parsed = PyArgs.Unpack("sha3_224", args, kwargs, new[] { "data", "usedforsecurity" }, 1, 0, 1);
                    parsed[1]?.IsTrue();
                    var hash = new Sha3Hash((PyType)cls, label, r, d, suffix);
                    if (parsed[0] != null)
                        hash.Update(DataOf(parsed[0]));
                    return hash;
                }, acceptsKwargs: true);
                InstallCommon(type, shake);
                type.TypeDict["_capacity_bits"] = new PyGetSetDescriptor("_capacity_bits", type, self => PyInt.From(1600 - 8 * ((Sha3Hash)self).Rate));
                type.TypeDict["_rate_bits"] = new PyGetSetDescriptor("_rate_bits", type, self => PyInt.From(8 * ((Sha3Hash)self).Rate));
                type.TypeDict["_suffix"] = new PyGetSetDescriptor("_suffix", type, self => PyBytes.From(new[] { ((Sha3Hash)self).Suffix }));
                if (shake)
                {
                    // _sha3_shake_128_digest_impl: length is an unsigned long below 2**29.
                    PyArgs.Define(type, "digest", PyCall.One, (self, args) => PyBytes.From(((Sha3Hash)self).Squeeze(ShakeLength(args[0]))));
                    PyArgs.Define(type, "hexdigest", PyCall.One, (self, args) => PyString.From(Hex(((Sha3Hash)self).Squeeze(ShakeLength(args[0])))));
                }
                module.ModuleDict[name] = type;
            }
            // The implementation the module was built with; this is a port of HACL*'s.
            module.ModuleDict["implementation"] = PyString.From("HACL");
            return module;
        }

        private static int ShakeLength(PyObject value)
        {
            ulong length = UnsignedLong(value);
            if (length >= 1UL << 29)
                throw PyValueError.Create("length is too large");
            return (int)length;
        }

        // _PyLong_UnsignedLong_Converter: an int that is not negative and fits a C unsigned long (32 bits on Windows).
        private static ulong UnsignedLong(PyObject value)
        {
            if (value is PyFloat)
                throw PyTypeError.Create("an integer is required");
            var number = PyObject.IndexOrThrow(value).Value;
            if (number.Sign < 0)
                throw PyValueError.Create("value must be positive");
            if (number > uint.MaxValue)
                throw PyOverflowError.Create("Python int too large to convert to C unsigned long");
            return (ulong)number;
        }

        // _PyLong_UnsignedLongLong_Converter
        private static ulong UnsignedLongLong(PyObject value)
        {
            var number = PyObject.IndexOrThrow(value).Value;
            if (number.Sign < 0)
                throw PyValueError.Create("value must be positive");
            if (number > ulong.MaxValue)
                throw PyOverflowError.Create("int too big to convert");
            return (ulong)number;
        }

        // ---- BLAKE2b and BLAKE2s (blake2b-ref.c, blake2s-ref.c) ----

        private sealed class Blake2Hash : HashObject
        {
            private readonly bool _wide;            // BLAKE2b: 64-bit words, 128-byte blocks
            private readonly int _digestSize;
            private readonly ulong[] _h;            // BLAKE2s keeps its 32-bit words in the low halves
            private readonly byte[] _buffer;
            private int _filled;
            private ulong _t0, _t1;
            private readonly bool _lastNode;

            internal Blake2Hash(bool wide, int digestSize, ulong[] h, bool lastNode) : base(wide ? Blake2bType : Blake2sType)
            {
                _wide = wide;
                _digestSize = digestSize;
                _h = h;
                _buffer = new byte[wide ? 128 : 64];
                _lastNode = lastNode;
            }

            private Blake2Hash(Blake2Hash other) : base(other.GetPyType())
            {
                _wide = other._wide;
                _digestSize = other._digestSize;
                _h = (ulong[])other._h.Clone();
                _buffer = (byte[])other._buffer.Clone();
                _filled = other._filled;
                _t0 = other._t0;
                _t1 = other._t1;
                _lastNode = other._lastNode;
            }

            internal override string Name => _wide ? "blake2b" : "blake2s";
            internal override int DigestSize => _digestSize;
            internal override int BlockSize => _buffer.Length;
            internal override HashObject Copy() => new Blake2Hash(this);

            private void Increment(int bytes)
            {
                if (_wide)
                {
                    _t0 += (ulong)bytes;
                    if (_t0 < (ulong)bytes) _t1++;
                }
                else
                {
                    ulong t = (_t0 & 0xffffffffUL) + (ulong)bytes;
                    _t0 = t & 0xffffffffUL;
                    if (t >> 32 != 0) _t1 = (_t1 + 1) & 0xffffffffUL;
                }
            }

            // blake2b_update / blake2s_update: a full buffer is compressed only once more input comes,
            // so the last block is the one finalization marks.
            internal override void Update(byte[] data)
            {
                int offset = 0, block = _buffer.Length;
                while (offset < data.Length)
                {
                    if (_filled == block)
                    {
                        Increment(block);
                        Compress(_h, _buffer, _t0, _t1, false, false);
                        _filled = 0;
                    }
                    int take = Math.Min(block - _filled, data.Length - offset);
                    Array.Copy(data, offset, _buffer, _filled, take);
                    _filled += take;
                    offset += take;
                }
            }

            internal override byte[] Digest()
            {
                var h = (ulong[])_h.Clone();
                var block = new byte[_buffer.Length];
                Array.Copy(_buffer, block, _filled);
                ulong t0 = _t0, t1 = _t1;
                if (_wide)
                {
                    t0 += (ulong)_filled;
                    if (t0 < (ulong)_filled) t1++;
                }
                else
                {
                    ulong t = t0 + (ulong)_filled;
                    t0 = t & 0xffffffffUL;
                    if (t >> 32 != 0) t1 = (t1 + 1) & 0xffffffffUL;
                }
                Compress(h, block, t0, t1, true, _lastNode);
                var output = new byte[_digestSize];
                int word = _wide ? 8 : 4;
                for (int i = 0; i < _digestSize; i++)
                    output[i] = (byte)(h[i / word] >> (8 * (i % word)));
                return output;
            }

            private void Compress(ulong[] h, byte[] block, ulong t0, ulong t1, bool last, bool lastNode)
            {
                if (_wide) Blake2.CompressB(h, block, t0, t1, last, lastNode);
                else Blake2.CompressS(h, block, t0, t1, last, lastNode);
            }
        }

        private static class Blake2
        {
            internal static readonly ulong[] IVb =
            {
                0x6a09e667f3bcc908UL, 0xbb67ae8584caa73bUL, 0x3c6ef372fe94f82bUL, 0xa54ff53a5f1d36f1UL,
                0x510e527fade682d1UL, 0x9b05688c2b3e6c1fUL, 0x1f83d9abfb41bd6bUL, 0x5be0cd19137e2179UL,
            };

            internal static readonly uint[] IVs =
            {
                0x6A09E667U, 0xBB67AE85U, 0x3C6EF372U, 0xA54FF53AU, 0x510E527FU, 0x9B05688CU, 0x1F83D9ABU, 0x5BE0CD19U,
            };

            private static readonly byte[,] Sigma =
            {
                { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 },
                { 14, 10, 4, 8, 9, 15, 13, 6, 1, 12, 0, 2, 11, 7, 5, 3 },
                { 11, 8, 12, 0, 5, 2, 15, 13, 10, 14, 3, 6, 7, 1, 9, 4 },
                { 7, 9, 3, 1, 13, 12, 11, 14, 2, 6, 5, 10, 4, 0, 15, 8 },
                { 9, 0, 5, 7, 2, 4, 10, 15, 14, 1, 11, 12, 6, 8, 3, 13 },
                { 2, 12, 6, 10, 0, 11, 8, 3, 4, 13, 7, 5, 15, 14, 1, 9 },
                { 12, 5, 1, 15, 14, 13, 4, 10, 0, 7, 6, 3, 9, 2, 8, 11 },
                { 13, 11, 7, 14, 12, 1, 3, 9, 5, 0, 15, 4, 8, 6, 2, 10 },
                { 6, 15, 14, 9, 11, 3, 0, 8, 12, 2, 13, 7, 1, 4, 10, 5 },
                { 10, 2, 8, 4, 7, 6, 1, 5, 15, 11, 9, 14, 3, 12, 13, 0 },
            };

            // blake2b_compress: twelve rounds of G over 64-bit words.
            internal static void CompressB(ulong[] h, byte[] block, ulong t0, ulong t1, bool last, bool lastNode)
            {
                var m = new ulong[16];
                for (int i = 0; i < 16; i++)
                {
                    ulong word = 0;
                    for (int b = 0; b < 8; b++) word |= (ulong)block[8 * i + b] << (8 * b);
                    m[i] = word;
                }
                var v = new ulong[16];
                Array.Copy(h, v, 8);
                Array.Copy(IVb, 0, v, 8, 8);
                v[12] ^= t0;
                v[13] ^= t1;
                if (last) v[14] = ~v[14];
                if (lastNode) v[15] = ~v[15];
                for (int round = 0; round < 12; round++)
                {
                    int s = round % 10;
                    G(v, 0, 4, 8, 12, m[Sigma[s, 0]], m[Sigma[s, 1]]);
                    G(v, 1, 5, 9, 13, m[Sigma[s, 2]], m[Sigma[s, 3]]);
                    G(v, 2, 6, 10, 14, m[Sigma[s, 4]], m[Sigma[s, 5]]);
                    G(v, 3, 7, 11, 15, m[Sigma[s, 6]], m[Sigma[s, 7]]);
                    G(v, 0, 5, 10, 15, m[Sigma[s, 8]], m[Sigma[s, 9]]);
                    G(v, 1, 6, 11, 12, m[Sigma[s, 10]], m[Sigma[s, 11]]);
                    G(v, 2, 7, 8, 13, m[Sigma[s, 12]], m[Sigma[s, 13]]);
                    G(v, 3, 4, 9, 14, m[Sigma[s, 14]], m[Sigma[s, 15]]);
                }
                for (int i = 0; i < 8; i++)
                    h[i] ^= v[i] ^ v[i + 8];
            }

            private static void G(ulong[] v, int a, int b, int c, int d, ulong x, ulong y)
            {
                v[a] = v[a] + v[b] + x; v[d] = RotateRight(v[d] ^ v[a], 32);
                v[c] = v[c] + v[d]; v[b] = RotateRight(v[b] ^ v[c], 24);
                v[a] = v[a] + v[b] + y; v[d] = RotateRight(v[d] ^ v[a], 16);
                v[c] = v[c] + v[d]; v[b] = RotateRight(v[b] ^ v[c], 63);
            }

            // blake2s_compress: ten rounds of G over 32-bit words.
            internal static void CompressS(ulong[] h, byte[] block, ulong t0, ulong t1, bool last, bool lastNode)
            {
                var m = new uint[16];
                for (int i = 0; i < 16; i++)
                    m[i] = (uint)block[4 * i] | ((uint)block[4 * i + 1] << 8) | ((uint)block[4 * i + 2] << 16) | ((uint)block[4 * i + 3] << 24);
                var v = new uint[16];
                for (int i = 0; i < 8; i++) v[i] = (uint)h[i];
                Array.Copy(IVs, 0, v, 8, 8);
                v[12] ^= (uint)t0;
                v[13] ^= (uint)t1;
                if (last) v[14] = ~v[14];
                if (lastNode) v[15] = ~v[15];
                for (int round = 0; round < 10; round++)
                {
                    GS(v, 0, 4, 8, 12, m[Sigma[round, 0]], m[Sigma[round, 1]]);
                    GS(v, 1, 5, 9, 13, m[Sigma[round, 2]], m[Sigma[round, 3]]);
                    GS(v, 2, 6, 10, 14, m[Sigma[round, 4]], m[Sigma[round, 5]]);
                    GS(v, 3, 7, 11, 15, m[Sigma[round, 6]], m[Sigma[round, 7]]);
                    GS(v, 0, 5, 10, 15, m[Sigma[round, 8]], m[Sigma[round, 9]]);
                    GS(v, 1, 6, 11, 12, m[Sigma[round, 10]], m[Sigma[round, 11]]);
                    GS(v, 2, 7, 8, 13, m[Sigma[round, 12]], m[Sigma[round, 13]]);
                    GS(v, 3, 4, 9, 14, m[Sigma[round, 14]], m[Sigma[round, 15]]);
                }
                for (int i = 0; i < 8; i++)
                    h[i] = (uint)h[i] ^ v[i] ^ v[i + 8];
            }

            private static void GS(uint[] v, int a, int b, int c, int d, uint x, uint y)
            {
                v[a] = v[a] + v[b] + x; v[d] = RotateRight(v[d] ^ v[a], 16);
                v[c] = v[c] + v[d]; v[b] = RotateRight(v[b] ^ v[c], 12);
                v[a] = v[a] + v[b] + y; v[d] = RotateRight(v[d] ^ v[a], 8);
                v[c] = v[c] + v[d]; v[b] = RotateRight(v[b] ^ v[c], 7);
            }
        }

        private static readonly PyType Blake2bType = new("blake2b", new[] { PyType.ObjectType }, "_blake2");
        private static readonly PyType Blake2sType = new("blake2s", new[] { PyType.ObjectType }, "_blake2");

        private static readonly string[] Blake2Parameters =
        {
            "data", "digest_size", "key", "salt", "person", "fanout", "depth", "leaf_size", "node_offset",
            "node_depth", "inner_size", "last_node", "usedforsecurity",
        };

        /// <summary>
        /// py_blake2b_new_impl and py_blake2s_new_impl: the parameter block (blake2b_param) from the
        /// arguments, each checked as there; the key, padded to a block, is the first input.
        /// </summary>
        private static PyObject Blake2New(bool wide, PyObject[] args, PyDict kwargs)
        {
            string fname = wide ? "blake2b" : "blake2s";
            int maxDigest = wide ? 64 : 32, maxKey = wide ? 64 : 32, saltSize = wide ? 16 : 8, personSize = wide ? 16 : 8;
            var p = PyArgs.Unpack(fname, args, kwargs, Blake2Parameters, 1, 0, 1);
            int digestSize = p[1] != null ? PyArgs.Int(p[1]) : maxDigest;
            if (digestSize <= 0 || digestSize > maxDigest)
                throw PyValueError.Create($"digest_size must be between 1 and {maxDigest} bytes");
            byte[] key = p[2] != null ? BytesMethods.RequireBuffer(p[2]) : Array.Empty<byte>();
            byte[] salt = p[3] != null ? BytesMethods.RequireBuffer(p[3]) : Array.Empty<byte>();
            byte[] person = p[4] != null ? BytesMethods.RequireBuffer(p[4]) : Array.Empty<byte>();
            int fanout = p[5] != null ? PyArgs.Int(p[5]) : 1;
            int depth = p[6] != null ? PyArgs.Int(p[6]) : 1;
            ulong leafSize = p[7] != null ? UnsignedLong(p[7]) : 0;
            ulong nodeOffset = p[8] != null ? UnsignedLongLong(p[8]) : 0;
            int nodeDepth = p[9] != null ? PyArgs.Int(p[9]) : 0;
            int innerSize = p[10] != null ? PyArgs.Int(p[10]) : 0;
            bool lastNode = p[11]?.IsTrue() ?? false;
            p[12]?.IsTrue();

            if (salt.Length > saltSize)
                throw PyValueError.Create($"maximum salt length is {saltSize} bytes");
            if (person.Length > personSize)
                throw PyValueError.Create($"maximum person length is {personSize} bytes");
            if (fanout < 0 || fanout > 255)
                throw PyValueError.Create("fanout must be between 0 and 255");
            if (depth <= 0 || depth > 255)
                throw PyValueError.Create("depth must be between 1 and 255");
            if (!wide && nodeOffset > 0xFFFFFFFFFFFFUL)
                throw PyOverflowError.Create("node_offset is too large");
            if (nodeDepth < 0 || nodeDepth > 255)
                throw PyValueError.Create("node_depth must be between 0 and 255");
            if (innerSize < 0 || innerSize > maxDigest)
                throw PyValueError.Create($"inner_size must be between 0 and is {maxDigest}");
            if (key.Length > maxKey)
                throw PyValueError.Create($"maximum key length is {maxKey} bytes");

            // blake2b_param / blake2s_param, little-endian.
            var param = new byte[wide ? 64 : 32];
            param[0] = (byte)digestSize;
            param[1] = (byte)key.Length;
            param[2] = (byte)fanout;
            param[3] = (byte)depth;
            for (int i = 0; i < 4; i++) param[4 + i] = (byte)(leafSize >> (8 * i));
            int offsetBytes = wide ? 8 : 6;
            for (int i = 0; i < offsetBytes; i++) param[8 + i] = (byte)(nodeOffset >> (8 * i));
            param[8 + offsetBytes] = (byte)nodeDepth;
            param[9 + offsetBytes] = (byte)innerSize;
            int saltAt = wide ? 32 : 16;
            Array.Copy(salt, 0, param, saltAt, salt.Length);
            Array.Copy(person, 0, param, saltAt + saltSize, person.Length);

            var h = new ulong[8];
            for (int i = 0; i < 8; i++)
            {
                if (wide)
                {
                    ulong word = 0;
                    for (int b = 0; b < 8; b++) word |= (ulong)param[8 * i + b] << (8 * b);
                    h[i] = Blake2.IVb[i] ^ word;
                }
                else
                {
                    uint word = (uint)param[4 * i] | ((uint)param[4 * i + 1] << 8) | ((uint)param[4 * i + 2] << 16) | ((uint)param[4 * i + 3] << 24);
                    h[i] = Blake2.IVs[i] ^ word;
                }
            }
            var hash = new Blake2Hash(wide, digestSize, h, lastNode);
            if (key.Length > 0)
            {
                var block = new byte[wide ? 128 : 64];
                Array.Copy(key, block, key.Length);
                hash.Update(block);
            }
            if (p[0] != null)
                hash.Update(DataOf(p[0]));
            return hash;
        }

        internal static PyModule CreateBlake2()
        {
            var module = new PyModule("_blake2");
            foreach (var (type, wide) in new[] { (Blake2bType, true), (Blake2sType, false) })
            {
                bool w = wide;
                type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) => Blake2New(w, args, kwargs), acceptsKwargs: true);
                InstallCommon(type);
                type.TypeDict["SALT_SIZE"] = PyInt.From(wide ? 16 : 8);
                type.TypeDict["PERSON_SIZE"] = PyInt.From(wide ? 16 : 8);
                type.TypeDict["MAX_KEY_SIZE"] = PyInt.From(wide ? 64 : 32);
                type.TypeDict["MAX_DIGEST_SIZE"] = PyInt.From(wide ? 64 : 32);
            }
            module.ModuleDict["blake2b"] = Blake2bType;
            module.ModuleDict["blake2s"] = Blake2sType;
            module.ModuleDict["BLAKE2B_SALT_SIZE"] = PyInt.From(16);
            module.ModuleDict["BLAKE2B_PERSON_SIZE"] = PyInt.From(16);
            module.ModuleDict["BLAKE2B_MAX_KEY_SIZE"] = PyInt.From(64);
            module.ModuleDict["BLAKE2B_MAX_DIGEST_SIZE"] = PyInt.From(64);
            module.ModuleDict["BLAKE2S_SALT_SIZE"] = PyInt.From(8);
            module.ModuleDict["BLAKE2S_PERSON_SIZE"] = PyInt.From(8);
            module.ModuleDict["BLAKE2S_MAX_KEY_SIZE"] = PyInt.From(32);
            module.ModuleDict["BLAKE2S_MAX_DIGEST_SIZE"] = PyInt.From(32);
            return module;
        }
    }
}
