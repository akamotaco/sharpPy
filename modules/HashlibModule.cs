using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_hashopenssl.c (CPython 3.12.15), the _hashlib module, with libcrypto replaced by the
    /// managed digests of HashlibEvp.cs (Evp): the HASH, HASHXOF and HMAC types, new() and the
    /// openssl_* constructors, pbkdf2_hmac, scrypt, hmac_digest, hmac_new, compare_digest and
    /// get_fips_mode. One C# method per C function, named after it. The algorithms are those the
    /// reference build's OpenSSL 3 default provider has, and the names, sizes and error texts are
    /// its own (raise_ssl_error reads Evp's error queue as it reads ERR_peek_last_error).
    /// </summary>
    internal static class HashlibModule
    {
        private const int INT_MAX = int.MaxValue;

        /// <summary>enum Py_hash_type</summary>
        private enum Py_hash_type
        {
            Py_ht_evp,            // usedforsecurity=True / default
            Py_ht_evp_nosecurity, // usedforsecurity=False
            Py_ht_mac,            // HMAC
            Py_ht_pbkdf2,         // PKBDF2
        }

        /// <summary>py_hashentry_t: Python's preferred name, its alias and the OpenSSL short name (SN_*).</summary>
        private sealed record py_hashentry_t(string py_name, string py_alias, string ossl_name);

        // py_hashes: OpenSSL's NIDs are known by their short names here (Evp.Md.Sn).
        private static readonly py_hashentry_t[] py_hashes =
        {
            /* md5 */
            new("md5", "MD5", "MD5"),
            /* sha1 */
            new("sha1", "SHA1", "SHA1"),
            /* sha2 family */
            new("sha224", "SHA224", "SHA224"),
            new("sha256", "SHA256", "SHA256"),
            new("sha384", "SHA384", "SHA384"),
            new("sha512", "SHA512", "SHA512"),
            /* truncated sha2 */
            new("sha512_224", "SHA512_224", "SHA512-224"),
            new("sha512_256", "SHA512_256", "SHA512-256"),
            /* sha3 */
            new("sha3_224", null, "SHA3-224"),
            new("sha3_256", null, "SHA3-256"),
            new("sha3_384", null, "SHA3-384"),
            new("sha3_512", null, "SHA3-512"),
            /* sha3 shake */
            new("shake_128", null, "SHAKE128"),
            new("shake_256", null, "SHAKE256"),
            /* blake2 digest */
            new("blake2s", "blake2s256", "BLAKE2s256"),
            new("blake2b", "blake2b512", "BLAKE2b512"),
        };

        // ---- module state (_hashlibstate): one per process, as the module's types are ----

        private static readonly PyType EVPtype = hashlib_init_evptype();
        private static readonly PyType EVPXOFtype = hashlib_init_evpxoftype();
        private static readonly PyType HMACtype = hashlib_init_hmactype();
        private static readonly PyType unsupported_digestmod_error =
            PyExceptionSlots.NewException("_hashlib", "UnsupportedDigestmodError", PyType.ValueErrorType);
        private static readonly Dictionary<string, py_hashentry_t> hashtable = py_hashentry_table_new();
        private static PyDict constructs;

        /// <summary>EVPobject: an EVP_MD_CTX and the lock ENTER_HASHLIB takes.</summary>
        private sealed class EVPobject : PyObject
        {
            private readonly PyType _type;
            internal readonly Evp.MdCtx ctx = new();
            internal readonly object Lock = new();

            internal EVPobject(PyType type) => _type = type;

            public override PyType GetPyType() => _type;
            protected internal override PyString Repr() => EVP_repr(this);
        }

        /// <summary>HMACobject: an HMAC_CTX and its lock.</summary>
        private sealed class HMACobject : PyObject
        {
            private readonly PyType _type;
            internal Evp.HmacCtx ctx;
            internal readonly object Lock = new();

            internal HMACobject(PyType type) => _type = type;

            public override PyType GetPyType() => _type;
            protected internal override PyString Repr() => _hmac_repr(this);
        }

        /// <summary>py_hashentry_table_new: py_name and py_alias, both, to their entry.</summary>
        private static Dictionary<string, py_hashentry_t> py_hashentry_table_new()
        {
            var ht = new Dictionary<string, py_hashentry_t>(StringComparer.Ordinal);
            foreach (var h in py_hashes)
            {
                ht[h.py_name] = h;
                if (h.py_alias != null)
                    ht[h.py_alias] = h;
            }
            return ht;
        }

        /// <summary>set_ssl_exception_from_errcode: "[lib] reason" (OpenSSL 3 has no function strings).</summary>
        private static PythonException set_ssl_exception_from_errcode(PyType exc, string lib, string reason) =>
            PyExceptionSlots.Raise(exc, lib != null ? $"[{lib}] {reason}" : reason);

        /// <summary>
        /// raise_ssl_error: the last queued error, which clears the queue, or the fallback text.
        /// </summary>
        private static PythonException raise_ssl_error(PyType exc, string fallback)
        {
            if (Evp.ErrPeekLastError(out var lib, out var reason))
            {
                Evp.ErrClearError();
                return set_ssl_exception_from_errcode(exc, lib, reason);
            }
            return PyExceptionSlots.Raise(exc, fallback);
        }

        /// <summary>notify_ssl_error_occurred</summary>
        private static PythonException notify_ssl_error_occurred() => raise_ssl_error(PyType.ValueErrorType, "no reason supplied");

        /// <summary>py_digest_name: the py_hashes name of the digest's NID, else OBJ_nid2ln (or OBJ_nid2sn).</summary>
        private static PyString py_digest_name(Evp.Md md)
        {
            string name = null;
            foreach (var h in py_hashes)
            {
                if (md.Sn != null && h.ossl_name == md.Sn)
                {
                    name = h.py_name;
                    break;
                }
            }
            // Ignore aliased names and only use long, lowercase name. NID_undef's long name is "undefined".
            name ??= md.Sn == null ? "undefined" : md.Ln ?? md.Sn;
            return PyString.From(name);
        }

        /// <summary>py_digest_by_name: the table's entry, else an OpenSSL name; "unsupported hash type" else.</summary>
        private static Evp.Md py_digest_by_name(string name, Py_hash_type py_ht)
        {
            Evp.Md digest;
            if (hashtable.TryGetValue(name, out var entry))
                // Py_ht_evp_nosecurity fetches with "-fips": the same default provider.
                digest = Evp.Fetch(entry.ossl_name);
            else
                // Fall back for looking up an unindexed OpenSSL specific name.
                digest = Evp.Fetch(name);
            if (digest == null)
                throw raise_ssl_error(unsupported_digestmod_error, $"unsupported hash type {name}");
            return digest;
        }

        /// <summary>
        /// py_digest_by_digestmod: a str, or an openssl_* constructor found in the module's
        /// constructors dict; its name read as a C string (PyUnicode_AsUTF8).
        /// </summary>
        private static Evp.Md py_digest_by_digestmod(PyObject digestmod, Py_hash_type py_ht)
        {
            PyObject name_obj;
            if (digestmod is PyString)
                name_obj = digestmod;
            else if (!constructs.TryGetStoredValue(digestmod, out name_obj))
                // PyDict_GetItemWithError: an unhashable digestmod raises in the lookup
                name_obj = null;
            if (name_obj == null)
                throw PyExceptionSlots.Raise(unsupported_digestmod_error, $"Unsupported digestmod {digestmod.ToRepr().Value}");
            string name = PyArgs.AsUtf8CString((PyString)name_obj);
            return py_digest_by_name(name, py_ht);
        }

        /// <summary>newEVPobject</summary>
        private static EVPobject newEVPobject(PyType type) => new(type);

        /// <summary>EVP_hash: EVP_DigestUpdate (in MUNCH_SIZE pieces there; a CLR array is shorter).</summary>
        private static void EVP_hash(EVPobject self, ReadOnlySpan<byte> data)
        {
            if (!self.ctx.DigestUpdate(data))
                throw notify_ssl_error_occurred();
        }

        /// <summary>locked_EVP_MD_CTX_copy</summary>
        private static bool locked_EVP_MD_CTX_copy(Evp.MdCtx new_ctx_p, EVPobject self)
        {
            lock (self.Lock)
                return new_ctx_p.CopyFrom(self.ctx);
        }

        /// <summary>EVP_copy_impl: _hashlib.HASH.copy</summary>
        private static PyObject EVP_copy_impl(EVPobject self)
        {
            var newobj = newEVPobject(self.GetPyType());
            if (!locked_EVP_MD_CTX_copy(newobj.ctx, self))
                throw notify_ssl_error_occurred();
            return newobj;
        }

        /// <summary>EVP_digest_impl: _hashlib.HASH.digest, the final digest of a copy.</summary>
        private static PyObject EVP_digest_impl(EVPobject self)
        {
            var temp_ctx = new Evp.MdCtx();
            if (!locked_EVP_MD_CTX_copy(temp_ctx, self))
                throw notify_ssl_error_occurred();
            var digest = temp_ctx.DigestFinal();
            if (digest == null)
                throw notify_ssl_error_occurred();
            return PyBytes.From(digest);
        }

        /// <summary>EVP_hexdigest_impl: _hashlib.HASH.hexdigest</summary>
        private static PyObject EVP_hexdigest_impl(EVPobject self)
        {
            var temp_ctx = new Evp.MdCtx();
            /* Get the raw (binary) digest value */
            if (!locked_EVP_MD_CTX_copy(temp_ctx, self))
                throw notify_ssl_error_occurred();
            var digest = temp_ctx.DigestFinal();
            if (digest == null)
                throw notify_ssl_error_occurred();
            return PyString.From(HashModules.Hex(digest));
        }

        /// <summary>EVP_update: _hashlib.HASH.update(obj, /)</summary>
        private static PyObject EVP_update(EVPobject self, PyObject obj)
        {
            var view = HashModules.GetBufferView(obj);
            lock (self.Lock)
                EVP_hash(self, view);
            return PyNone.Instance;
        }

        /// <summary>EVP_get_block_size: EVP_MD_CTX_block_size</summary>
        private static PyObject EVP_get_block_size(EVPobject self) => PyInt.From(self.ctx.Digest.BlockSize);

        /// <summary>EVP_get_digest_size: EVP_MD_CTX_size (-1 for SHAKE)</summary>
        private static PyObject EVP_get_digest_size(EVPobject self) => PyInt.From(self.ctx.Digest.Size);

        /// <summary>EVP_get_name</summary>
        private static PyObject EVP_get_name(EVPobject self) => py_digest_name(self.ctx.Digest);

        /// <summary>EVP_repr: "&lt;name _hashlib.HASH object @ %p&gt;"</summary>
        private static PyString EVP_repr(EVPobject self)
        {
            var name_obj = py_digest_name(self.ctx.Digest);
            return PyString.From($"<{name_obj.Value} {self.GetPyType().FullName} object @ {PyObjectId.Pointer(self)}>");
        }

        /// <summary>EVPXOF_digest_impl: _hashlib.HASHXOF.digest(length)</summary>
        private static PyObject EVPXOF_digest_impl(EVPobject self, long length)
        {
            // PyBytes_FromStringAndSize(NULL, length)
            if (length < 0)
                throw PySystemError.Create("Negative size passed to PyBytes_FromStringAndSize");
            int size = PyAllocation.ArrayLength(length);
            var temp_ctx = new Evp.MdCtx();
            if (!locked_EVP_MD_CTX_copy(temp_ctx, self))
                throw notify_ssl_error_occurred();
            return PyBytes.From(temp_ctx.DigestFinalXOF(size));
        }

        /// <summary>EVPXOF_hexdigest_impl: _hashlib.HASHXOF.hexdigest(length)</summary>
        private static PyObject EVPXOF_hexdigest_impl(EVPobject self, long length)
        {
            // PyMem_Malloc(length): a negative length is a size_t past any allocation
            if (length < 0)
                throw PyMemoryError.Create();
            int size = PyAllocation.ArrayLength(length);
            PyAllocation.StringLength(2 * (long)size);
            var temp_ctx = new Evp.MdCtx();
            /* Get the raw (binary) digest value */
            if (!locked_EVP_MD_CTX_copy(temp_ctx, self))
                throw notify_ssl_error_occurred();
            return PyString.From(HashModules.Hex(temp_ctx.DigestFinalXOF(size)));
        }

        /// <summary>EVPXOF_get_digest_size</summary>
        private static PyObject EVPXOF_get_digest_size(EVPobject self) => PyInt.From(0);

        /// <summary>py_evp_fromname: the data's buffer first, then the digest, then a HASH or HASHXOF.</summary>
        private static PyObject py_evp_fromname(string digestname, PyObject data_obj, bool usedforsecurity)
        {
            byte[] view = null;
            if (data_obj != null)
                view = HashModules.GetBufferView(data_obj);

            var digest = py_digest_by_name(digestname, usedforsecurity ? Py_hash_type.Py_ht_evp : Py_hash_type.Py_ht_evp_nosecurity);
            var type = digest.Xof ? EVPXOFtype : EVPtype;
            var self = newEVPobject(type);
            if (!self.ctx.DigestInit(digest))
                throw notify_ssl_error_occurred();
            if (view != null && view.Length > 0)
                EVP_hash(self, view);
            return self;
        }

        /// <summary>
        /// EVP_new_impl: _hashlib.new(name, string=b'', *, usedforsecurity=True). The name is read
        /// with PyArg_Parse's "s": a str without NUL that UTF-8 can encode, else "name must be a string".
        /// </summary>
        private static PyObject EVP_new_impl(PyObject name_obj, PyObject data_obj, bool usedforsecurity)
        {
            string name;
            try
            {
                if (name_obj is not PyString text)
                    throw PyTypeError.Create("name must be a string");
                name = PyArgs.AsUtf8(text);
                if (name.IndexOf('\0') >= 0)
                    throw PyValueError.Create("embedded null character");
            }
            catch (PythonException)
            {
                throw PyTypeError.Create("name must be a string");
            }
            return py_evp_fromname(name, data_obj, usedforsecurity);
        }

        /// <summary>
        /// The clinic of _hashlib.openssl_md5 and the others: (string=b'', *, usedforsecurity=True),
        /// then their _impl, py_evp_fromname of the fixed name.
        /// </summary>
        private static PyBuiltinFunction openssl_constructor(string fname, string py_name) =>
            new(fname, (args, kwargs) =>
            {
                var parsed = PyArgs.Unpack(fname, args, kwargs, new[] { "string", "usedforsecurity" }, 0, 0, 1);
                bool usedforsecurity = parsed[1] == null || parsed[1].IsTrue();
                return py_evp_fromname(py_name, parsed[0], usedforsecurity);
            });

        /// <summary>pbkdf2_hmac_impl</summary>
        private static PyObject pbkdf2_hmac_impl(string hash_name, byte[] password, byte[] salt, long iterations, PyObject dklen_obj)
        {
            var digest = py_digest_by_name(hash_name, Py_hash_type.Py_ht_pbkdf2);

            if (password.Length > INT_MAX)
                throw PyOverflowError.Create("password is too long.");
            if (salt.Length > INT_MAX)
                throw PyOverflowError.Create("salt is too long.");
            if (iterations < 1)
                throw PyValueError.Create("iteration value must be greater than 0.");
            if (iterations > INT_MAX)
                throw PyOverflowError.Create("iteration value is too great.");

            long dklen;
            if (dklen_obj is PyNone)
                dklen = digest.Size;
            else
                dklen = PyArgs.CLong(dklen_obj);
            if (dklen < 1)
                throw PyValueError.Create("key length must be greater than 0.");
            if (dklen > INT_MAX)
                /* INT_MAX is always smaller than dkLen max (2^32 - 1) * hLen */
                throw PyOverflowError.Create("key length is too great.");

            var key = new byte[PyAllocation.ArrayLength(dklen)];
            if (!Evp.Pkcs5Pbkdf2Hmac(password, salt, (int)iterations, digest, key))
                throw notify_ssl_error_occurred();
            return PyBytes.From(key);
        }

        /// <summary>
        /// _hashlib_scrypt_impl: n, r and p by PyLong_AsUnsignedLong (any failure is "... is required
        /// and must be an unsigned int"); OpenSSL validates the rest without a key first.
        /// </summary>
        private static PyObject _hashlib_scrypt_impl(byte[] password, byte[] salt, PyObject n_obj, PyObject r_obj, PyObject p_obj, long maxmem, long dklen)
        {
            if (password.Length > INT_MAX)
                throw PyOverflowError.Create("password is too long.");
            if (salt == null)
                throw PyTypeError.Create("salt is required");
            if (salt.Length > INT_MAX)
                throw PyOverflowError.Create("salt is too long.");

            ulong n = PyLong_AsUnsignedLong(n_obj, "n is required and must be an unsigned int");
            if (n < 2 || (n & (n - 1)) != 0)
                throw PyValueError.Create("n must be a power of 2.");
            ulong r = PyLong_AsUnsignedLong(r_obj, "r is required and must be an unsigned int");
            ulong p = PyLong_AsUnsignedLong(p_obj, "p is required and must be an unsigned int");

            if (maxmem < 0 || maxmem > INT_MAX)
                /* OpenSSL 1.1.0 restricts maxmem to 32 MiB. It may change in the future. The maxmem constant is private to OpenSSL. */
                throw PyValueError.Create($"maxmem must be positive and smaller than {INT_MAX}");
            if (dklen < 1 || dklen > INT_MAX)
                throw PyValueError.Create($"dklen must be greater than 0 and smaller than {INT_MAX}");

            /* let OpenSSL validate the rest */
            if (!Evp.PbeScrypt(default, default, n, r, p, (ulong)maxmem, null))
                throw raise_ssl_error(PyType.ValueErrorType, "Invalid parameter combination for n, r, p, maxmem.");

            var key = new byte[PyAllocation.ArrayLength(dklen)];
            if (!Evp.PbeScrypt(password, salt, n, r, p, (ulong)maxmem, key))
                throw notify_ssl_error_occurred();
            return PyBytes.From(key);
        }

        // PyLong_AsUnsignedLong where _hashlib_scrypt_impl replaces whatever it raised with its own TypeError.
        private static ulong PyLong_AsUnsignedLong(PyObject value, string message)
        {
            try
            {
                return PyArgs.AsUnsignedLong(value);
            }
            catch (PythonException)
            {
                throw PyTypeError.Create(message);
            }
        }

        /// <summary>_hashlib_hmac_singleshot_impl: _hashlib.hmac_digest(key, msg, digest)</summary>
        private static PyObject _hashlib_hmac_singleshot_impl(byte[] key, byte[] msg, PyObject digest)
        {
            if (key.Length > INT_MAX)
                throw PyOverflowError.Create("key is too long.");
            if (msg.Length > INT_MAX)
                throw PyOverflowError.Create("msg is too long.");

            var evp = py_digest_by_digestmod(digest, Py_hash_type.Py_ht_mac);
            var result = Evp.Hmac(evp, key, msg);
            if (result == null)
                throw notify_ssl_error_occurred();
            return PyBytes.From(result);
        }

        /// <summary>_hashlib_hmac_get_md: HMAC_CTX_get_md</summary>
        private static Evp.Md _hashlib_hmac_get_md(HMACobject self) =>
            self.ctx.Digest ?? throw raise_ssl_error(PyType.ValueErrorType, "missing EVP_MD for HMAC context");

        /// <summary>_hashlib_hmac_new_impl: _hashlib.hmac_new(key, msg=b'', digestmod=None)</summary>
        private static PyObject _hashlib_hmac_new_impl(byte[] key, PyObject msg_obj, PyObject digestmod)
        {
            if (key.Length > INT_MAX)
                throw PyOverflowError.Create("key is too long.");
            if (digestmod == null)
                throw PyTypeError.Create("Missing required parameter 'digestmod'.");

            var digest = py_digest_by_digestmod(digestmod, Py_hash_type.Py_ht_mac);
            var ctx = new Evp.HmacCtx();
            if (!ctx.Init(key, digest))
                throw notify_ssl_error_occurred();

            var self = new HMACobject(HMACtype) { ctx = ctx };
            if (msg_obj != null && msg_obj is not PyNone)
                _hmac_update(self, msg_obj);
            return self;
        }

        /// <summary>locked_HMAC_CTX_copy</summary>
        private static bool locked_HMAC_CTX_copy(Evp.HmacCtx new_ctx_p, HMACobject self)
        {
            lock (self.Lock)
                return new_ctx_p.CopyFrom(self.ctx);
        }

        /// <summary>_hashlib_hmac_digest_size: EVP_MD_size, of which 0 is "invalid digest size".</summary>
        private static int _hashlib_hmac_digest_size(HMACobject self)
        {
            var md = _hashlib_hmac_get_md(self);
            int digest_size = md.Size;
            if (digest_size == 0)
                throw raise_ssl_error(PyType.ValueErrorType, "invalid digest size");
            return digest_size;
        }

        /// <summary>_hmac_update: GET_BUFFER_VIEW_OR_ERROR, then HMAC_Update under the lock.</summary>
        private static void _hmac_update(HMACobject self, PyObject obj)
        {
            var view = HashModules.GetBufferView(obj);
            bool r;
            lock (self.Lock)
                r = self.ctx.Update(view);
            if (!r)
                throw notify_ssl_error_occurred();
        }

        /// <summary>_hashlib_HMAC_copy_impl: Return a copy ("clone") of the HMAC object.</summary>
        private static PyObject _hashlib_HMAC_copy_impl(HMACobject self)
        {
            var ctx = new Evp.HmacCtx();
            if (!locked_HMAC_CTX_copy(ctx, self))
                throw notify_ssl_error_occurred();
            return new HMACobject(self.GetPyType()) { ctx = ctx };
        }

        /// <summary>_hmac_repr: "&lt;name HMAC object @ %p&gt;"</summary>
        private static PyString _hmac_repr(HMACobject self)
        {
            var md = _hashlib_hmac_get_md(self);
            var digest_name = py_digest_name(md);
            return PyString.From($"<{digest_name.Value} HMAC object @ {PyObjectId.Pointer(self)}>");
        }

        /// <summary>_hashlib_HMAC_update_impl: _hashlib.HMAC.update(msg)</summary>
        private static PyObject _hashlib_HMAC_update_impl(HMACobject self, PyObject msg)
        {
            _hmac_update(self, msg);
            return PyNone.Instance;
        }

        /// <summary>_hmac_digest: HMAC_Final of a copy.</summary>
        private static byte[] _hmac_digest(HMACobject self)
        {
            var temp_ctx = new Evp.HmacCtx();
            if (!locked_HMAC_CTX_copy(temp_ctx, self))
                throw notify_ssl_error_occurred();
            return temp_ctx.Final() ?? throw notify_ssl_error_occurred();
        }

        /// <summary>_hashlib_HMAC_digest_impl</summary>
        private static PyObject _hashlib_HMAC_digest_impl(HMACobject self)
        {
            _hashlib_hmac_digest_size(self);
            return PyBytes.From(_hmac_digest(self));
        }

        /// <summary>_hashlib_HMAC_hexdigest_impl</summary>
        private static PyObject _hashlib_HMAC_hexdigest_impl(HMACobject self)
        {
            _hashlib_hmac_digest_size(self);
            return PyString.From(HashModules.Hex(_hmac_digest(self)));
        }

        /// <summary>_hashlib_hmac_get_digest_size</summary>
        private static PyObject _hashlib_hmac_get_digest_size(HMACobject self) => PyInt.From(_hashlib_hmac_digest_size(self));

        /// <summary>_hashlib_hmac_get_block_size</summary>
        private static PyObject _hashlib_hmac_get_block_size(HMACobject self) => PyInt.From(_hashlib_hmac_get_md(self).BlockSize);

        /// <summary>_hashlib_hmac_get_name: "hmac-" and the digest's name.</summary>
        private static PyObject _hashlib_hmac_get_name(HMACobject self) =>
            PyString.From("hmac-" + py_digest_name(_hashlib_hmac_get_md(self)).Value);

        /// <summary>_openssl_hash_name_mapper: a digest with a NID adds its Python name.</summary>
        private static void _openssl_hash_name_mapper(Evp.Md md, List<PyObject> set)
        {
            // ignore all undefined providers
            if (md == null || md.Sn == null)
                return;
            set.Add(py_digest_name(md));
        }

        /// <summary>hashlib_md_meth_names: EVP_MD_do_all_provided into openssl_md_meth_names.</summary>
        private static void hashlib_md_meth_names(PyModule module)
        {
            var set = new List<PyObject>();
            // get algorithms from all activated providers in default context
            foreach (var md in Evp.Provided)
                _openssl_hash_name_mapper(md, set);
            module.ModuleDict["openssl_md_meth_names"] = new PyFrozenSet(set);
        }

        /// <summary>
        /// _hashlib_get_fips_mode_impl: EVP_default_properties_is_fips_enabled(NULL). There is no
        /// FIPS provider here, so 0.
        /// </summary>
        private static int _hashlib_get_fips_mode_impl() => 0;

        /// <summary>_tscmp: the loop runs over b's length; different lengths compare b with itself.</summary>
        private static bool _tscmp(byte[] a, byte[] b)
        {
            int length = b.Length;
            byte[] left = a;
            byte[] right = b;
            int result = 0;
            if (a.Length != length)
            {
                left = b;
                result = 1;
            }
            result |= Evp.CryptoMemcmp(left, right, length);
            return result == 0;
        }

        // PyObject_CheckBuffer
        private static bool PyObject_CheckBuffer(PyObject obj) => obj is PyMemoryView || BytesMethods.Buffer(obj) != null;

        /// <summary>_hashlib_compare_digest_impl: two ASCII str, or two buffers.</summary>
        private static PyObject _hashlib_compare_digest_impl(PyObject a, PyObject b)
        {
            bool rc;
            /* ASCII unicode string */
            if (a is PyString sa && b is PyString sb)
            {
                if (!IsAscii(sa.Value) || !IsAscii(sb.Value))
                    throw PyTypeError.Create("comparing strings with non-ASCII characters is not supported");
                rc = _tscmp(System.Text.Encoding.ASCII.GetBytes(sa.Value), System.Text.Encoding.ASCII.GetBytes(sb.Value));
            }
            /* fallback to buffer interface for bytes, bytearray and other */
            else
            {
                if (!PyObject_CheckBuffer(a) && !PyObject_CheckBuffer(b))
                    throw PyTypeError.Create($"unsupported operand types(s) or combination of types: '{a.GetPyType().Name}' and '{b.GetPyType().Name}'");
                // PyBUF_SIMPLE views are one-dimensional: "Buffer must be single dimension" cannot follow.
                var view_a = BytesMethods.RequireBuffer(a);
                var view_b = BytesMethods.RequireBuffer(b);
                rc = _tscmp(view_a, view_b);
            }
            return PyBool.FromBool(rc);
        }

        private static bool IsAscii(string text)
        {
            foreach (char c in text)
                if (c > 0x7f)
                    return false;
            return true;
        }

        // ---- the types (PyType_Spec and their slots) ----

        /// <summary>
        /// hashlib_init_evptype: _hashlib.HASH, a heap type made from EVPtype_spec:
        /// BASETYPE | DISALLOW_INSTANTIATION | IMMUTABLETYPE (the layout table says the first two).
        /// </summary>
        private static PyType hashlib_init_evptype()
        {
            var type = new PyType("HASH", new[] { PyType.ObjectType }, "_hashlib");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            // PyType_FromSpec: __module__ from tp_name; Py_tp_repr EVP_repr as its slot wrapper.
            type.TypeDict["__module__"] = PyString.From("_hashlib");
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => EVP_repr((EVPobject)self), minArgs: 0, maxArgs: 0);
            // EVP_methods
            PyArgs.Define(type, "update", PyCall.One, (self, args) => EVP_update((EVPobject)self, args[0]));
            PyArgs.Define(type, "digest", PyCall.NoArgs, (self, args) => EVP_digest_impl((EVPobject)self));
            PyArgs.Define(type, "hexdigest", PyCall.NoArgs, (self, args) => EVP_hexdigest_impl((EVPobject)self));
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => EVP_copy_impl((EVPobject)self));
            // EVP_getseters
            type.TypeDict["digest_size"] = new PyGetSetDescriptor("digest_size", type, self => EVP_get_digest_size((EVPobject)self));
            type.TypeDict["block_size"] = new PyGetSetDescriptor("block_size", type, self => EVP_get_block_size((EVPobject)self));
            type.TypeDict["name"] = new PyGetSetDescriptor("name", type, self => EVP_get_name((EVPobject)self), doc: "algorithm name.");
            return type;
        }

        /// <summary>hashlib_init_evpxoftype: _hashlib.HASHXOF, EVPXOFtype_spec with HASH as its base.</summary>
        private static PyType hashlib_init_evpxoftype()
        {
            var type = new PyType("HASHXOF", new[] { EVPtype }, "_hashlib");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("_hashlib");
            // EVPXOF_methods: the clinic's (length) with a keyword, a Py_ssize_t
            type.TypeDict["digest"] = new PyMethodDescriptor("digest", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("digest", args, kwargs, new[] { "length" }, 0, 1, 1);
                return EVPXOF_digest_impl((EVPobject)self, PyArgs.Ssize(parsed[0]));
            }, acceptsKwargs: true);
            type.TypeDict["hexdigest"] = new PyMethodDescriptor("hexdigest", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("hexdigest", args, kwargs, new[] { "length" }, 0, 1, 1);
                return EVPXOF_hexdigest_impl((EVPobject)self, PyArgs.Ssize(parsed[0]));
            }, acceptsKwargs: true);
            // EVPXOF_getseters
            type.TypeDict["digest_size"] = new PyGetSetDescriptor("digest_size", type, self => EVPXOF_get_digest_size((EVPobject)self));
            return type;
        }

        /// <summary>hashlib_init_hmactype: _hashlib.HMAC, final, DISALLOW_INSTANTIATION | IMMUTABLETYPE.</summary>
        private static PyType hashlib_init_hmactype()
        {
            var type = new PyType("HMAC", new[] { PyType.ObjectType }, "_hashlib");
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__module__"] = PyString.From("_hashlib");
            type.TypeDict["__repr__"] = new PyWrapperDescriptor("__repr__", type,
                (self, args, kwargs) => _hmac_repr((HMACobject)self), minArgs: 0, maxArgs: 0);
            // HMAC_methods
            type.TypeDict["update"] = new PyMethodDescriptor("update", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("update", args, kwargs, new[] { "msg" }, 0, 1, 1);
                return _hashlib_HMAC_update_impl((HMACobject)self, parsed[0]);
            }, acceptsKwargs: true);
            PyArgs.Define(type, "digest", PyCall.NoArgs, (self, args) => _hashlib_HMAC_digest_impl((HMACobject)self));
            PyArgs.Define(type, "hexdigest", PyCall.NoArgs, (self, args) => _hashlib_HMAC_hexdigest_impl((HMACobject)self));
            PyArgs.Define(type, "copy", PyCall.NoArgs, (self, args) => _hashlib_HMAC_copy_impl((HMACobject)self));
            // HMAC_getset
            type.TypeDict["digest_size"] = new PyGetSetDescriptor("digest_size", type, self => _hashlib_hmac_get_digest_size((HMACobject)self));
            type.TypeDict["block_size"] = new PyGetSetDescriptor("block_size", type, self => _hashlib_hmac_get_block_size((HMACobject)self));
            type.TypeDict["name"] = new PyGetSetDescriptor("name", type, self => _hashlib_hmac_get_name((HMACobject)self));
            return type;
        }

        // ---- the module (EVP_functions and hashlib_slots) ----

        /// <summary>
        /// PyInit__hashlib and its Py_mod_exec slots in order: the hashtable and the types (static
        /// fields above), EVP_functions, hashlib_md_meth_names, hashlib_init_constructors,
        /// hashlib_exception.
        /// </summary>
        internal static PyModule Create()
        {
            var module = new PyModule("_hashlib");
            var d = module.ModuleDict;
            d["HASH"] = EVPtype;
            d["HASHXOF"] = EVPXOFtype;
            d["HMAC"] = HMACtype;

            // EVP_functions
            d["new"] = new PyBuiltinFunction("new", (args, kwargs) =>
            {
                // _hashlib.new as EVP_new: (name, string=b'', *, usedforsecurity=True)
                var parsed = PyArgs.Unpack("new", args, kwargs, new[] { "name", "string", "usedforsecurity" }, 0, 1, 2);
                bool usedforsecurity = parsed[2] == null || parsed[2].IsTrue();
                return EVP_new_impl(parsed[0], parsed[1], usedforsecurity);
            });
            d["pbkdf2_hmac"] = new PyBuiltinFunction("pbkdf2_hmac", (args, kwargs) =>
            {
                // (hash_name: str, password: Py_buffer, salt: Py_buffer, iterations: long, dklen=None)
                var parsed = PyArgs.Unpack("pbkdf2_hmac", args, kwargs, new[] { "hash_name", "password", "salt", "iterations", "dklen" }, 0, 4, 5);
                string hash_name = PyArgs.ClinicStr("pbkdf2_hmac", "argument 'hash_name'", "str", parsed[0]);
                byte[] password = BytesMethods.RequireBuffer(parsed[1]);
                byte[] salt = BytesMethods.RequireBuffer(parsed[2]);
                long iterations = PyArgs.CLong(parsed[3]);
                return pbkdf2_hmac_impl(hash_name, password, salt, iterations, parsed[4] ?? PyNone.Instance);
            });
            d["scrypt"] = new PyBuiltinFunction("scrypt", (args, kwargs) =>
            {
                // (password: Py_buffer, *, salt: Py_buffer = None, n, r, p: int = None, maxmem: long = 0, dklen: long = 64)
                var parsed = PyArgs.Unpack("scrypt", args, kwargs, new[] { "password", "salt", "n", "r", "p", "maxmem", "dklen" }, 0, 1, 1);
                byte[] password = BytesMethods.RequireBuffer(parsed[0]);
                byte[] salt = parsed[1] != null ? BytesMethods.RequireBuffer(parsed[1]) : null;
                // object(subclass_of='&PyLong_Type'): PyLong_Check
                PyObject IntArgument(PyObject value, string name) =>
                    value == null ? PyNone.Instance
                    : PyInt.LongValue(value) != null ? value
                    : throw PyArgs.BadArgument("scrypt", $"argument '{name}'", "int", value);
                var n_obj = IntArgument(parsed[2], "n");
                var r_obj = IntArgument(parsed[3], "r");
                var p_obj = IntArgument(parsed[4], "p");
                long maxmem = parsed[5] != null ? PyArgs.CLong(parsed[5]) : 0;
                long dklen = parsed[6] != null ? PyArgs.CLong(parsed[6]) : 64;
                return _hashlib_scrypt_impl(password, salt, n_obj, r_obj, p_obj, maxmem, dklen);
            });
            d["get_fips_mode"] = PyArgs.NoArgsFunction("_hashlib", "get_fips_mode", () => PyInt.From(_hashlib_get_fips_mode_impl()));
            d["compare_digest"] = PyArgs.FastCall("_hashlib", "compare_digest", 2, 2, args => _hashlib_compare_digest_impl(args[0], args[1]));
            d["hmac_digest"] = new PyBuiltinFunction("hmac_digest", (args, kwargs) =>
            {
                // _hashlib.hmac_digest as _hashlib_hmac_singleshot: (key: Py_buffer, msg: Py_buffer, digest)
                var parsed = PyArgs.Unpack("hmac_digest", args, kwargs, new[] { "key", "msg", "digest" }, 0, 3, 3);
                byte[] key = BytesMethods.RequireBuffer(parsed[0]);
                byte[] msg = BytesMethods.RequireBuffer(parsed[1]);
                return _hashlib_hmac_singleshot_impl(key, msg, parsed[2]);
            });
            d["hmac_new"] = new PyBuiltinFunction("hmac_new", (args, kwargs) =>
            {
                // (key: Py_buffer, msg=b'', digestmod=None), both defaults NULL in C
                var parsed = PyArgs.Unpack("hmac_new", args, kwargs, new[] { "key", "msg", "digestmod" }, 0, 1, 3);
                byte[] key = BytesMethods.RequireBuffer(parsed[0]);
                return _hashlib_hmac_new_impl(key, parsed[1], parsed[2]);
            });
            foreach (var (fname, py_name) in new[]
            {
                ("openssl_md5", "md5"), ("openssl_sha1", "sha1"), ("openssl_sha224", "sha224"),
                ("openssl_sha256", "sha256"), ("openssl_sha384", "sha384"), ("openssl_sha512", "sha512"),
                ("openssl_sha3_224", "sha3_224"), ("openssl_sha3_256", "sha3_256"), ("openssl_sha3_384", "sha3_384"),
                ("openssl_sha3_512", "sha3_512"), ("openssl_shake_128", "shake_128"), ("openssl_shake_256", "shake_256"),
            })
                d[fname] = openssl_constructor(fname, py_name);

            hashlib_md_meth_names(module);
            hashlib_init_constructors(module);
            hashlib_exception(module);
            return module;
        }

        /// <summary>
        /// hashlib_init_constructors: {_hashlib.openssl_sha256: "sha256", ...} from the module's
        /// openssl_* functions, kept in the state and exposed read-only as _constructors.
        /// </summary>
        private static void hashlib_init_constructors(PyModule module)
        {
            constructs = new PyDict();
            foreach (var (name, func) in module.ModuleDict)
            {
                if (!name.StartsWith("openssl_", StringComparison.Ordinal) || func is not PyBuiltinFunction)
                    continue;
                constructs.SetItem(func, PyString.From(name.Substring(8)));
            }
            module.ModuleDict["_constructors"] = new PyMappingProxy((PyObject)constructs);
        }

        /// <summary>hashlib_exception: _hashlib.UnsupportedDigestmodError, a ValueError.</summary>
        private static void hashlib_exception(PyModule module) =>
            module.ModuleDict["UnsupportedDigestmodError"] = unsupported_digestmod_error;
    }
}
