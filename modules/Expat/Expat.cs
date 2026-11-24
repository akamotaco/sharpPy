using System;
using System.Runtime.CompilerServices;

namespace SharpPy.Modules.Expat
{
    /// <summary>
    /// const XML_Char * (XML_Char is char, UTF-8): a position in a byte array. Strings are
    /// NUL-terminated as in C; equality is pointer equality (same array, same offset).
    /// </summary>
    internal readonly struct XCHARP : IEquatable<XCHARP>
    {
        internal readonly byte[] a;
        internal readonly int i;

        internal XCHARP(byte[] a, int i)
        {
            this.a = a;
            this.i = i;
        }

        internal static readonly XCHARP NULL = default;

        internal bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => a == null;
        }

        internal byte this[int k]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => a[i + k];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Set(int k, byte v) => a[i + k] = v;

        public static XCHARP operator +(XCHARP p, int k) => new XCHARP(p.a, p.i + k);
        public static XCHARP operator -(XCHARP p, int k) => new XCHARP(p.a, p.i - k);
        public static int operator -(XCHARP p, XCHARP q) => p.i - q.i;
        public static bool operator ==(XCHARP p, XCHARP q) => ReferenceEquals(p.a, q.a) && p.i == q.i;
        public static bool operator !=(XCHARP p, XCHARP q) => !(p == q);
        public bool Equals(XCHARP other) => this == other;
        public override bool Equals(object obj) => obj is XCHARP o && this == o;
        public override int GetHashCode() => (a == null ? 0 : RuntimeHelpers.GetHashCode(a)) ^ i;

        /// <summary>xcslen / strlen</summary>
        internal int Length
        {
            get
            {
                int n = Array.IndexOf(a, (byte)0, i);
                return (n < 0 ? a.Length : n) - i;
            }
        }

        /// <summary>A NUL-terminated copy of an ASCII literal.</summary>
        internal static XCHARP Literal(string s)
        {
            var r = new byte[s.Length + 1];
            for (int k = 0; k < s.Length; k++)
                r[k] = (byte)s[k];
            return new XCHARP(r, 0);
        }

