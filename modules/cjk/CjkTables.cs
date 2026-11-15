using System;
using System.Collections.Generic;

namespace SharpPy.Modules.Cjk
{
    /// <summary>
    /// cjkcodecs.h's index over a mapping table (dbcs_index, unim_index, widedbcs_index): per lead
    /// value, where its row starts in the array and the range of trail values the row has.
    /// </summary>
    internal sealed class CjkIndex<T> where T : struct
    {
        private readonly T[] _map;
        private readonly int[] _offset;
        private readonly byte[] _bottom, _top;

        internal CjkIndex(T[] map, int[] offset, byte[] bottom, byte[] top)
        {
            _map = map;
            _offset = offset;
            _bottom = bottom;
            _top = top;
        }

        // _TRYMAP_ENC and _TRYMAP_DEC without the comparison with NOCHAR or UNIINV, which the callers make.
        internal bool TryGet(int lead, int trail, out T value)
        {
            if (lead < 0 || lead > 255 || _offset[lead] < 0 || trail < _bottom[lead] || trail > _top[lead])
            {
                value = default;
                return false;
            }
            value = _map[_offset[lead] + trail - _bottom[lead]];
            return true;
        }
    }

    /// <summary>cjkcodecs.h's TRYMAP_ENC and TRYMAP_DEC.</summary>
    internal static class Trymap
    {
        internal static bool TryEnc(CjkIndex<ushort> map, int uni, out ushort code) =>
            map.TryGet(uni >> 8, uni & 0xff, out code) && code != CjkTables.NOCHAR;

        internal static bool TryDec(CjkIndex<ushort> map, int c1, int c2, out ushort decoded) =>
            map.TryGet(c1, c2, out decoded) && decoded != CjkTables.UNIINV;

        // TRYMAP_DEC over a widedbcs_index
        internal static bool TryDecWide(CjkIndex<uint> map, int c1, int c2, out uint decoded) =>
            map.TryGet(c1, c2, out decoded) && decoded != CjkTables.UNIINV;

        // find_pairencmap (USING_BINARY_PAIR_SEARCH)
        internal static ushort FindPairEncMap(int body, int modifier, CjkPair[] haystack)
        {
            int pos, min = 0, max = haystack.Length;
            uint value = ((uint)(ushort)body << 16) | (ushort)modifier;
            for (pos = haystack.Length >> 1; min != max; pos = (min + max) >> 1)
            {
                if (value < haystack[pos].Uniseq)
                {
                    if (max != pos)
                    {
                        max = pos;
                        continue;
                    }
                }
                else if (value > haystack[pos].Uniseq)
                {
                    if (min != pos)
                    {
                        min = pos;
                        continue;
                    }
                }
                break;
            }
            return value == haystack[pos].Uniseq ? haystack[pos].Code : CjkTables.DBCINV;
        }
    }

    /// <summary>cjkcodecs.h's pair_encodemap: a code point pair (base &lt;&lt; 16 | combining) and its code.</summary>
    internal readonly record struct CjkPair(uint Uniseq, ushort Code);

    /// <summary>mappings_cn.h's _gb18030_to_unibmp_ranges.</summary>
    internal readonly record struct Gb18030Range(uint First, uint Last, ushort Base);

    /// <summary>
    /// The tables of one mappings_*.h, read from the blob tools/gen_cjk_mappings.py makes, each by
    /// its name in the header; decoded with zlib's inflate on first use.
    /// </summary>
    internal sealed class CjkTables
    {
        internal const ushort UNIINV = 0xFFFE, NOCHAR = 0xFFFF, MULTIC = 0xFFFE, DBCINV = 0xFFFD;

        private readonly Dictionary<string, object> _tables = new();

        private static readonly Dictionary<string, CjkTables> Loaded = new();

        internal static CjkTables Of(string locale)
        {
            lock (Loaded)
            {
                if (!Loaded.TryGetValue(locale, out var tables))
                    Loaded[locale] = tables = new CjkTables(locale switch
                    {
                        "kr" => CjkMappings.Kr,
                        "jp" => CjkMappings.Jp,
                        "cn" => CjkMappings.Cn,
                        "tw" => CjkMappings.Tw,
                        "hk" => CjkMappings.Hk,
                        _ => throw new ArgumentException(locale),
                    });
                return tables;
            }
        }

