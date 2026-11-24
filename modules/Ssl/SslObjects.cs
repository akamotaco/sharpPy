using System;
using System.Collections.Generic;

namespace SharpPy.Modules.Ssl
{
    /// <summary>
    /// crypto/objects/obj_dat.c over the generated table (SslData.Objects): OBJ_nid2sn, OBJ_nid2ln,
    /// OBJ_obj2nid, OBJ_sn2nid, OBJ_ln2nid, OBJ_txt2obj and OBJ_obj2txt.
    /// </summary>
    internal static class SslObjects
    {
        internal const int NID_undef = 0;

        internal sealed class Obj
        {
            public int Nid;
            public string Sn, Ln, Oid;
        }

        private static readonly Dictionary<int, Obj> ByNid = new();
        private static readonly Dictionary<string, Obj> ByOid = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Obj> BySn = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, Obj> ByLn = new(StringComparer.Ordinal);

        static SslObjects()
        {
            foreach (var (nid, sn, ln, oid) in SslData.Objects)
            {
                var obj = new Obj { Nid = nid, Sn = sn, Ln = ln, Oid = oid };
                ByNid[nid] = obj;
                if (oid != null)
                    ByOid.TryAdd(oid, obj);
                if (sn != null)
                    BySn.TryAdd(sn, obj);
                if (ln != null)
                    ByLn.TryAdd(ln, obj);
            }
        }

        /// <summary>OBJ_nid2obj: null for an unknown NID.</summary>
        internal static Obj Nid2Obj(int nid) => ByNid.TryGetValue(nid, out var o) ? o : null;

        /// <summary>OBJ_obj2nid of a dotted OID.</summary>
        internal static int Oid2Nid(string oid) => oid != null && ByOid.TryGetValue(oid, out var o) ? o.Nid : NID_undef;

        internal static Obj ByOidText(string oid) => oid != null && ByOid.TryGetValue(oid, out var o) ? o : null;

        /// <summary>OBJ_sn2nid</summary>
        internal static int Sn2Nid(string sn) => BySn.TryGetValue(sn, out var o) ? o.Nid : NID_undef;

        /// <summary>OBJ_nid2ln</summary>
        internal static string Nid2Ln(int nid) => ByNid.TryGetValue(nid, out var o) ? o.Ln : null;

        /// <summary>OBJ_nid2sn</summary>
        internal static string Nid2Sn(int nid) => ByNid.TryGetValue(nid, out var o) ? o.Sn : null;

        /// <summary>
        /// OBJ_txt2obj: with no_name 0 a short or long name first, then the dotted form. The result is
        /// the known object, a fresh one with NID_undef for a valid but unknown OID, or null.
        /// </summary>
        internal static Obj Txt2Obj(string text, bool noName)
        {
            if (!noName)
            {
                if (BySn.TryGetValue(text, out var o) || ByLn.TryGetValue(text, out o))
                    return o;
            }
            var encoded = Asn1.OidEncode(text);
            if (encoded == null)
                return null;
            string canonical = Asn1.OidText(encoded);
            return ByOidText(canonical) ?? new Obj { Nid = NID_undef, Oid = canonical };
        }

        /// <summary>OBJ_obj2txt: the long name (else the short name) unless no_name, else the dotted OID.</summary>
        internal static string Obj2Txt(string oid, bool noName)
        {
            if (!noName && ByOidText(oid) is { } o)
                return o.Ln ?? o.Sn;
            return oid;
        }
    }
}