        /// <summary>A NUL-terminated UTF-8 copy of a C# string (PyArg "s" conversions).</summary>
        internal static XCHARP FromString(string s)
        {
            if (s == null)
                return NULL;
            var bytes = System.Text.Encoding.UTF8.GetBytes(s);
            var r = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, r, 0, bytes.Length);
            return new XCHARP(r, 0);
        }

        /// <summary>The bytes up to the NUL.</summary>
        internal byte[] ToArray()
        {
            int n = Length;
            var r = new byte[n];
            Buffer.BlockCopy(a, i, r, 0, n);
            return r;
        }
    }

    internal enum XML_Status
    {
        XML_STATUS_ERROR = 0,
        XML_STATUS_OK = 1,
        XML_STATUS_SUSPENDED = 2
    }

    internal enum XML_Error
    {
        XML_ERROR_NONE,
        XML_ERROR_NO_MEMORY,
        XML_ERROR_SYNTAX,
        XML_ERROR_NO_ELEMENTS,
        XML_ERROR_INVALID_TOKEN,
        XML_ERROR_UNCLOSED_TOKEN,
        XML_ERROR_PARTIAL_CHAR,
        XML_ERROR_TAG_MISMATCH,
        XML_ERROR_DUPLICATE_ATTRIBUTE,
        XML_ERROR_JUNK_AFTER_DOC_ELEMENT,
        XML_ERROR_PARAM_ENTITY_REF,
        XML_ERROR_UNDEFINED_ENTITY,
        XML_ERROR_RECURSIVE_ENTITY_REF,
        XML_ERROR_ASYNC_ENTITY,
        XML_ERROR_BAD_CHAR_REF,
        XML_ERROR_BINARY_ENTITY_REF,
        XML_ERROR_ATTRIBUTE_EXTERNAL_ENTITY_REF,
        XML_ERROR_MISPLACED_XML_PI,
        XML_ERROR_UNKNOWN_ENCODING,
        XML_ERROR_INCORRECT_ENCODING,
        XML_ERROR_UNCLOSED_CDATA_SECTION,
        XML_ERROR_EXTERNAL_ENTITY_HANDLING,
        XML_ERROR_NOT_STANDALONE,
        XML_ERROR_UNEXPECTED_STATE,
        XML_ERROR_ENTITY_DECLARED_IN_PE,
        XML_ERROR_FEATURE_REQUIRES_XML_DTD,
        XML_ERROR_CANT_CHANGE_FEATURE_ONCE_PARSING,
        /* Added in 1.95.7. */
        XML_ERROR_UNBOUND_PREFIX,
        /* Added in 1.95.8. */
        XML_ERROR_UNDECLARING_PREFIX,
        XML_ERROR_INCOMPLETE_PE,
        XML_ERROR_XML_DECL,
        XML_ERROR_TEXT_DECL,
        XML_ERROR_PUBLICID,
        XML_ERROR_SUSPENDED,
        XML_ERROR_NOT_SUSPENDED,
        XML_ERROR_ABORTED,
        XML_ERROR_FINISHED,
        XML_ERROR_SUSPEND_PE,
        /* Added in 2.0. */
        XML_ERROR_RESERVED_PREFIX_XML,
        XML_ERROR_RESERVED_PREFIX_XMLNS,
        XML_ERROR_RESERVED_NAMESPACE_URI,
        /* Added in 2.2.1. */
        XML_ERROR_INVALID_ARGUMENT,
        /* Added in 2.3.0. */
        XML_ERROR_NO_BUFFER,
        /* Added in 2.4.0. */
        XML_ERROR_AMPLIFICATION_LIMIT_BREACH,
        /* Added in 2.6.4. */
        XML_ERROR_NOT_STARTED,
    }

    internal enum XML_Content_Type
    {
        XML_CTYPE_EMPTY = 1,
        XML_CTYPE_ANY,
        XML_CTYPE_MIXED,
        XML_CTYPE_NAME,
        XML_CTYPE_CHOICE,
        XML_CTYPE_SEQ
    }

    internal enum XML_Content_Quant
    {
        XML_CQUANT_NONE,
        XML_CQUANT_OPT,
        XML_CQUANT_REP,
        XML_CQUANT_PLUS
    }

    /// <summary>XML_Content: a node of an element's content model.</summary>
    internal sealed class XML_Content
    {
        internal XML_Content_Type type;
        internal XML_Content_Quant quant;
        internal XCHARP name;
        internal uint numchildren;
        internal XML_Content[] children;
    }

    internal enum XML_Parsing
    {
        XML_INITIALIZED,
        XML_PARSING,
        XML_FINISHED,
        XML_SUSPENDED
    }

    internal struct XML_ParsingStatus
    {
        internal XML_Parsing parsing;
        internal bool finalBuffer;
    }

    internal enum XML_ParamEntityParsing
    {
        XML_PARAM_ENTITY_PARSING_NEVER,
        XML_PARAM_ENTITY_PARSING_UNLESS_STANDALONE,
        XML_PARAM_ENTITY_PARSING_ALWAYS
    }

    /// <summary>XML_Encoding: what an unknown encoding handler fills in.</summary>
    internal sealed class XML_Encoding
    {
        internal readonly int[] map = new int[256];
        internal object data;
        internal CONVERTER convert;
        internal Action<object> release;
    }

    // expat.h handler types; userData is the void * the parser was given (XML_SetUserData).
    internal delegate void XML_StartElementHandler(object userData, XCHARP name, XCHARP[] atts);
    internal delegate void XML_EndElementHandler(object userData, XCHARP name);
    internal delegate void XML_CharacterDataHandler(object userData, XCHARP s, int len);
    internal delegate void XML_ProcessingInstructionHandler(object userData, XCHARP target, XCHARP data);
    internal delegate void XML_CommentHandler(object userData, XCHARP data);
    internal delegate void XML_StartCdataSectionHandler(object userData);
    internal delegate void XML_EndCdataSectionHandler(object userData);
    internal delegate void XML_DefaultHandler(object userData, XCHARP s, int len);
    internal delegate void XML_StartDoctypeDeclHandler(object userData, XCHARP doctypeName, XCHARP sysid, XCHARP pubid, int has_internal_subset);
    internal delegate void XML_EndDoctypeDeclHandler(object userData);
    internal delegate void XML_ElementDeclHandler(object userData, XCHARP name, XML_Content model);
    internal delegate void XML_AttlistDeclHandler(object userData, XCHARP elname, XCHARP attname, XCHARP att_type, XCHARP dflt, int isrequired);
    internal delegate void XML_XmlDeclHandler(object userData, XCHARP version, XCHARP encoding, int standalone);
    internal delegate void XML_EntityDeclHandler(object userData, XCHARP entityName, int is_parameter_entity, XCHARP value, int value_length, XCHARP @base, XCHARP systemId, XCHARP publicId, XCHARP notationName);
    internal delegate void XML_UnparsedEntityDeclHandler(object userData, XCHARP entityName, XCHARP @base, XCHARP systemId, XCHARP publicId, XCHARP notationName);
    internal delegate void XML_NotationDeclHandler(object userData, XCHARP notationName, XCHARP @base, XCHARP systemId, XCHARP publicId);
    internal delegate void XML_StartNamespaceDeclHandler(object userData, XCHARP prefix, XCHARP uri);
    internal delegate void XML_EndNamespaceDeclHandler(object userData, XCHARP prefix);
    internal delegate int XML_NotStandaloneHandler(object userData);
    internal delegate int XML_ExternalEntityRefHandler(XML_ParserStruct parser, XCHARP context, XCHARP @base, XCHARP systemId, XCHARP publicId);
    internal delegate void XML_SkippedEntityHandler(object userData, XCHARP entityName, int is_parameter_entity);
    internal delegate int XML_UnknownEncodingHandler(object encodingHandlerData, XCHARP name, XML_Encoding info);

    internal struct XML_Feature
    {
        internal string name;
        internal long value;
    }

    /// <summary>siphash.h (SipHash-2-4), the hash expat uses for its tables.</summary>
    internal struct sipkey
    {
        internal ulong k0, k1;
    }

    internal sealed class siphash
    {
        private ulong v0, v1, v2, v3;
        private readonly byte[] buf = new byte[8];
        private int p;
        private ulong c;

        private static ulong ROTL(ulong x, int b) => (x << b) | (x >> (64 - b));

        private void sipround()
        {
            v0 += v1;
            v1 = ROTL(v1, 13);
            v1 ^= v0;
            v0 = ROTL(v0, 32);
            v2 += v3;
            v3 = ROTL(v3, 16);
            v3 ^= v2;
            v0 += v3;
            v3 = ROTL(v3, 21);
            v3 ^= v0;
            v2 += v1;
            v1 = ROTL(v1, 17);
            v1 ^= v2;
            v2 = ROTL(v2, 32);
        }

        // sip24_init
        internal siphash(sipkey key)
        {
            v0 = 0x736f6d6570736575UL ^ key.k0;
            v1 = 0x646f72616e646f6dUL ^ key.k1;
            v2 = 0x6c7967656e657261UL ^ key.k0;
            v3 = 0x7465646279746573UL ^ key.k1;
            p = 0;
            c = 0;
        }

        // sip24_update
        internal void update(byte[] src, int off, int len)
        {
            int pe = off + len;
            ulong m;
            do
            {
                while (off < pe && p < 8)
                    buf[p++] = src[off++];
                if (p < 8)
                    break;
                m = BitConverter.ToUInt64(buf, 0);
                v3 ^= m;
                sipround();
                sipround();
                v0 ^= m;
                p = 0;
                c += 8;
            } while (off < pe);
        }

        // sip24_final
        internal ulong final()
        {
            int left = p;
            ulong b = (c + (ulong)left) << 56;
            switch (left)
            {
                case 7: b |= (ulong)buf[6] << 48; goto case 6;
                case 6: b |= (ulong)buf[5] << 40; goto case 5;
                case 5: b |= (ulong)buf[4] << 32; goto case 4;
                case 4: b |= (ulong)buf[3] << 24; goto case 3;
                case 3: b |= (ulong)buf[2] << 16; goto case 2;
                case 2: b |= (ulong)buf[1] << 8; goto case 1;
                case 1: b |= (ulong)buf[0] << 0; break;
                case 0: break;
            }
            v3 ^= b;
            sipround();
            sipround();
            v0 ^= b;
            v2 ^= 0xff;
            sipround();
            sipround();
            sipround();
            sipround();
            return v0 ^ v1 ^ v2 ^ v3;
        }

        // sip_tokey
        internal static sipkey sip_tokey(byte[] src)
        {
            return new sipkey { k0 = BitConverter.ToUInt64(src, 0), k1 = BitConverter.ToUInt64(src, 8) };
        }
    }
}