        private CjkTables(string blob)
        {
            var data = ZlibModule.Inflate(Convert.FromBase64String(blob));
            int at = 0;
            int ReadInt() { int v = BitConverter.ToInt32(data, at); at += 4; return v; }
            string ReadName() { int n = data[at++]; var s = System.Text.Encoding.ASCII.GetString(data, at, n); at += n; return s; }
            int count = ReadInt();
            var indexes = new List<(string Name, string Array, int[] Off, byte[] Bottom, byte[] Top)>();
            for (int t = 0; t < count; t++)
            {
                string name = ReadName();
                byte kind = data[at++];
                switch (kind)
                {
                    case 1:
                    {
                        var values = new ushort[ReadInt()];
                        for (int i = 0; i < values.Length; i++, at += 2) values[i] = BitConverter.ToUInt16(data, at);
                        _tables[name] = values;
                        break;
                    }
                    case 2:
                    {
                        var values = new uint[ReadInt()];
                        for (int i = 0; i < values.Length; i++, at += 4) values[i] = BitConverter.ToUInt32(data, at);
                        _tables[name] = values;
                        break;
                    }
                    case 3:
                    {
                        var values = new byte[ReadInt()];
                        Array.Copy(data, at, values, 0, values.Length);
                        at += values.Length;
                        _tables[name] = values;
                        break;
                    }
                    case 4:
                    {
                        string array = ReadName();
                        var off = new int[256];
                        var bottom = new byte[256];
                        var top = new byte[256];
                        for (int i = 0; i < 256; i++)
                        {
                            off[i] = ReadInt();
                            bottom[i] = data[at++];
                            top[i] = data[at++];
                        }
                        indexes.Add((name, array, off, bottom, top));
                        break;
                    }
                    case 5:
                    {
                        var pairs = new CjkPair[ReadInt()];
                        for (int i = 0; i < pairs.Length; i++, at += 6)
                            pairs[i] = new CjkPair(BitConverter.ToUInt32(data, at), BitConverter.ToUInt16(data, at + 4));
                        _tables[name] = pairs;
                        break;
                    }
                    case 6:
                    {
                        var ranges = new Gb18030Range[ReadInt()];
                        for (int i = 0; i < ranges.Length; i++, at += 10)
                            ranges[i] = new Gb18030Range(BitConverter.ToUInt32(data, at), BitConverter.ToUInt32(data, at + 4), BitConverter.ToUInt16(data, at + 8));
                        _tables[name] = ranges;
                        break;
                    }
                    default:
                        throw new InvalidOperationException("bad CJK mapping blob");
                }
            }
            foreach (var (name, array, off, bottom, top) in indexes)
            {
                if (array.Length == 0)
                    _tables[name] = new CjkIndex<ushort>(Array.Empty<ushort>(), off, bottom, top);
                else if (_tables[array] is uint[] wide)
                    _tables[name] = new CjkIndex<uint>(wide, off, bottom, top);
                else
                    _tables[name] = new CjkIndex<ushort>((ushort[])_tables[array], off, bottom, top);
            }
        }

        internal CjkIndex<ushort> Map(string name) => (CjkIndex<ushort>)_tables[name];
        internal CjkIndex<uint> WideMap(string name) => (CjkIndex<uint>)_tables[name];
        internal T Get<T>(string name) => (T)_tables[name];
    }

    /// <summary>
    /// The maps a cjkcodecs module holds (cjkcodecs_module_state's mapping_list): each charset's
    /// encode and decode map, as __map_&lt;charset&gt; hands them to the modules that import them.
    /// </summary>
    internal sealed class DbcsMap
    {
        internal readonly string Charset;
        // a unim_index or pair_encodemap; a dbcs_index or widedbcs_index (the C pointers are void *)
        internal readonly object EncMap, DecMap;

        internal DbcsMap(string charset, object encmap, object decmap)
        {
            Charset = charset;
            EncMap = encmap;
            DecMap = decmap;
        }
    }
}
