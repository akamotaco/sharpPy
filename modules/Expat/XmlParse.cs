using System;
using static SharpPy.Modules.Expat.XmlTok;
using static SharpPy.Modules.Expat.XmlRole;
using static SharpPy.Modules.Expat.XML_Error;
using static SharpPy.Modules.Expat.XML_Parsing;
using XML_Parser = SharpPy.Modules.Expat.XML_ParserStruct;

namespace SharpPy.Modules.Expat
{
    // ------------------------------------------------------------------ xmlparse.c structures

    /// <summary>NAMED (hash_table.h): the head every hash table entry starts with.</summary>
    internal abstract class NAMED
    {
        internal XCHARP name;
        // sizeof the C structure (x64), for the allocation tracker
        internal abstract int CSize { get; }
    }

    /// <summary>HASH_TABLE</summary>
    internal sealed class HASH_TABLE
    {
        internal NAMED[] v;
        internal byte power;
        internal long size;
        internal long used;
        internal XML_Parser parser;
    }

    /// <summary>BINDING</summary>
    internal sealed class BINDING
    {
        internal PREFIX prefix;
        internal BINDING nextTagBinding;
        internal BINDING prevPrefixBinding;
        internal ATTRIBUTE_ID attId;
        internal byte[] uri;
        internal int uriLen;
        internal int uriAlloc;
        internal const int CSize = 56;
    }

    /// <summary>PREFIX</summary>
    internal sealed class PREFIX : NAMED
    {
        internal BINDING binding;
        internal override int CSize => 16;
    }

    /// <summary>TAG_NAME</summary>
    internal sealed class TAG_NAME
    {
        internal XCHARP str;
        internal XCHARP localPart;
        internal XCHARP prefix;
        internal int strLen;
        internal int uriLen;
        internal int prefixLen;
    }

    /// <summary>TAG: an open element; buf holds the converted name and, between parse calls, the raw name.</summary>
    internal sealed class TAG
    {
        internal TAG parent;
        internal byte[] rawNameBuf;   // the array rawName points into
        internal int rawName;
        internal int rawNameLength;
        internal readonly TAG_NAME name = new TAG_NAME();
        internal byte[] buf;          // buf.raw / buf.str
        internal int bufEnd;          // bufEnd - buf.raw
        internal BINDING bindings;
        internal const int CSize = 88;
    }

    /// <summary>ENTITY</summary>
    internal sealed class ENTITY : NAMED
    {
        internal XCHARP textPtr;
        internal int textLen;   /* length in XML_Chars */
        internal int processed; /* # of processed bytes - when suspended */
        internal XCHARP systemId;
        internal XCHARP @base;
        internal XCHARP publicId;
        internal XCHARP notation;
        internal bool open;
        internal bool hasMore;
        internal bool is_param;
        internal bool is_internal;
        internal override int CSize => 64;
    }

    /// <summary>CONTENT_SCAFFOLD</summary>
    internal struct CONTENT_SCAFFOLD
    {
        internal XML_Content_Type type;
        internal XML_Content_Quant quant;
        internal XCHARP name;
        internal int firstchild;
        internal int lastchild;
        internal int childcnt;
        internal int nextsib;
        internal const int CSize = 32;
    }

    /// <summary>BLOCK</summary>
    internal sealed class BLOCK
    {
        internal BLOCK next;
        internal int size;
        internal byte[] s;
    }

    /// <summary>STRING_POOL: start/ptr/end are offsets into cur, the first block's s (NULL when cur is null).</summary>
    internal sealed class STRING_POOL
    {
        internal BLOCK blocks;
        internal BLOCK freeBlocks;
        internal byte[] cur;
        internal int end;
        internal int ptr;
        internal int start;
        internal XML_Parser parser;
    }

    /// <summary>ATTRIBUTE_ID: the XML_Char before the name (name[-1]) marks the attribute as specified.</summary>
    internal sealed class ATTRIBUTE_ID : NAMED
    {
        internal PREFIX prefix;
        internal bool maybeTokenized;
        internal bool xmlns;
        internal override int CSize => 24;
    }

    /// <summary>DEFAULT_ATTRIBUTE</summary>
    internal struct DEFAULT_ATTRIBUTE
    {
        internal ATTRIBUTE_ID id;
        internal bool isCdata;
        internal XCHARP value;
        internal const int CSize = 24;
    }

    /// <summary>NAME_AND_DEFAULT_ATTRIBUTE</summary>
    internal sealed class NAME_AND_DEFAULT_ATTRIBUTE : NAMED
    {
        internal long attIndex;
        internal bool initialized;
        internal override int CSize => 24;
    }

    /// <summary>NS_ATT</summary>
    internal struct NS_ATT
    {
        internal ulong version;
        internal ulong hash;
        internal XCHARP uriName;
        internal const int CSize = 24;
    }

    /// <summary>ELEMENT_TYPE</summary>
    internal sealed class ELEMENT_TYPE : NAMED
    {
        internal PREFIX prefix;
        internal ATTRIBUTE_ID idAtt;
        internal long nDefaultAtts;
        internal long allocDefaultAtts;
        internal DEFAULT_ATTRIBUTE[] defaultAtts;
        internal readonly HASH_TABLE defaultAttForName = new HASH_TABLE();
        internal override int CSize => 88;
    }

    /// <summary>DTD</summary>
    internal sealed class DTD
    {
        internal readonly HASH_TABLE generalEntities = new HASH_TABLE();
        internal readonly HASH_TABLE elementTypes = new HASH_TABLE();
        internal readonly HASH_TABLE attributeIds = new HASH_TABLE();
        internal readonly HASH_TABLE prefixes = new HASH_TABLE();
        internal readonly STRING_POOL pool = new STRING_POOL();
        internal readonly STRING_POOL entityValuePool = new STRING_POOL();
        /* false once a parameter entity reference has been skipped */
        internal bool keepProcessing;
        /* true once an internal or external PE reference has been encountered;
           this includes the reference to an external subset */
        internal bool hasParamEntityRefs;
        internal bool standalone;
        /* indicates if external PE has been read */
        internal bool paramEntityRead;
        internal readonly HASH_TABLE paramEntities = new HASH_TABLE();
        internal readonly PREFIX defaultPrefix = new PREFIX();
        /* === scaffolding for building content model === */
        internal bool in_eldecl;
        internal CONTENT_SCAFFOLD[] scaffold;
        internal uint contentStringLen;
        internal uint scaffSize;
        internal uint scaffCount;
        internal int scaffLevel;
        internal int[] scaffIndex;
        internal long scaffIndexSize;
        internal const int CSize = 360;
    }

    internal enum EntityType
    {
        ENTITY_INTERNAL,
        ENTITY_ATTRIBUTE,
        ENTITY_VALUE,
    }

    /// <summary>OPEN_INTERNAL_ENTITY: the event pointers are offsets into entity.textPtr.a.</summary>
    internal sealed class OPEN_INTERNAL_ENTITY
    {
        internal int internalEventPtr = -1;
        internal int internalEventEndPtr = -1;
        internal OPEN_INTERNAL_ENTITY next;
        internal ENTITY entity;
        internal int startTagLevel;
        internal bool betweenDecl; /* WFC: PE Between Declarations */
        internal EntityType type;
        internal const int CSize = 48;
    }

    internal enum XML_Account
    {
        XML_ACCOUNT_DIRECT,           /* bytes directly passed to the Expat parser */
        XML_ACCOUNT_ENTITY_EXPANSION, /* intermediate bytes produced during entity expansion */
        XML_ACCOUNT_NONE              /* i.e. do not account, was accounted already */
    }

    /// <summary>ACCOUNTING</summary>
    internal struct ACCOUNTING
    {
        internal ulong countBytesDirect;
        internal ulong countBytesIndirect;
        internal ulong debugLevel;
        internal float maximumAmplificationFactor; // >=1.0
        internal ulong activationThresholdBytes;
    }

    /// <summary>MALLOC_TRACKER</summary>
    internal struct MALLOC_TRACKER
    {
        internal ulong bytesAllocated;
        internal ulong peakBytesAllocated;
        internal ulong debugLevel;
        internal float maximumAmplificationFactor; // >=1.0
        internal ulong activationThresholdBytes;
    }

    /// <summary>ENTITY_STATS</summary>
    internal struct ENTITY_STATS
    {
        internal uint countEverOpened;
        internal uint currentDepth;
        internal uint maximumDepthSeen;
        internal ulong debugLevel;
    }

    internal delegate XML_Error Processor(XML_Parser parser, int start, int end, ref int endPtr);

    /// <summary>
    /// struct XML_ParserStruct. Pointers into the parse buffer (m_buffer) are offsets, -1 for NULL;
    /// m_bufferPtr/m_bufferEnd/m_bufferLim are 0 while there is no buffer, which keeps
    /// EXPAT_SAFE_PTR_DIFF's results.
    /// </summary>
    internal sealed class XML_ParserStruct
    {
        internal object m_userData;
        internal object m_handlerArg;
        internal byte[] m_buffer;
        internal int m_bufferPtr;
        internal int m_bufferEnd;
        internal int m_bufferLim;
        internal ulong m_parseEndByteIndex;
        internal int m_parseEndPtr = -1;
        internal long m_partialTokenBytesBefore;
        internal bool m_reparseDeferralEnabled;
        internal int m_lastBufferRequestSize;
        internal byte[] m_dataBuf;
        internal XML_StartElementHandler m_startElementHandler;
        internal XML_EndElementHandler m_endElementHandler;
        internal XML_CharacterDataHandler m_characterDataHandler;
        internal XML_ProcessingInstructionHandler m_processingInstructionHandler;
        internal XML_CommentHandler m_commentHandler;
        internal XML_StartCdataSectionHandler m_startCdataSectionHandler;
        internal XML_EndCdataSectionHandler m_endCdataSectionHandler;
        internal XML_DefaultHandler m_defaultHandler;
        internal XML_StartDoctypeDeclHandler m_startDoctypeDeclHandler;
        internal XML_EndDoctypeDeclHandler m_endDoctypeDeclHandler;
        internal XML_UnparsedEntityDeclHandler m_unparsedEntityDeclHandler;
        internal XML_NotationDeclHandler m_notationDeclHandler;
        internal XML_StartNamespaceDeclHandler m_startNamespaceDeclHandler;
        internal XML_EndNamespaceDeclHandler m_endNamespaceDeclHandler;
        internal XML_NotStandaloneHandler m_notStandaloneHandler;
        internal XML_ExternalEntityRefHandler m_externalEntityRefHandler;
        internal XML_Parser m_externalEntityRefHandlerArg;
        internal XML_SkippedEntityHandler m_skippedEntityHandler;
        internal XML_UnknownEncodingHandler m_unknownEncodingHandler;
        internal XML_ElementDeclHandler m_elementDeclHandler;
        internal XML_AttlistDeclHandler m_attlistDeclHandler;
        internal XML_EntityDeclHandler m_entityDeclHandler;
        internal XML_XmlDeclHandler m_xmlDeclHandler;
        internal ENCODING m_encoding;
        internal readonly INIT_ENCODING m_initEncoding = new INIT_ENCODING();
        internal ENCODING m_internalEncoding;
        internal XCHARP m_protocolEncodingName;
        internal bool m_ns;
        internal bool m_ns_triplets;
        internal bool m_unknownEncodingMem;
        internal object m_unknownEncodingData;
        internal object m_unknownEncodingHandlerData;
        internal CONVERTER m_unknownEncodingConvert;
        internal Action<object> m_unknownEncodingRelease;
        internal readonly PROLOG_STATE m_prologState = new PROLOG_STATE();
        internal Processor m_processor;
        internal XML_Error m_errorCode;
        internal int m_eventPtr = -1;
        internal int m_eventEndPtr = -1;
        internal int m_positionPtr = -1;
        internal OPEN_INTERNAL_ENTITY m_openInternalEntities;
        internal OPEN_INTERNAL_ENTITY m_openAttributeEntities;
        internal OPEN_INTERNAL_ENTITY m_openValueEntities;
        internal OPEN_INTERNAL_ENTITY m_freeEntities;
        internal bool m_defaultExpandInternalEntities;
        internal int m_tagLevel;
        internal ENTITY m_declEntity;
        internal XCHARP m_doctypeName;
        internal XCHARP m_doctypeSysid;
        internal XCHARP m_doctypePubid;
        internal XCHARP m_declAttributeType;
        internal XCHARP m_declNotationName;
        internal XCHARP m_declNotationPublicId;
        internal ELEMENT_TYPE m_declElementType;
        internal ATTRIBUTE_ID m_declAttributeId;
        internal bool m_declAttributeIsCdata;
        internal bool m_declAttributeIsId;
        internal DTD m_dtd;
        internal XCHARP m_curBase;
        internal TAG m_tagStack;
        internal TAG m_freeTagList;
        internal BINDING m_inheritedBindings;
        internal BINDING m_freeBindingList;
        internal int m_attsSize;
        internal int m_nSpecifiedAtts;
        internal int m_idAttIndex;
        internal ATTRIBUTE[] m_atts;
        internal XCHARP[] m_appAtts;   // appAtts: C reuses m_atts' memory as the XML_Char * array
        internal NS_ATT[] m_nsAtts;
        internal ulong m_nsAttsVersion;
        internal byte m_nsAttsPower;
        internal POSITION m_position;
        internal readonly STRING_POOL m_tempPool = new STRING_POOL();
        internal readonly STRING_POOL m_temp2Pool = new STRING_POOL();
        internal byte[] m_groupConnector;
        internal long m_groupSize;
        internal byte m_namespaceSeparator;
        internal XML_Parser m_parentParser;
        internal XML_ParsingStatus m_parsingStatus;
        internal bool m_isParamEntity;
        internal bool m_useForeignDTD;
        internal XML_ParamEntityParsing m_paramEntityParsing;
        internal sipkey m_hash_secret_salt_128;
        internal bool m_hash_secret_salt_set;
        internal ACCOUNTING m_accounting;
        internal MALLOC_TRACKER m_alloc_tracker;
        internal ENTITY_STATS m_entity_stats;
        internal bool m_reenter;
        internal uint m_handlerCallDepth;
        // bytes this parser's allocations added to its root parser's tracker, given back by XML_ParserFree
        internal ulong m_trackedHere;

        internal const int CSize = 1176;
    }

    /// <summary>
    /// Modules/expat/xmlparse.c (expat 2.8.5) as CPython builds it (XML_NS, XML_DTD, XML_GE == 1,
    /// XML_CONTEXT_BYTES 1024, XML_Char = char): one static method per C function, the parser
    /// first. MALLOC/REALLOC/FREE are the allocation tracker's bookkeeping (expat_malloc etc.)
    /// around the managed allocation the C call would make.
    /// </summary>
    internal static partial class XmlParse
    {
        internal const int XML_MAJOR_VERSION = 2, XML_MINOR_VERSION = 8, XML_MICRO_VERSION = 5;
        private const int XML_CONTEXT_BYTES = 1024;

        private const int INIT_TAG_BUF_SIZE = 32; /* must be a multiple of sizeof(XML_Char) */
        private const int INIT_DATA_BUF_SIZE = 1024;
        private const int INIT_ATTS_SIZE = 16;
        private const ulong INIT_ATTS_VERSION = 0xFFFFFFFF;
        private const int INIT_BLOCK_SIZE = 1024;
        private const int INIT_BUFFER_SIZE = 1024;
        private const int EXPAND_SPARE = 24;
        private const int INIT_SCAFFOLD_ELEMENTS = 32;
        private const int INIT_POWER = 6;
        private const int XML_ENCODE_MAX = XML_UTF8_ENCODE_MAX;

        private const float EXPAT_BILLION_LAUGHS_ATTACK_PROTECTION_MAXIMUM_AMPLIFICATION_DEFAULT = 100.0f;
        private const ulong EXPAT_BILLION_LAUGHS_ATTACK_PROTECTION_ACTIVATION_THRESHOLD_DEFAULT = 8388608; // 8 MiB, 2^23
        private const float EXPAT_ALLOC_TRACKER_MAXIMUM_AMPLIFICATION_DEFAULT = 100.0f;
        private const ulong EXPAT_ALLOC_TRACKER_ACTIVATION_THRESHOLD_DEFAULT = 67108864; // 64 MiB, 2^26
        private const int EXPAT_MALLOC_PADDING = 0; // EXPAT_MALLOC_ALIGNMENT - sizeof(size_t) on x64
        private const int SIZEOF_SIZE_T = 8;
        private const int SIZEOF_POINTER = 8;
        private const int SIZEOF_ATTRIBUTE = 32;
        private const int BLOCK_HEADER = 16; // offsetof(BLOCK, s)

        internal static readonly bool g_reparseDeferralEnabledDefault = true;

        // MUST_CONVERT(enc, s)
        private static bool MUST_CONVERT(ENCODING enc) => !enc.isUtf8;

        // XmlConvert = XmlUtf8Convert, XmlEncode = XmlUtf8Encode
        private static XML_Convert_Result XmlConvert(ENCODING enc, byte[] from, ref int fromP, int fromLim, byte[] to, ref int toP, int toLim) =>
            enc.Utf8Convert(from, ref fromP, fromLim, to, ref toP, toLim);

        // ------------------------------------------------------------------ allocation tracker

        /// <summary>expat_heap_increase_tolerable</summary>
        private static bool expat_heap_increase_tolerable(XML_Parser rootParser, ulong increase)
        {
            ulong newTotal;
            bool tolerable = true;
            if (ulong.MaxValue - rootParser.m_alloc_tracker.bytesAllocated < increase)
                tolerable = false;
            else
            {
                newTotal = rootParser.m_alloc_tracker.bytesAllocated + increase;
                if (newTotal >= rootParser.m_alloc_tracker.activationThresholdBytes)
                {
                    float amplification = (float)newTotal / (float)rootParser.m_accounting.countBytesDirect;
                    if (amplification > rootParser.m_alloc_tracker.maximumAmplificationFactor)
                        tolerable = false;
                }
            }
            return tolerable;
        }

        /// <summary>expat_malloc: the bookkeeping of MALLOC(parser, size); false where C returns NULL.</summary>
        internal static bool MALLOC(XML_Parser parser, long size)
        {
            XML_Parser rootParser = getRootParserOf(parser, out _);
            ulong bytesToAllocate = (ulong)(SIZEOF_SIZE_T + EXPAT_MALLOC_PADDING + size);
            if (ulong.MaxValue - rootParser.m_alloc_tracker.bytesAllocated < bytesToAllocate)
                return false;
            if (!expat_heap_increase_tolerable(rootParser, bytesToAllocate))
                return false;
            rootParser.m_alloc_tracker.bytesAllocated += bytesToAllocate;
            parser.m_trackedHere += bytesToAllocate;
            if (rootParser.m_alloc_tracker.bytesAllocated > rootParser.m_alloc_tracker.peakBytesAllocated)
                rootParser.m_alloc_tracker.peakBytesAllocated = rootParser.m_alloc_tracker.bytesAllocated;
            return true;
        }

        /// <summary>expat_free: the bookkeeping of FREE(parser, p) for a block of the given size.</summary>
        internal static void FREE(XML_Parser parser, long size)
        {
            if (size < 0)
                return;
            XML_Parser rootParser = getRootParserOf(parser, out _);
            ulong bytesAllocated = (ulong)(SIZEOF_SIZE_T + EXPAT_MALLOC_PADDING + size);
            rootParser.m_alloc_tracker.bytesAllocated -= Math.Min(bytesAllocated, rootParser.m_alloc_tracker.bytesAllocated);
            parser.m_trackedHere -= Math.Min(bytesAllocated, parser.m_trackedHere);
        }

        /// <summary>expat_realloc: the bookkeeping of REALLOC(parser, p, size) from prevSize.</summary>
        internal static bool REALLOC(XML_Parser parser, long prevSize, long size)
        {
            if (prevSize < 0)
                return MALLOC(parser, size);
            XML_Parser rootParser = getRootParserOf(parser, out _);
            bool isIncrease = size > prevSize;
            ulong absDiff = (ulong)(size > prevSize ? size - prevSize : prevSize - size);
            if (isIncrease)
            {
                if (!expat_heap_increase_tolerable(rootParser, absDiff))
                    return false;
                rootParser.m_alloc_tracker.bytesAllocated += absDiff;
                parser.m_trackedHere += absDiff;
            }
            else
            {
                rootParser.m_alloc_tracker.bytesAllocated -= Math.Min(absDiff, rootParser.m_alloc_tracker.bytesAllocated);
                parser.m_trackedHere -= Math.Min(absDiff, parser.m_trackedHere);
            }
            if (rootParser.m_alloc_tracker.bytesAllocated > rootParser.m_alloc_tracker.peakBytesAllocated)
                rootParser.m_alloc_tracker.peakBytesAllocated = rootParser.m_alloc_tracker.bytesAllocated;
            return true;
        }

        // ------------------------------------------------------------------ creation

        /// <summary>XML_ParserCreate</summary>
        internal static XML_Parser XML_ParserCreate(XCHARP encodingName) => XML_ParserCreate_MM(encodingName, XCHARP.NULL);

        /// <summary>XML_ParserCreateNS</summary>
        internal static XML_Parser XML_ParserCreateNS(XCHARP encodingName, byte nsSep) =>
            XML_ParserCreate_MM(encodingName, new XCHARP(new byte[] { nsSep, 0 }, 0));

        private static readonly XCHARP implicitContext = XCHARP.Literal("xml=http://www.w3.org/XML/1998/namespace");

        /// <summary>generate_hash_secret_salt: the Windows build uses rand_s.</summary>
        private static sipkey generate_hash_secret_salt()
        {
            var bytes = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            return siphash.sip_tokey(bytes);
        }

        /// <summary>beforeHandler</summary>
        private static void beforeHandler(XML_Parser parser) => parser.m_handlerCallDepth++;

        /// <summary>afterHandler</summary>
        private static void afterHandler(XML_Parser parser) => parser.m_handlerCallDepth--;

        /// <summary>isCalledFromInsideHandler</summary>
        private static bool isCalledFromInsideHandler(XML_Parser parser) => parser.m_handlerCallDepth > 0;

        /// <summary>callUnknownEncodingRelease</summary>
        private static void callUnknownEncodingRelease(XML_Parser parser)
        {
            beforeHandler(parser);
            try
            {
                parser.m_unknownEncodingRelease(parser.m_unknownEncodingData);
            }
            finally
            {
                afterHandler(parser);
            }
            parser.m_unknownEncodingRelease = null;
            parser.m_unknownEncodingData = null;
        }

        /// <summary>callUnknownEncodingConvert</summary>
        private static int callUnknownEncodingConvert(object data, byte[] b, int p)
        {
            XML_Parser parser = (XML_Parser)data;
            beforeHandler(parser);
            try
            {
                return parser.m_unknownEncodingConvert(parser.m_unknownEncodingData, b, p);
            }
            finally
            {
                afterHandler(parser);
            }
        }

        /// <summary>callProcessor</summary>
        private static XML_Error callProcessor(XML_Parser parser, int start, int end, ref int endPtr)
        {
            long have_now = end - start;
            if (parser.m_reparseDeferralEnabled && !parser.m_parsingStatus.finalBuffer)
            {
                // Heuristic: don't try to parse a partial token again until the amount of
                // available data has increased significantly.
                long had_before = parser.m_partialTokenBytesBefore;
                // ...but *do* try anyway if we're close to causing a reallocation.
                long available_buffer = parser.m_bufferPtr;
                available_buffer -= Math.Min(available_buffer, XML_CONTEXT_BYTES);
                // m_lastBufferRequestSize is never assigned a value < 0, so the cast is ok
                available_buffer += parser.m_bufferLim - parser.m_bufferEnd;
                bool enough = (have_now >= 2 * had_before) || ((long)parser.m_lastBufferRequestSize > available_buffer);
                if (!enough)
                {
                    endPtr = start; // callers may expect this to be set
                    return XML_ERROR_NONE;
                }
            }
            XML_Error ret;
            endPtr = start;
            while (true)
            {
                // Run the processor only once if parsing is not suspended or the parser is
                // in a state of reentry.
                ret = parser.m_processor(parser, endPtr, end, ref endPtr);
                if (parser.m_parsingStatus.parsing != XML_PARSING)
                    parser.m_reenter = false;
                if (!parser.m_reenter)
                    break;
                parser.m_reenter = false;
                if (ret != XML_ERROR_NONE)
                    return ret;
            }
            if (ret == XML_ERROR_NONE)
            {
                // if we consumed nothing, remember what we had on this parse attempt.
                if (endPtr == start)
                    parser.m_partialTokenBytesBefore = have_now;
                else
                    parser.m_partialTokenBytesBefore = 0;
            }
            return ret;
        }

        /// <summary>startParsing</summary>
        private static bool startParsing(XML_Parser parser)
        {
            /* hash functions must be initialized before setContext() is called */
            if (parser.m_hash_secret_salt_set != true)
            {
                parser.m_hash_secret_salt_128 = generate_hash_secret_salt();
                parser.m_hash_secret_salt_set = true;
            }
            if (parser.m_ns)
            {
                /* implicit context only set for root parser, since child
                   parsers (i.e. external entity parsers) will inherit it */
                return setContext(parser, implicitContext);
            }
            return true;
        }

        /// <summary>XML_ParserCreate_MM (the memory suite is always the CLR's)</summary>
        internal static XML_Parser XML_ParserCreate_MM(XCHARP encodingName, XCHARP nameSep) =>
            parserCreate(encodingName, nameSep, null, null);

        /// <summary>parserCreate</summary>
        private static XML_Parser parserCreate(XCHARP encodingName, XCHARP nameSep, DTD dtd, XML_Parser parentParser)
        {
            ulong increase = (ulong)(SIZEOF_SIZE_T + EXPAT_MALLOC_PADDING + XML_ParserStruct.CSize);
            if (parentParser != null)
            {
                XML_Parser root = getRootParserOf(parentParser, out _);
                if (!expat_heap_increase_tolerable(root, increase))
                    return null;
            }
            XML_Parser parser = new XML_Parser();
            parser.m_alloc_tracker = default;
            if (parentParser == null)
            {
                parser.m_alloc_tracker.debugLevel = getDebugLevel("EXPAT_MALLOC_DEBUG", 0u);
                parser.m_alloc_tracker.maximumAmplificationFactor = EXPAT_ALLOC_TRACKER_MAXIMUM_AMPLIFICATION_DEFAULT;
                parser.m_alloc_tracker.activationThresholdBytes = EXPAT_ALLOC_TRACKER_ACTIVATION_THRESHOLD_DEFAULT;
                // NOTE: This initialization needs to come this early because these fields
                //       are read by allocation tracking code
                parser.m_parentParser = null;
                parser.m_accounting.countBytesDirect = 0;
            }
            else
                parser.m_parentParser = parentParser;

            // Record XML_ParserStruct allocation we did a few lines up before
            XML_Parser rootParser = getRootParserOf(parser, out _);
            rootParser.m_alloc_tracker.bytesAllocated += increase;
            parser.m_trackedHere += increase;

            parser.m_buffer = null;
            parser.m_bufferLim = 0;

            parser.m_attsSize = INIT_ATTS_SIZE;
            if (!MALLOC(parser, (long)parser.m_attsSize * SIZEOF_ATTRIBUTE))
                return null;
            parser.m_atts = new ATTRIBUTE[parser.m_attsSize];
            parser.m_appAtts = new XCHARP[parser.m_attsSize * 4];
            if (!MALLOC(parser, INIT_DATA_BUF_SIZE))
                return null;
            parser.m_dataBuf = new byte[INIT_DATA_BUF_SIZE];

            if (dtd != null)
                parser.m_dtd = dtd;
            else
            {
                parser.m_dtd = dtdCreate(parser);
                if (parser.m_dtd == null)
                    return null;
            }

            parser.m_freeBindingList = null;
            parser.m_freeTagList = null;
            parser.m_freeEntities = null;

            parser.m_groupSize = 0;
            parser.m_groupConnector = null;

            parser.m_unknownEncodingHandler = null;
            parser.m_unknownEncodingHandlerData = null;

            parser.m_namespaceSeparator = (byte)'!';
            parser.m_ns = false;
            parser.m_ns_triplets = false;

            parser.m_nsAtts = null;
            parser.m_nsAttsVersion = 0;
            parser.m_nsAttsPower = 0;

            parser.m_protocolEncodingName = XCHARP.NULL;

            poolInit(parser.m_tempPool, parser);
            poolInit(parser.m_temp2Pool, parser);
            parserInit(parser, encodingName);

            if (!encodingName.IsNull && parser.m_protocolEncodingName.IsNull)
            {
                if (dtd != null)
                {
                    // We need to stop the upcoming call to XML_ParserFree from happily
                    // destroying parser->m_dtd because the DTD is shared with the parent
                    // parser and the only guard that keeps XML_ParserFree from destroying
                    // parser->m_dtd is parser->m_isParamEntity but it will be set to
                    // XML_TRUE only later in XML_ExternalEntityParserCreate (or not at all).
                    parser.m_dtd = null;
                }
                XML_ParserFree(parser);
                return null;
            }

            if (!nameSep.IsNull)
            {
                parser.m_ns = true;
                parser.m_internalEncoding = XmlGetUtf8InternalEncodingNS();
                parser.m_namespaceSeparator = nameSep[0];
            }
            else
                parser.m_internalEncoding = XmlGetUtf8InternalEncoding();

            return parser;
        }

        /// <summary>parserInit</summary>
        private static void parserInit(XML_Parser parser, XCHARP encodingName)
        {
            parser.m_processor = prologInitProcessor;
            XmlPrologStateInit(parser.m_prologState);
            if (!encodingName.IsNull)
                parser.m_protocolEncodingName = copyString(encodingName, parser);
            parser.m_curBase = XCHARP.NULL;
            XmlInitEncoding(parser.m_initEncoding, e => parser.m_encoding = e, null, false);
            parser.m_userData = null;
            parser.m_handlerArg = null;
            parser.m_startElementHandler = null;
            parser.m_endElementHandler = null;
            parser.m_characterDataHandler = null;
            parser.m_processingInstructionHandler = null;
            parser.m_commentHandler = null;
            parser.m_startCdataSectionHandler = null;
            parser.m_endCdataSectionHandler = null;
            parser.m_defaultHandler = null;
            parser.m_startDoctypeDeclHandler = null;
            parser.m_endDoctypeDeclHandler = null;
            parser.m_unparsedEntityDeclHandler = null;
            parser.m_notationDeclHandler = null;
            parser.m_startNamespaceDeclHandler = null;
            parser.m_endNamespaceDeclHandler = null;
            parser.m_notStandaloneHandler = null;
            parser.m_externalEntityRefHandler = null;
            parser.m_externalEntityRefHandlerArg = parser;
            parser.m_skippedEntityHandler = null;
            parser.m_elementDeclHandler = null;
            parser.m_attlistDeclHandler = null;
            parser.m_entityDeclHandler = null;
            parser.m_xmlDeclHandler = null;
            parser.m_bufferPtr = 0;
            parser.m_bufferEnd = 0;
            parser.m_parseEndByteIndex = 0;
            parser.m_parseEndPtr = -1;
            parser.m_partialTokenBytesBefore = 0;
            parser.m_reparseDeferralEnabled = g_reparseDeferralEnabledDefault;
            parser.m_lastBufferRequestSize = 0;
            parser.m_declElementType = null;
            parser.m_declAttributeId = null;
            parser.m_declEntity = null;
            parser.m_doctypeName = XCHARP.NULL;
            parser.m_doctypeSysid = XCHARP.NULL;
            parser.m_doctypePubid = XCHARP.NULL;
            parser.m_declAttributeType = XCHARP.NULL;
            parser.m_declNotationName = XCHARP.NULL;
            parser.m_declNotationPublicId = XCHARP.NULL;
            parser.m_declAttributeIsCdata = false;
            parser.m_declAttributeIsId = false;
            parser.m_position = default;
            parser.m_errorCode = XML_ERROR_NONE;
            parser.m_eventPtr = -1;
            parser.m_eventEndPtr = -1;
            parser.m_positionPtr = -1;
            parser.m_openInternalEntities = null;
            parser.m_openAttributeEntities = null;
            parser.m_openValueEntities = null;
            parser.m_defaultExpandInternalEntities = true;
            parser.m_tagLevel = 0;
            parser.m_tagStack = null;
            parser.m_inheritedBindings = null;
            parser.m_nSpecifiedAtts = 0;
            parser.m_unknownEncodingMem = false;
            parser.m_unknownEncodingConvert = null;
            parser.m_unknownEncodingRelease = null;
            parser.m_unknownEncodingData = null;
            parser.m_parsingStatus.parsing = XML_INITIALIZED;
            // Reentry can only be triggered inside m_processor calls
            parser.m_reenter = false;
            // Only an internal flag that is not exposed via API
            parser.m_handlerCallDepth = 0;
            parser.m_isParamEntity = false;
            parser.m_useForeignDTD = false;
            parser.m_paramEntityParsing = XML_ParamEntityParsing.XML_PARAM_ENTITY_PARSING_NEVER;
            parser.m_hash_secret_salt_128 = default;
            parser.m_hash_secret_salt_set = false;

            parser.m_accounting = default;
            parser.m_accounting.debugLevel = getDebugLevel("EXPAT_ACCOUNTING_DEBUG", 0u);
            parser.m_accounting.maximumAmplificationFactor = EXPAT_BILLION_LAUGHS_ATTACK_PROTECTION_MAXIMUM_AMPLIFICATION_DEFAULT;
            parser.m_accounting.activationThresholdBytes = EXPAT_BILLION_LAUGHS_ATTACK_PROTECTION_ACTIVATION_THRESHOLD_DEFAULT;

            parser.m_entity_stats = default;
            parser.m_entity_stats.debugLevel = getDebugLevel("EXPAT_ENTITY_DEBUG", 0u);
        }

        /// <summary>moveToFreeBindingList</summary>
        private static void moveToFreeBindingList(XML_Parser parser, BINDING bindings)
        {
            while (bindings != null)
            {
                BINDING b = bindings;
                bindings = bindings.nextTagBinding;
                b.nextTagBinding = parser.m_freeBindingList;
                parser.m_freeBindingList = b;
            }
        }

        /// <summary>moveEntityList</summary>
        private static void moveEntityList(ref OPEN_INTERNAL_ENTITY dst, ref OPEN_INTERNAL_ENTITY src)
        {
            for (OPEN_INTERNAL_ENTITY head = src; head != null;)
            {
                OPEN_INTERNAL_ENTITY openEntity = head;
                head = head.next;
                openEntity.next = dst;
                dst = openEntity;
            }
            src = null;
        }

        /// <summary>XML_ParserReset</summary>
        internal static bool XML_ParserReset(XML_Parser parser, XCHARP encodingName)
        {
            TAG tStk;
            if (parser == null || isCalledFromInsideHandler(parser))
                return false;
            if (parser.m_parentParser != null)
                return false;
            /* move m_tagStack to m_freeTagList */
            tStk = parser.m_tagStack;
            while (tStk != null)
            {
                TAG tag = tStk;
                tStk = tStk.parent;
                tag.parent = parser.m_freeTagList;
                moveToFreeBindingList(parser, tag.bindings);
                tag.bindings = null;
                parser.m_freeTagList = tag;
            }
            /* move open internal entities to free entities list */
            moveEntityList(ref parser.m_freeEntities, ref parser.m_openInternalEntities);
            moveEntityList(ref parser.m_freeEntities, ref parser.m_openAttributeEntities);
            moveEntityList(ref parser.m_freeEntities, ref parser.m_openValueEntities);
            moveToFreeBindingList(parser, parser.m_inheritedBindings);
            parser.m_unknownEncodingMem = false;
            if (parser.m_unknownEncodingRelease != null)
                callUnknownEncodingRelease(parser);
            poolClear(parser.m_tempPool);
            poolClear(parser.m_temp2Pool);
            parser.m_protocolEncodingName = XCHARP.NULL;
            parserInit(parser, encodingName);
            dtdReset(parser.m_dtd, parser);
            return true;
        }

        /// <summary>parserBusy</summary>
        private static bool parserBusy(XML_Parser parser)
        {
            switch (parser.m_parsingStatus.parsing)
            {
                case XML_PARSING:
                case XML_SUSPENDED:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>XML_SetEncoding</summary>
        internal static XML_Status XML_SetEncoding(XML_Parser parser, XCHARP encodingName)
        {
            if (parser == null)
                return XML_Status.XML_STATUS_ERROR;
            // Block after XML_Parse()/XML_ParseBuffer() has been called.
            if (parserBusy(parser))
                return XML_Status.XML_STATUS_ERROR;
            if (encodingName.IsNull)
                parser.m_protocolEncodingName = XCHARP.NULL;
            else
            {
                parser.m_protocolEncodingName = copyString(encodingName, parser);
                if (parser.m_protocolEncodingName.IsNull)
                    return XML_Status.XML_STATUS_ERROR;
            }
            return XML_Status.XML_STATUS_OK;
        }

        /// <summary>XML_ExternalEntityParserCreate</summary>
        internal static XML_Parser XML_ExternalEntityParserCreate(XML_Parser oldParser, XCHARP context, XCHARP encodingName)
        {
            XML_Parser parser = oldParser;
            DTD newDtd = null;
            if (oldParser == null)
                return null;
            DTD oldDtd = parser.m_dtd;
            var oldStartElementHandler = parser.m_startElementHandler;
            var oldEndElementHandler = parser.m_endElementHandler;
            var oldCharacterDataHandler = parser.m_characterDataHandler;
            var oldProcessingInstructionHandler = parser.m_processingInstructionHandler;
            var oldCommentHandler = parser.m_commentHandler;
            var oldStartCdataSectionHandler = parser.m_startCdataSectionHandler;
            var oldEndCdataSectionHandler = parser.m_endCdataSectionHandler;
            var oldDefaultHandler = parser.m_defaultHandler;
            var oldUnparsedEntityDeclHandler = parser.m_unparsedEntityDeclHandler;
            var oldNotationDeclHandler = parser.m_notationDeclHandler;
            var oldStartNamespaceDeclHandler = parser.m_startNamespaceDeclHandler;
            var oldEndNamespaceDeclHandler = parser.m_endNamespaceDeclHandler;
            var oldNotStandaloneHandler = parser.m_notStandaloneHandler;
            var oldExternalEntityRefHandler = parser.m_externalEntityRefHandler;
            var oldSkippedEntityHandler = parser.m_skippedEntityHandler;
            var oldUnknownEncodingHandler = parser.m_unknownEncodingHandler;
            var oldUnknownEncodingHandlerData = parser.m_unknownEncodingHandlerData;
            var oldElementDeclHandler = parser.m_elementDeclHandler;
            var oldAttlistDeclHandler = parser.m_attlistDeclHandler;
            var oldEntityDeclHandler = parser.m_entityDeclHandler;
            var oldXmlDeclHandler = parser.m_xmlDeclHandler;
            var oldDeclElementType = parser.m_declElementType;
            var oldUserData = parser.m_userData;
            var oldHandlerArg = parser.m_handlerArg;
            var oldDefaultExpandInternalEntities = parser.m_defaultExpandInternalEntities;
            var oldExternalEntityRefHandlerArg = parser.m_externalEntityRefHandlerArg;
            var oldParamEntityParsing = parser.m_paramEntityParsing;
            var oldInEntityValue = parser.m_prologState.inEntityValue;
            var oldns_triplets = parser.m_ns_triplets;
            /* Note that the new parser shares the same hash secret as the old
               parser, so that dtdCopy and copyEntityTable can lookup values
               from hash tables associated with either parser without us having
               to worry which hash secrets each table has. */
            var oldhash_secret_salt_128 = parser.m_hash_secret_salt_128;
            var oldhash_secret_salt_set = parser.m_hash_secret_salt_set;
            var oldReparseDeferralEnabled = parser.m_reparseDeferralEnabled;

            if (context.IsNull)
                newDtd = oldDtd;

            /* Note that the magical uses of the pre-processor to make field
               access look more like C++ require that `parser' be overwritten
               here.  This makes this function more painful to follow than it
               would be otherwise. */
            if (parser.m_ns)
                parser = parserCreate(encodingName, new XCHARP(new byte[] { parser.m_namespaceSeparator, 0 }, 0), newDtd, oldParser);
            else
                parser = parserCreate(encodingName, XCHARP.NULL, newDtd, oldParser);

            if (parser == null)
                return null;

            parser.m_startElementHandler = oldStartElementHandler;
            parser.m_endElementHandler = oldEndElementHandler;
            parser.m_characterDataHandler = oldCharacterDataHandler;
            parser.m_processingInstructionHandler = oldProcessingInstructionHandler;
            parser.m_commentHandler = oldCommentHandler;
            parser.m_startCdataSectionHandler = oldStartCdataSectionHandler;
            parser.m_endCdataSectionHandler = oldEndCdataSectionHandler;
            parser.m_defaultHandler = oldDefaultHandler;
            parser.m_unparsedEntityDeclHandler = oldUnparsedEntityDeclHandler;
            parser.m_notationDeclHandler = oldNotationDeclHandler;
            parser.m_startNamespaceDeclHandler = oldStartNamespaceDeclHandler;
            parser.m_endNamespaceDeclHandler = oldEndNamespaceDeclHandler;
            parser.m_notStandaloneHandler = oldNotStandaloneHandler;
            parser.m_externalEntityRefHandler = oldExternalEntityRefHandler;
            parser.m_skippedEntityHandler = oldSkippedEntityHandler;
            parser.m_unknownEncodingHandler = oldUnknownEncodingHandler;
            parser.m_unknownEncodingHandlerData = oldUnknownEncodingHandlerData;
            parser.m_elementDeclHandler = oldElementDeclHandler;
            parser.m_attlistDeclHandler = oldAttlistDeclHandler;
            parser.m_entityDeclHandler = oldEntityDeclHandler;
            parser.m_xmlDeclHandler = oldXmlDeclHandler;
            parser.m_declElementType = oldDeclElementType;
            parser.m_userData = oldUserData;
            if (oldUserData == oldHandlerArg)
                parser.m_handlerArg = parser.m_userData;
            else
                parser.m_handlerArg = parser;
            if (oldExternalEntityRefHandlerArg != oldParser)
                parser.m_externalEntityRefHandlerArg = oldExternalEntityRefHandlerArg;
            parser.m_defaultExpandInternalEntities = oldDefaultExpandInternalEntities;
            parser.m_ns_triplets = oldns_triplets;
            parser.m_hash_secret_salt_128 = oldhash_secret_salt_128;
            parser.m_hash_secret_salt_set = oldhash_secret_salt_set;
            parser.m_reparseDeferralEnabled = oldReparseDeferralEnabled;
            parser.m_parentParser = oldParser;
            parser.m_paramEntityParsing = oldParamEntityParsing;
            parser.m_prologState.inEntityValue = oldInEntityValue;
            if (!context.IsNull)
            {
                if (!dtdCopy(oldParser, parser.m_dtd, oldDtd, parser) || !setContext(parser, context))
                {
                    XML_ParserFree(parser);
                    return null;
                }
                parser.m_processor = externalEntityInitProcessor;
            }
            else
            {
                /* The DTD instance referenced by parser->m_dtd is shared between the
                   document's root parser and external PE parsers, therefore one does not
                   need to call setContext. In addition, one also *must* not call
                   setContext, because this would overwrite existing prefix->binding
                   pointers in parser->m_dtd with ones that get destroyed with the external
                   PE parser. This would leave those prefixes with dangling pointers. */
                parser.m_isParamEntity = true;
                XmlPrologStateInitExternalEntity(parser.m_prologState);
                parser.m_processor = externalParEntInitProcessor;
            }
            return parser;
        }

        /// <summary>XML_ParserFree: the managed objects are collected; this returns the tracked bytes.</summary>
        internal static void XML_ParserFree(XML_Parser parser)
        {
            if (parser == null || isCalledFromInsideHandler(parser))
                return;
            if (parser.m_unknownEncodingRelease != null)
                callUnknownEncodingRelease(parser);
            if (parser.m_parentParser != null)
            {
                XML_Parser rootParser = getRootParserOf(parser, out _);
                rootParser.m_alloc_tracker.bytesAllocated -= Math.Min(parser.m_trackedHere, rootParser.m_alloc_tracker.bytesAllocated);
                parser.m_trackedHere = 0;
            }
        }

        /// <summary>XML_UseParserAsHandlerArg</summary>
        internal static void XML_UseParserAsHandlerArg(XML_Parser parser)
        {
            if (parser != null)
                parser.m_handlerArg = parser;
        }

        /// <summary>XML_UseForeignDTD</summary>
        internal static XML_Error XML_UseForeignDTD(XML_Parser parser, bool useDTD)
        {
            if (parser == null)
                return XML_ERROR_INVALID_ARGUMENT;
            /* block after XML_Parse()/XML_ParseBuffer() has been called */
            if (parserBusy(parser))
                return XML_ERROR_CANT_CHANGE_FEATURE_ONCE_PARSING;
            parser.m_useForeignDTD = useDTD;
            return XML_ERROR_NONE;
        }

        /// <summary>XML_SetReturnNSTriplet</summary>
        internal static void XML_SetReturnNSTriplet(XML_Parser parser, int do_nst)
        {
            if (parser == null)
                return;
            /* block after XML_Parse()/XML_ParseBuffer() has been called */
            if (parserBusy(parser))
                return;
            parser.m_ns_triplets = do_nst != 0;
        }

        /// <summary>XML_SetUserData</summary>
        internal static void XML_SetUserData(XML_Parser parser, object p)
        {
            if (parser == null)
                return;
            if (parser.m_handlerArg == parser.m_userData)
                parser.m_handlerArg = parser.m_userData = p;
            else
                parser.m_userData = p;
        }

        /// <summary>XML_GetUserData</summary>
        internal static object XML_GetUserData(XML_Parser parser) => parser.m_userData;

        /// <summary>XML_SetBase</summary>
        internal static XML_Status XML_SetBase(XML_Parser parser, XCHARP p)
        {
            if (parser == null)
                return XML_Status.XML_STATUS_ERROR;
            if (!p.IsNull)
            {
                p = poolCopyString(parser.m_dtd.pool, p);
                if (p.IsNull)
                    return XML_Status.XML_STATUS_ERROR;
                parser.m_curBase = p;
            }
            else
                parser.m_curBase = XCHARP.NULL;
            return XML_Status.XML_STATUS_OK;
        }

        /// <summary>XML_GetBase</summary>
        internal static XCHARP XML_GetBase(XML_Parser parser) => parser == null ? XCHARP.NULL : parser.m_curBase;

        /// <summary>XML_GetSpecifiedAttributeCount</summary>
        internal static int XML_GetSpecifiedAttributeCount(XML_Parser parser) => parser == null ? -1 : parser.m_nSpecifiedAtts;

        /// <summary>XML_GetIdAttributeIndex</summary>
        internal static int XML_GetIdAttributeIndex(XML_Parser parser) => parser == null ? -1 : parser.m_idAttIndex;

        internal static void XML_SetElementHandler(XML_Parser parser, XML_StartElementHandler start, XML_EndElementHandler end)
        {
            if (parser == null)
                return;
            parser.m_startElementHandler = start;
            parser.m_endElementHandler = end;
        }

        internal static void XML_SetStartElementHandler(XML_Parser parser, XML_StartElementHandler start)
        {
            if (parser != null)
                parser.m_startElementHandler = start;
        }

        internal static void XML_SetEndElementHandler(XML_Parser parser, XML_EndElementHandler end)
        {
            if (parser != null)
                parser.m_endElementHandler = end;
        }

        internal static void XML_SetCharacterDataHandler(XML_Parser parser, XML_CharacterDataHandler handler)
        {
            if (parser != null)
                parser.m_characterDataHandler = handler;
        }

        internal static void XML_SetProcessingInstructionHandler(XML_Parser parser, XML_ProcessingInstructionHandler handler)
        {
            if (parser != null)
                parser.m_processingInstructionHandler = handler;
        }

        internal static void XML_SetCommentHandler(XML_Parser parser, XML_CommentHandler handler)
        {
            if (parser != null)
                parser.m_commentHandler = handler;
        }

        internal static void XML_SetCdataSectionHandler(XML_Parser parser, XML_StartCdataSectionHandler start, XML_EndCdataSectionHandler end)
        {
            if (parser == null)
                return;
            parser.m_startCdataSectionHandler = start;
            parser.m_endCdataSectionHandler = end;
        }

        internal static void XML_SetStartCdataSectionHandler(XML_Parser parser, XML_StartCdataSectionHandler start)
        {
            if (parser != null)
                parser.m_startCdataSectionHandler = start;
        }

        internal static void XML_SetEndCdataSectionHandler(XML_Parser parser, XML_EndCdataSectionHandler end)
        {
            if (parser != null)
                parser.m_endCdataSectionHandler = end;
        }

        internal static void XML_SetDefaultHandler(XML_Parser parser, XML_DefaultHandler handler)
        {
            if (parser == null)
                return;
            parser.m_defaultHandler = handler;
            parser.m_defaultExpandInternalEntities = false;
        }

        internal static void XML_SetDefaultHandlerExpand(XML_Parser parser, XML_DefaultHandler handler)
        {
            if (parser == null)
                return;
            parser.m_defaultHandler = handler;
            parser.m_defaultExpandInternalEntities = true;
        }

        internal static void XML_SetDoctypeDeclHandler(XML_Parser parser, XML_StartDoctypeDeclHandler start, XML_EndDoctypeDeclHandler end)
        {
            if (parser == null)
                return;
            parser.m_startDoctypeDeclHandler = start;
            parser.m_endDoctypeDeclHandler = end;
        }

        internal static void XML_SetStartDoctypeDeclHandler(XML_Parser parser, XML_StartDoctypeDeclHandler start)
        {
            if (parser != null)
                parser.m_startDoctypeDeclHandler = start;
        }

        internal static void XML_SetEndDoctypeDeclHandler(XML_Parser parser, XML_EndDoctypeDeclHandler end)
        {
            if (parser != null)
                parser.m_endDoctypeDeclHandler = end;
        }

        internal static void XML_SetUnparsedEntityDeclHandler(XML_Parser parser, XML_UnparsedEntityDeclHandler handler)
        {
            if (parser != null)
                parser.m_unparsedEntityDeclHandler = handler;
        }

        internal static void XML_SetNotationDeclHandler(XML_Parser parser, XML_NotationDeclHandler handler)
        {
            if (parser != null)
                parser.m_notationDeclHandler = handler;
        }

        internal static void XML_SetNamespaceDeclHandler(XML_Parser parser, XML_StartNamespaceDeclHandler start, XML_EndNamespaceDeclHandler end)
        {
            if (parser == null)
                return;
            parser.m_startNamespaceDeclHandler = start;
            parser.m_endNamespaceDeclHandler = end;
        }

        internal static void XML_SetStartNamespaceDeclHandler(XML_Parser parser, XML_StartNamespaceDeclHandler start)
        {
            if (parser != null)
                parser.m_startNamespaceDeclHandler = start;
        }

        internal static void XML_SetEndNamespaceDeclHandler(XML_Parser parser, XML_EndNamespaceDeclHandler end)
        {
            if (parser != null)
                parser.m_endNamespaceDeclHandler = end;
        }

        internal static void XML_SetNotStandaloneHandler(XML_Parser parser, XML_NotStandaloneHandler handler)
        {
            if (parser != null)
                parser.m_notStandaloneHandler = handler;
        }

        internal static void XML_SetExternalEntityRefHandler(XML_Parser parser, XML_ExternalEntityRefHandler handler)
        {
            if (parser != null)
                parser.m_externalEntityRefHandler = handler;
        }

        internal static void XML_SetExternalEntityRefHandlerArg(XML_Parser parser, XML_Parser arg)
        {
            if (parser == null)
                return;
            if (arg != null)
                parser.m_externalEntityRefHandlerArg = arg;
            else
                parser.m_externalEntityRefHandlerArg = parser;
        }

        internal static void XML_SetSkippedEntityHandler(XML_Parser parser, XML_SkippedEntityHandler handler)
        {
            if (parser != null)
                parser.m_skippedEntityHandler = handler;
        }

        internal static void XML_SetUnknownEncodingHandler(XML_Parser parser, XML_UnknownEncodingHandler handler, object data)
        {
            if (parser == null)
                return;
            parser.m_unknownEncodingHandler = handler;
            parser.m_unknownEncodingHandlerData = data;
        }

        internal static void XML_SetElementDeclHandler(XML_Parser parser, XML_ElementDeclHandler eldecl)
        {
            if (parser != null)
                parser.m_elementDeclHandler = eldecl;
        }

        internal static void XML_SetAttlistDeclHandler(XML_Parser parser, XML_AttlistDeclHandler attdecl)
        {
            if (parser != null)
                parser.m_attlistDeclHandler = attdecl;
        }

        internal static void XML_SetEntityDeclHandler(XML_Parser parser, XML_EntityDeclHandler handler)
        {
            if (parser != null)
                parser.m_entityDeclHandler = handler;
        }

        internal static void XML_SetXmlDeclHandler(XML_Parser parser, XML_XmlDeclHandler handler)
        {
            if (parser != null)
                parser.m_xmlDeclHandler = handler;
        }

        /// <summary>XML_SetParamEntityParsing</summary>
        internal static int XML_SetParamEntityParsing(XML_Parser parser, XML_ParamEntityParsing peParsing)
        {
            if (parser == null)
                return 0;
            /* block after XML_Parse()/XML_ParseBuffer() has been called */
            if (parserBusy(parser))
                return 0;
            parser.m_paramEntityParsing = peParsing;
            return 1;
        }

        /// <summary>xmlSetHashSalt / XML_SetHashSalt</summary>
        internal static int XML_SetHashSalt(XML_Parser parser, ulong hash_salt)
        {
            if (parser == null)
                return 0;
            XML_Parser rootParser = getRootParserOf(parser, out _);
            /* block after XML_Parse()/XML_ParseBuffer() has been called */
            if (parserBusy(rootParser))
                return 0;
            rootParser.m_hash_secret_salt_128.k0 = 0;
            rootParser.m_hash_secret_salt_128.k1 = hash_salt;
            if (hash_salt != 0)
                rootParser.m_hash_secret_salt_set = true;
            return 1;
        }

        /// <summary>XML_SetHashSalt16Bytes</summary>
        internal static bool XML_SetHashSalt16Bytes(XML_Parser parser, byte[] entropy)
        {
            if (parser == null)
                return false;
            if (entropy == null)
                return false;
            XML_Parser rootParser = getRootParserOf(parser, out _);
            /* block after XML_Parse()/XML_ParseBuffer() has been called */
            if (parserBusy(rootParser))
                return false;
            rootParser.m_hash_secret_salt_128 = siphash.sip_tokey(entropy);
            rootParser.m_hash_secret_salt_set = true;
            return true;
        }

        /// <summary>XML_Parse (XML_CONTEXT_BYTES > 0: the data is copied into the parse buffer)</summary>
        internal static XML_Status XML_Parse(XML_Parser parser, byte[] s, int sOff, int len, bool isFinal)
        {
            if (parser == null || len < 0 || (s == null && len != 0))
            {
                if (parser != null)
                    parser.m_errorCode = XML_ERROR_INVALID_ARGUMENT;
                return XML_Status.XML_STATUS_ERROR;
            }
            // XML_Parse() is not reentrant: it must not be called from within a handler.
            if (isCalledFromInsideHandler(parser))
                return XML_Status.XML_STATUS_ERROR;
            switch (parser.m_parsingStatus.parsing)
            {
                case XML_SUSPENDED:
                    parser.m_errorCode = XML_ERROR_SUSPENDED;
                    return XML_Status.XML_STATUS_ERROR;
                case XML_FINISHED:
                    parser.m_errorCode = XML_ERROR_FINISHED;
                    return XML_Status.XML_STATUS_ERROR;
                case XML_INITIALIZED:
                    if (parser.m_parentParser == null && !startParsing(parser))
                    {
                        parser.m_errorCode = XML_ERROR_NO_MEMORY;
                        return XML_Status.XML_STATUS_ERROR;
                    }
                    parser.m_parsingStatus.parsing = XML_PARSING;
                    break;
                default:
                    parser.m_parsingStatus.parsing = XML_PARSING;
                    break;
            }

            int buff = XML_GetBuffer(parser, len);
            if (buff < 0)
                return XML_Status.XML_STATUS_ERROR;
            // can be NULL only if len == 0 (and that's fine)
            if (len > 0)
                Buffer.BlockCopy(s, sOff, parser.m_buffer, buff, len);
            return XML_ParseBuffer(parser, len, isFinal);
        }

        /// <summary>XML_ParseBuffer</summary>
        internal static XML_Status XML_ParseBuffer(XML_Parser parser, int len, bool isFinal)
        {
            int start;
            XML_Status result = XML_Status.XML_STATUS_OK;

            if (parser == null || isCalledFromInsideHandler(parser))
                return XML_Status.XML_STATUS_ERROR;
            if (len < 0)
            {
                parser.m_errorCode = XML_ERROR_INVALID_ARGUMENT;
                return XML_Status.XML_STATUS_ERROR;
            }
            switch (parser.m_parsingStatus.parsing)
            {
                case XML_SUSPENDED:
                    parser.m_errorCode = XML_ERROR_SUSPENDED;
                    return XML_Status.XML_STATUS_ERROR;
                case XML_FINISHED:
                    parser.m_errorCode = XML_ERROR_FINISHED;
                    return XML_Status.XML_STATUS_ERROR;
                case XML_INITIALIZED:
                    /* Has someone called XML_GetBuffer successfully before? */
                    if (parser.m_buffer == null)
                    {
                        parser.m_errorCode = XML_ERROR_NO_BUFFER;
                        return XML_Status.XML_STATUS_ERROR;
                    }
                    if (parser.m_parentParser == null && !startParsing(parser))
                    {
                        parser.m_errorCode = XML_ERROR_NO_MEMORY;
                        return XML_Status.XML_STATUS_ERROR;
                    }
                    parser.m_parsingStatus.parsing = XML_PARSING;
                    break;
                default:
                    parser.m_parsingStatus.parsing = XML_PARSING;
                    break;
            }

            // Detect and report integer overflow before it causes harm.
            if ((ulong)len > ulong.MaxValue - parser.m_parseEndByteIndex)
            {
                parser.m_errorCode = XML_ERROR_NO_MEMORY;
                parser.m_eventPtr = parser.m_eventEndPtr = -1;
                parser.m_processor = errorProcessor;
                return XML_Status.XML_STATUS_ERROR;
            }

            start = parser.m_bufferPtr;
            parser.m_positionPtr = start;
            parser.m_bufferEnd += len;
            parser.m_parseEndPtr = parser.m_bufferEnd;
            parser.m_parseEndByteIndex += (ulong)len;
            parser.m_parsingStatus.finalBuffer = isFinal;

            parser.m_errorCode = callProcessor(parser, start, parser.m_parseEndPtr, ref parser.m_bufferPtr);

            if (parser.m_errorCode != XML_ERROR_NONE)
            {
                parser.m_eventEndPtr = parser.m_eventPtr;
                parser.m_processor = errorProcessor;
                return XML_Status.XML_STATUS_ERROR;
            }
            else
            {
                switch (parser.m_parsingStatus.parsing)
                {
                    case XML_SUSPENDED:
                        result = XML_Status.XML_STATUS_SUSPENDED;
                        break;
                    case XML_INITIALIZED:
                    case XML_PARSING:
                        if (isFinal)
                        {
                            parser.m_parsingStatus.parsing = XML_FINISHED;
                            return result;
                        }
                        break;
                    default:
                        break; /* should not happen */
                }
            }

            parser.m_encoding.UpdatePosition(parser.m_buffer, parser.m_positionPtr, parser.m_bufferPtr, ref parser.m_position);
            parser.m_positionPtr = parser.m_bufferPtr;
            return result;
        }

        /// <summary>setParserBuffer</summary>
        private static void setParserBuffer(XML_Parser parser, byte[] newBuf, int newBufSize, int keep)
        {
            parser.m_bufferLim = newBufSize;
            if (parser.m_buffer != null)
            {
                int parsing = parser.m_bufferEnd - parser.m_bufferPtr;
                Buffer.BlockCopy(parser.m_buffer, parser.m_bufferPtr - keep, newBuf, 0, parsing + keep);
                parser.m_buffer = newBuf;
                parser.m_bufferEnd = parsing + keep;
                parser.m_bufferPtr = keep;
            }
            else
            {
                parser.m_buffer = newBuf;
                parser.m_bufferEnd = 0;
                parser.m_bufferPtr = 0;
            }
        }

        /// <summary>XML_GetBuffer: the offset in m_buffer where len bytes may be written, -1 for NULL.</summary>
        internal static int XML_GetBuffer(XML_Parser parser, int len)
        {
            if (parser == null || isCalledFromInsideHandler(parser))
                return -1;
            if (len < 0)
            {
                parser.m_errorCode = XML_ERROR_NO_MEMORY;
                return -1;
            }
            switch (parser.m_parsingStatus.parsing)
            {
                case XML_SUSPENDED:
                    parser.m_errorCode = XML_ERROR_SUSPENDED;
                    return -1;
                case XML_FINISHED:
                    parser.m_errorCode = XML_ERROR_FINISHED;
                    return -1;
                default:
                    break;
            }

            // whether or not the request succeeds, `len` seems to be the app's preferred
            // buffer fill size; remember it.
            parser.m_lastBufferRequestSize = len;
            if (len > parser.m_bufferLim - parser.m_bufferEnd || parser.m_buffer == null)
            {
                /* Do not invoke signed arithmetic overflow: */
                int neededSize = (int)((uint)len + (uint)(parser.m_bufferEnd - parser.m_bufferPtr));
                if (neededSize < 0)
                {
                    parser.m_errorCode = XML_ERROR_NO_MEMORY;
                    return -1;
                }
                int parsed = parser.m_bufferPtr;
                int keep = parsed;
                if (keep > XML_CONTEXT_BYTES)
                    keep = XML_CONTEXT_BYTES;
                /* Detect and prevent integer overflow */
                if (keep > int.MaxValue - neededSize)
                {
                    parser.m_errorCode = XML_ERROR_NO_MEMORY;
                    return -1;
                }
                neededSize += keep;
                if (parser.m_buffer != null && neededSize <= parser.m_bufferLim)
                {
                    if (keep < parsed)
                    {
                        int offset = parsed - keep;
                        /* The buffer pointers cannot be NULL here; we have at least some bytes
                         * in the buffer */
                        Buffer.BlockCopy(parser.m_buffer, offset, parser.m_buffer, 0, parser.m_bufferEnd - parser.m_bufferPtr + keep);
                        parser.m_bufferEnd -= offset;
                        parser.m_bufferPtr -= offset;
                    }
                }
                else
                {
                    int bufferSize = parser.m_bufferLim;
                    if (bufferSize == 0)
                        bufferSize = INIT_BUFFER_SIZE;
                    do
                    {
                        /* Do not invoke signed arithmetic overflow: */
                        bufferSize = (int)(2U * (uint)bufferSize);
                    } while (bufferSize < neededSize && bufferSize > 0);
                    if (bufferSize <= 0 || bufferSize > Array.MaxLength)
                    {
                        parser.m_errorCode = XML_ERROR_NO_MEMORY;
                        return -1;
                    }
                    // NOTE: We are avoiding MALLOC(..) here to leave limiting
                    //       the input size to the application using Expat.
                    byte[] newBuf;
                    try
                    {
                        newBuf = new byte[bufferSize];
                    }
                    catch (OutOfMemoryException)
                    {
                        parser.m_errorCode = XML_ERROR_NO_MEMORY;
                        return -1;
                    }
                    setParserBuffer(parser, newBuf, bufferSize, keep);
                }
                parser.m_eventPtr = parser.m_eventEndPtr = -1;
                parser.m_positionPtr = -1;
            }
            return parser.m_bufferEnd;
        }

        /// <summary>triggerReenter</summary>
        private static void triggerReenter(XML_Parser parser) => parser.m_reenter = true;

        /// <summary>XML_StopParser</summary>
        internal static XML_Status XML_StopParser(XML_Parser parser, bool resumable)
        {
            if (parser == null)
                return XML_Status.XML_STATUS_ERROR;
            switch (parser.m_parsingStatus.parsing)
            {
                case XML_INITIALIZED:
                    parser.m_errorCode = XML_ERROR_NOT_STARTED;
                    return XML_Status.XML_STATUS_ERROR;
                case XML_SUSPENDED:
                    if (resumable)
                    {
                        parser.m_errorCode = XML_ERROR_SUSPENDED;
                        return XML_Status.XML_STATUS_ERROR;
                    }
                    parser.m_parsingStatus.parsing = XML_FINISHED;
                    break;
                case XML_FINISHED:
                    parser.m_errorCode = XML_ERROR_FINISHED;
                    return XML_Status.XML_STATUS_ERROR;
                case XML_PARSING:
                    if (resumable)
                    {
                        if (parser.m_isParamEntity)
                        {
                            parser.m_errorCode = XML_ERROR_SUSPEND_PE;
                            return XML_Status.XML_STATUS_ERROR;
                        }
                        parser.m_parsingStatus.parsing = XML_SUSPENDED;
                    }
                    else
                        parser.m_parsingStatus.parsing = XML_FINISHED;
                    break;
            }
            return XML_Status.XML_STATUS_OK;
        }

        /// <summary>XML_ResumeParser</summary>
        internal static XML_Status XML_ResumeParser(XML_Parser parser)
        {
            XML_Status result = XML_Status.XML_STATUS_OK;
            if (parser == null || isCalledFromInsideHandler(parser))
                return XML_Status.XML_STATUS_ERROR;
            if (parser.m_parsingStatus.parsing != XML_SUSPENDED)
            {
                parser.m_errorCode = XML_ERROR_NOT_SUSPENDED;
                return XML_Status.XML_STATUS_ERROR;
            }
            parser.m_parsingStatus.parsing = XML_PARSING;

            parser.m_errorCode = callProcessor(parser, parser.m_bufferPtr, parser.m_parseEndPtr, ref parser.m_bufferPtr);

            if (parser.m_errorCode != XML_ERROR_NONE)
            {
                parser.m_eventEndPtr = parser.m_eventPtr;
                parser.m_processor = errorProcessor;
                return XML_Status.XML_STATUS_ERROR;
            }
            else
            {
                switch (parser.m_parsingStatus.parsing)
                {
                    case XML_SUSPENDED:
                        result = XML_Status.XML_STATUS_SUSPENDED;
                        break;
                    case XML_INITIALIZED:
                    case XML_PARSING:
                        if (parser.m_parsingStatus.finalBuffer)
                        {
                            parser.m_parsingStatus.parsing = XML_FINISHED;
                            return result;
                        }
                        break;
                    default:
                        break;
                }
            }

            parser.m_encoding.UpdatePosition(parser.m_buffer, parser.m_positionPtr, parser.m_bufferPtr, ref parser.m_position);
            parser.m_positionPtr = parser.m_bufferPtr;
            return result;
        }

        /// <summary>XML_GetParsingStatus</summary>
        internal static XML_ParsingStatus XML_GetParsingStatus(XML_Parser parser) => parser.m_parsingStatus;

        /// <summary>XML_GetErrorCode</summary>
        internal static XML_Error XML_GetErrorCode(XML_Parser parser) =>
            parser == null ? XML_ERROR_INVALID_ARGUMENT : parser.m_errorCode;

        /// <summary>XML_GetCurrentByteIndex</summary>
        internal static long XML_GetCurrentByteIndex(XML_Parser parser)
        {
            if (parser == null)
                return -1;
            if (parser.m_eventPtr >= 0)
                return (long)(parser.m_parseEndByteIndex - (ulong)(parser.m_parseEndPtr - parser.m_eventPtr));
            return -1;
        }

        /// <summary>XML_GetCurrentByteCount</summary>
        internal static int XML_GetCurrentByteCount(XML_Parser parser)
        {
            if (parser == null)
                return 0;
            if (parser.m_eventEndPtr >= 0 && parser.m_eventPtr >= 0)
                return parser.m_eventEndPtr - parser.m_eventPtr;
            return 0;
        }

        /// <summary>XML_GetInputContext: the buffer, or null; offset and size as in C.</summary>
        internal static byte[] XML_GetInputContext(XML_Parser parser, out int offset, out int size)
        {
            offset = size = 0;
            if (parser == null)
                return null;
            if (parser.m_eventPtr >= 0 && parser.m_buffer != null)
            {
                offset = parser.m_eventPtr;
                size = parser.m_bufferEnd;
                return parser.m_buffer;
            }
            return null;
        }

        /// <summary>XML_GetCurrentLineNumber</summary>
        internal static ulong XML_GetCurrentLineNumber(XML_Parser parser)
        {
            if (parser == null)
                return 0;
            if (parser.m_eventPtr >= 0 && parser.m_eventPtr >= parser.m_positionPtr)
            {
                parser.m_encoding.UpdatePosition(parser.m_buffer, parser.m_positionPtr, parser.m_eventPtr, ref parser.m_position);
                parser.m_positionPtr = parser.m_eventPtr;
            }
            return parser.m_position.lineNumber + 1;
        }

        /// <summary>XML_GetCurrentColumnNumber</summary>
        internal static ulong XML_GetCurrentColumnNumber(XML_Parser parser)
        {
            if (parser == null)
                return 0;
            if (parser.m_eventPtr >= 0 && parser.m_eventPtr >= parser.m_positionPtr)
            {
                parser.m_encoding.UpdatePosition(parser.m_buffer, parser.m_positionPtr, parser.m_eventPtr, ref parser.m_position);
                parser.m_positionPtr = parser.m_eventPtr;
            }
            return parser.m_position.columnNumber;
        }

        /// <summary>XML_DefaultCurrent</summary>
        internal static void XML_DefaultCurrent(XML_Parser parser)
        {
            if (parser == null)
                return;
            if (parser.m_defaultHandler != null)
            {
                if (parser.m_openInternalEntities != null)
                    reportDefault(parser, parser.m_internalEncoding, parser.m_openInternalEntities.entity.textPtr.a,
                                  parser.m_openInternalEntities.internalEventPtr,
                                  parser.m_openInternalEntities.internalEventEndPtr);
                else
                    reportDefault(parser, parser.m_encoding, parser.m_buffer, parser.m_eventPtr, parser.m_eventEndPtr);
            }
        }

        /// <summary>XML_ErrorString</summary>
        internal static string XML_ErrorString(XML_Error code)
        {
            switch (code)
            {
                case XML_ERROR_NONE: return null;
                case XML_ERROR_NO_MEMORY: return "out of memory";
                case XML_ERROR_SYNTAX: return "syntax error";
                case XML_ERROR_NO_ELEMENTS: return "no element found";
                case XML_ERROR_INVALID_TOKEN: return "not well-formed (invalid token)";
                case XML_ERROR_UNCLOSED_TOKEN: return "unclosed token";
                case XML_ERROR_PARTIAL_CHAR: return "partial character";
                case XML_ERROR_TAG_MISMATCH: return "mismatched tag";
                case XML_ERROR_DUPLICATE_ATTRIBUTE: return "duplicate attribute";
                case XML_ERROR_JUNK_AFTER_DOC_ELEMENT: return "junk after document element";
                case XML_ERROR_PARAM_ENTITY_REF: return "illegal parameter entity reference";
                case XML_ERROR_UNDEFINED_ENTITY: return "undefined entity";
                case XML_ERROR_RECURSIVE_ENTITY_REF: return "recursive entity reference";
                case XML_ERROR_ASYNC_ENTITY: return "asynchronous entity";
                case XML_ERROR_BAD_CHAR_REF: return "reference to invalid character number";
                case XML_ERROR_BINARY_ENTITY_REF: return "reference to binary entity";
                case XML_ERROR_ATTRIBUTE_EXTERNAL_ENTITY_REF: return "reference to external entity in attribute";
                case XML_ERROR_MISPLACED_XML_PI: return "XML or text declaration not at start of entity";
                case XML_ERROR_UNKNOWN_ENCODING: return "unknown encoding";
                case XML_ERROR_INCORRECT_ENCODING: return "encoding specified in XML declaration is incorrect";
                case XML_ERROR_UNCLOSED_CDATA_SECTION: return "unclosed CDATA section";
                case XML_ERROR_EXTERNAL_ENTITY_HANDLING: return "error in processing external entity reference";
                case XML_ERROR_NOT_STANDALONE: return "document is not standalone";
                case XML_ERROR_UNEXPECTED_STATE: return "unexpected parser state - please send a bug report";
                case XML_ERROR_ENTITY_DECLARED_IN_PE: return "entity declared in parameter entity";
                case XML_ERROR_FEATURE_REQUIRES_XML_DTD: return "requested feature requires XML_DTD support in Expat";
                case XML_ERROR_CANT_CHANGE_FEATURE_ONCE_PARSING: return "cannot change setting once parsing has begun";
                /* Added in 1.95.7. */
                case XML_ERROR_UNBOUND_PREFIX: return "unbound prefix";
                /* Added in 1.95.8. */
                case XML_ERROR_UNDECLARING_PREFIX: return "must not undeclare prefix";
                case XML_ERROR_INCOMPLETE_PE: return "incomplete markup in parameter entity";
                case XML_ERROR_XML_DECL: return "XML declaration not well-formed";
                case XML_ERROR_TEXT_DECL: return "text declaration not well-formed";
                case XML_ERROR_PUBLICID: return "illegal character(s) in public id";
                case XML_ERROR_SUSPENDED: return "parser suspended";
                case XML_ERROR_NOT_SUSPENDED: return "parser not suspended";
                case XML_ERROR_ABORTED: return "parsing aborted";
                case XML_ERROR_FINISHED: return "parsing finished";
                case XML_ERROR_SUSPEND_PE: return "cannot suspend in external parameter entity";
                /* Added in 2.0.0. */
                case XML_ERROR_RESERVED_PREFIX_XML: return "reserved prefix (xml) must not be undeclared or bound to another namespace name";
                case XML_ERROR_RESERVED_PREFIX_XMLNS: return "reserved prefix (xmlns) must not be declared or undeclared";
                case XML_ERROR_RESERVED_NAMESPACE_URI: return "prefix must not be bound to one of the reserved namespace names";
                /* Added in 2.2.5. */
                case XML_ERROR_INVALID_ARGUMENT: return "invalid argument";
                /* Added in 2.3.0. */
                case XML_ERROR_NO_BUFFER: return "a successful prior call to function XML_GetBuffer is required";
                /* Added in 2.4.0. */
                case XML_ERROR_AMPLIFICATION_LIMIT_BREACH: return "limit on input amplification factor (from DTD and entities) breached";
                /* Added in 2.6.4. */
                case XML_ERROR_NOT_STARTED: return "parser not started";
            }
            return null;
        }

        /// <summary>XML_ExpatVersion</summary>
        internal static string XML_ExpatVersion() => $"expat_{XML_MAJOR_VERSION}.{XML_MINOR_VERSION}.{XML_MICRO_VERSION}";

        /// <summary>XML_GetFeatureList</summary>
        internal static XML_Feature[] XML_GetFeatureList() => new[]
        {
            new XML_Feature { name = "sizeof(XML_Char)", value = 1 },
            new XML_Feature { name = "sizeof(XML_LChar)", value = 1 },
            new XML_Feature { name = "XML_DTD", value = 0 },
            new XML_Feature { name = "XML_CONTEXT_BYTES", value = XML_CONTEXT_BYTES },
            new XML_Feature { name = "XML_NS", value = 0 },
            new XML_Feature { name = "XML_BLAP_MAX_AMP", value = (long)EXPAT_BILLION_LAUGHS_ATTACK_PROTECTION_MAXIMUM_AMPLIFICATION_DEFAULT },
            new XML_Feature { name = "XML_BLAP_ACT_THRES", value = (long)EXPAT_BILLION_LAUGHS_ATTACK_PROTECTION_ACTIVATION_THRESHOLD_DEFAULT },
            new XML_Feature { name = "XML_GE", value = 0 },
            new XML_Feature { name = "XML_AT_MAX_AMP", value = (long)EXPAT_ALLOC_TRACKER_MAXIMUM_AMPLIFICATION_DEFAULT },
            new XML_Feature { name = "XML_AT_ACT_THRES", value = (long)EXPAT_ALLOC_TRACKER_ACTIVATION_THRESHOLD_DEFAULT },
        };

        /// <summary>XML_SetBillionLaughsAttackProtectionMaximumAmplification</summary>
        internal static bool XML_SetBillionLaughsAttackProtectionMaximumAmplification(XML_Parser parser, float maximumAmplificationFactor)
        {
            if (parser == null || parser.m_parentParser != null || float.IsNaN(maximumAmplificationFactor)
                || maximumAmplificationFactor < 1.0f)
                return false;
            parser.m_accounting.maximumAmplificationFactor = maximumAmplificationFactor;
            return true;
        }

        /// <summary>XML_SetBillionLaughsAttackProtectionActivationThreshold</summary>
        internal static bool XML_SetBillionLaughsAttackProtectionActivationThreshold(XML_Parser parser, ulong activationThresholdBytes)
        {
            if (parser == null || parser.m_parentParser != null)
                return false;
            parser.m_accounting.activationThresholdBytes = activationThresholdBytes;
            return true;
        }

        /// <summary>XML_SetAllocTrackerMaximumAmplification</summary>
        internal static bool XML_SetAllocTrackerMaximumAmplification(XML_Parser parser, float maximumAmplificationFactor)
        {
            if (parser == null || parser.m_parentParser != null || float.IsNaN(maximumAmplificationFactor)
                || maximumAmplificationFactor < 1.0f)
                return false;
            parser.m_alloc_tracker.maximumAmplificationFactor = maximumAmplificationFactor;
            return true;
        }

        /// <summary>XML_SetAllocTrackerActivationThreshold</summary>
        internal static bool XML_SetAllocTrackerActivationThreshold(XML_Parser parser, ulong activationThresholdBytes)
        {
            if (parser == null || parser.m_parentParser != null)
                return false;
            parser.m_alloc_tracker.activationThresholdBytes = activationThresholdBytes;
            return true;
        }

        /// <summary>XML_SetReparseDeferralEnabled</summary>
        internal static bool XML_SetReparseDeferralEnabled(XML_Parser parser, bool enabled)
        {
            if (parser != null)
            {
                parser.m_reparseDeferralEnabled = enabled;
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ hash tables

        /// <summary>keyeq</summary>
        private static bool keyeq(XCHARP s1, int s1len, XCHARP s2)
        {
            int k = 0;
            for (; s1len > 0 && s1[k] == s2[k]; s1len--, k++)
                ;
            return s1len == 0 && s2[k] == 0;
        }

        /// <summary>keylen</summary>
        private static int keylen(XCHARP s) => s.Length;

        /// <summary>copy_salt_to_sipkey</summary>
        private static sipkey copy_salt_to_sipkey(XML_Parser parser)
        {
            XML_Parser rootParser = getRootParserOf(parser, out _);
            return rootParser.m_hash_secret_salt_128;
        }

        /// <summary>hash</summary>
        private static ulong hash(XML_Parser parser, XCHARP s, int keyLen)
        {
            var state = new siphash(copy_salt_to_sipkey(parser));
            state.update(s.a, s.i, keyLen);
            return state.final();
        }

        // SECOND_HASH / PROBE_STEP
        private static byte PROBE_STEP(ulong hash, ulong mask, byte power) =>
            (byte)((((hash & ~mask) >> (power - 1)) & (mask >> 2)) | 1);

        /// <summary>lookupWithLength: create builds the entry (createSize != 0 in C).</summary>
        private static T lookupWithLength<T>(XML_Parser parser, HASH_TABLE table, XCHARP name, int nameLen, bool create)
            where T : NAMED, new()
        {
            long i;
            if (table.size == 0)
            {
                long tsize;
                if (!create)
                    return null;
                table.power = INIT_POWER;
                /* table->size is a power of 2 */
                table.size = 1L << INIT_POWER;
                tsize = table.size * SIZEOF_POINTER;
                if (!MALLOC(table.parser, tsize))
                {
                    table.size = 0;
                    return null;
                }
                table.v = new NAMED[table.size];
                i = (long)(hash(parser, name, nameLen) & ((ulong)table.size - 1));
            }
            else
            {
                ulong h = hash(parser, name, nameLen);
                ulong mask = (ulong)table.size - 1;
                byte step = 0;
                i = (long)(h & mask);
                while (table.v[i] != null)
                {
                    if (keyeq(name, nameLen, table.v[i].name))
                        return (T)table.v[i];
                    if (step == 0)
                        step = PROBE_STEP(h, mask, table.power);
                    if (i < step)
                        i += table.size - step;
                    else
                        i -= step;
                }
                if (!create)
                    return null;

                /* check for overflow (table is half full) */
                if ((table.used >> (table.power - 1)) != 0)
                {
                    byte newPower = (byte)(table.power + 1);
                    long newSize = 1L << newPower;
                    ulong newMask = (ulong)newSize - 1;
                    long tsize = newSize * SIZEOF_POINTER;
                    if (!MALLOC(table.parser, tsize))
                        return null;
                    var newV = new NAMED[newSize];
                    for (i = 0; i < table.size; i++)
                        if (table.v[i] != null)
                        {
                            XCHARP key = table.v[i].name;
                            ulong newHash = hash(parser, key, keylen(key));
                            long j = (long)(newHash & newMask);
                            step = 0;
                            while (newV[j] != null)
                            {
                                if (step == 0)
                                    step = PROBE_STEP(newHash, newMask, newPower);
                                if (j < step)
                                    j += newSize - step;
                                else
                                    j -= step;
                            }
                            newV[j] = table.v[i];
                        }
                    FREE(table.parser, table.size * SIZEOF_POINTER);
                    table.v = newV;
                    table.power = newPower;
                    table.size = newSize;
                    i = (long)(h & newMask);
                    step = 0;
                    while (table.v[i] != null)
                    {
                        if (step == 0)
                            step = PROBE_STEP(h, newMask, newPower);
                        if (i < step)
                            i += newSize - step;
                        else
                            i -= step;
                    }
                }
            }
            var entry = new T();
            if (!MALLOC(table.parser, entry.CSize))
                return null;
            entry.name = name;
            table.v[i] = entry;
            table.used++;
            return entry;
        }

        /// <summary>lookup</summary>
        private static T lookup<T>(XML_Parser parser, HASH_TABLE table, XCHARP name, bool create) where T : NAMED, new() =>
            lookupWithLength<T>(parser, table, name, keylen(name), create);

        /// <summary>hashTableClear</summary>
        private static void hashTableClear(HASH_TABLE table)
        {
            for (long i = 0; i < table.size; i++)
            {
                if (table.v[i] != null)
                    FREE(table.parser, table.v[i].CSize);
                table.v[i] = null;
            }
            table.used = 0;
        }

        /// <summary>hashTableDestroy</summary>
        private static void hashTableDestroy(HASH_TABLE table)
        {
            for (long i = 0; i < table.size; i++)
                if (table.v[i] != null)
                    FREE(table.parser, table.v[i].CSize);
            if (table.v != null)
                FREE(table.parser, table.size * SIZEOF_POINTER);
            table.v = null;
            table.size = 0;
            table.used = 0;
        }

        /// <summary>hashTableInit</summary>
        private static void hashTableInit(HASH_TABLE p, XML_Parser parser)
        {
            p.power = 0;
            p.size = 0;
            p.used = 0;
            p.v = null;
            p.parser = parser;
        }

        /// <summary>hashTableIterInit / hashTableIterNext, as an enumeration of the slots in order.</summary>
        private static System.Collections.Generic.IEnumerable<T> hashTableIter<T>(HASH_TABLE table) where T : NAMED
        {
            if (table.v == null)
                yield break;
            var v = table.v;
            for (long i = 0; i < v.Length; i++)
                if (v[i] != null)
                    yield return (T)v[i];
        }

        // ------------------------------------------------------------------ string pools

        // poolStart, poolLength, poolChop, poolLastChar, poolDiscard, poolFinish
        private static XCHARP poolStart(STRING_POOL pool) => pool.cur == null ? XCHARP.NULL : new XCHARP(pool.cur, pool.start);
        private static int poolLength(STRING_POOL pool) => pool.ptr - pool.start;
        private static void poolChop(STRING_POOL pool) => --pool.ptr;
        private static byte poolLastChar(STRING_POOL pool) => pool.cur[pool.ptr - 1];
        private static void poolDiscard(STRING_POOL pool) => pool.ptr = pool.start;
        private static void poolFinish(STRING_POOL pool) => pool.start = pool.ptr;

        /// <summary>poolAppendChar</summary>
        private static bool poolAppendChar(STRING_POOL pool, byte c)
        {
            if (pool.ptr == pool.end && !poolGrow(pool))
                return false;
            pool.cur[pool.ptr++] = c;
            return true;
        }

        /// <summary>poolAppendChars</summary>
        private static bool poolAppendChars(STRING_POOL pool, byte[] s, int sOff, int len)
        {
            if (!poolGrowUntil(pool, len))
                return false;
            Buffer.BlockCopy(s, sOff, pool.cur, pool.ptr, len);
            pool.ptr += len;
            return true;
        }

        private static bool poolAppendChars(STRING_POOL pool, XCHARP s, int len) => poolAppendChars(pool, s.a, s.i, len);

        /// <summary>poolInit</summary>
        private static void poolInit(STRING_POOL pool, XML_Parser parser)
        {
            pool.blocks = null;
            pool.freeBlocks = null;
            pool.cur = null;
            pool.start = 0;
            pool.ptr = 0;
            pool.end = 0;
            pool.parser = parser;
        }

        /// <summary>poolClear</summary>
        private static void poolClear(STRING_POOL pool)
        {
            if (pool.freeBlocks == null)
                pool.freeBlocks = pool.blocks;
            else
            {
                BLOCK p = pool.blocks;
                while (p != null)
                {
                    BLOCK tem = p.next;
                    p.next = pool.freeBlocks;
                    pool.freeBlocks = p;
                    p = tem;
                }
            }
            pool.blocks = null;
            pool.cur = null;
            pool.start = 0;
            pool.ptr = 0;
            pool.end = 0;
        }

        /// <summary>poolDestroy</summary>
        private static void poolDestroy(STRING_POOL pool)
        {
            BLOCK p = pool.blocks;
            while (p != null)
            {
                BLOCK tem = p.next;
                FREE(pool.parser, BLOCK_HEADER + p.size);
                p = tem;
            }
            p = pool.freeBlocks;
            while (p != null)
            {
                BLOCK tem = p.next;
                FREE(pool.parser, BLOCK_HEADER + p.size);
                p = tem;
            }
            pool.blocks = pool.freeBlocks = null;
            pool.cur = null;
            pool.start = pool.ptr = pool.end = 0;
        }

        /// <summary>poolAppend</summary>
        private static XCHARP poolAppend(STRING_POOL pool, ENCODING enc, byte[] buf, int ptr, int end)
        {
            if (pool.cur == null && !poolGrow(pool))
                return XCHARP.NULL;
            for (; ; )
            {
                XML_Convert_Result convert_res = XmlConvert(enc, buf, ref ptr, end, pool.cur, ref pool.ptr, pool.end);
                if (convert_res == XML_Convert_Result.XML_CONVERT_COMPLETED
                    || convert_res == XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE)
                    break;
                if (!poolGrow(pool))
                    return XCHARP.NULL;
            }
            return poolStart(pool);
        }

        /// <summary>poolCopyString</summary>
        private static XCHARP poolCopyString(STRING_POOL pool, XCHARP s)
        {
            if (!poolAppendChars(pool, s, s.Length + 1))
                return XCHARP.NULL;
            s = poolStart(pool);
            poolFinish(pool);
            return s;
        }

        /// <summary>poolCopyStringNoFinish</summary>
        private static XCHARP poolCopyStringNoFinish(STRING_POOL pool, XCHARP s)
        {
            int k = 0;
            do
            {
                if (!poolAppendChar(pool, s[k]))
                {
                    if (k > 0)
                        pool.ptr -= k;
                    return XCHARP.NULL;
                }
            } while (s[k++] != 0);
            return poolStart(pool);
        }

        /// <summary>poolCopyStringN</summary>
        private static XCHARP poolCopyStringN(STRING_POOL pool, XCHARP s, int n)
        {
            if (pool.cur == null && !poolGrow(pool))
                return XCHARP.NULL;
            if (n > 0 && !poolAppendChars(pool, s, n))
                return XCHARP.NULL;
            s = poolStart(pool);
            poolFinish(pool);
            return s;
        }

        /// <summary>poolAppendString</summary>
        private static XCHARP poolAppendString(STRING_POOL pool, XCHARP s)
        {
            if (!poolAppendChars(pool, s, s.Length))
                return XCHARP.NULL;
            return poolStart(pool);
        }

        /// <summary>poolStoreString</summary>
        private static XCHARP poolStoreString(STRING_POOL pool, ENCODING enc, byte[] buf, int ptr, int end)
        {
            if (poolAppend(pool, enc, buf, ptr, end).IsNull)
                return XCHARP.NULL;
            if (!poolAppendChar(pool, 0))
                return XCHARP.NULL;
            return poolStart(pool);
        }

        /// <summary>poolBytesToAllocateFor</summary>
        private static long poolBytesToAllocateFor(int blockSize)
        {
            if (blockSize <= 0)
                return 0;
            return BLOCK_HEADER + (long)blockSize;
        }

        /// <summary>poolGrow</summary>
        private static bool poolGrow(STRING_POOL pool)
        {
            if (pool.freeBlocks != null)
            {
                if (pool.cur == null)
                {
                    pool.blocks = pool.freeBlocks;
                    pool.freeBlocks = pool.freeBlocks.next;
                    pool.blocks.next = null;
                    pool.cur = pool.blocks.s;
                    pool.start = 0;
                    pool.end = pool.blocks.size;
                    pool.ptr = 0;
                    return true;
                }
                if (pool.end - pool.start < pool.freeBlocks.size)
                {
                    BLOCK tem = pool.freeBlocks.next;
                    pool.freeBlocks.next = pool.blocks;
                    pool.blocks = pool.freeBlocks;
                    pool.freeBlocks = tem;
                    Buffer.BlockCopy(pool.cur, pool.start, pool.blocks.s, 0, pool.end - pool.start);
                    pool.ptr = pool.ptr - pool.start;
                    pool.cur = pool.blocks.s;
                    pool.start = 0;
                    pool.end = pool.blocks.size;
                    return true;
                }
            }
            if (pool.blocks != null && pool.cur == pool.blocks.s && pool.start == 0)
            {
                int blockSize = (int)((uint)(pool.end - pool.start) * 2U);
                long bytesToAllocate;
                int offsetInsideBlock = pool.ptr - pool.start;

                if (blockSize < 0)
                    return false;
                bytesToAllocate = poolBytesToAllocateFor(blockSize);
                if (bytesToAllocate == 0 || blockSize > Array.MaxLength)
                    return false;
                if (!REALLOC(pool.parser, BLOCK_HEADER + pool.blocks.size, bytesToAllocate))
                    return false;
                var temp = new byte[blockSize];
                Buffer.BlockCopy(pool.blocks.s, 0, temp, 0, pool.blocks.size);
                pool.blocks.s = temp;
                pool.blocks.size = blockSize;
                pool.cur = temp;
                pool.ptr = offsetInsideBlock;
                pool.start = 0;
                pool.end = blockSize;
            }
            else
            {
                BLOCK tem;
                int blockSize = pool.cur == null ? 0 : pool.end - pool.start;
                long bytesToAllocate;

                if (blockSize < 0)
                    return false;

                if (blockSize < INIT_BLOCK_SIZE)
                    blockSize = INIT_BLOCK_SIZE;
                else
                {
                    /* Detect overflow, avoiding _signed_ overflow undefined behavior */
                    if ((int)((uint)blockSize * 2U) < 0)
                        return false;
                    blockSize *= 2;
                }

                bytesToAllocate = poolBytesToAllocateFor(blockSize);
                if (bytesToAllocate == 0 || blockSize > Array.MaxLength)
                    return false;

                if (!MALLOC(pool.parser, bytesToAllocate))
                    return false;
                tem = new BLOCK { size = blockSize, s = new byte[blockSize] };
                tem.next = pool.blocks;
                pool.blocks = tem;
                int used = pool.cur == null ? 0 : pool.ptr - pool.start;
                if (used != 0)
                    Buffer.BlockCopy(pool.cur, pool.start, tem.s, 0, used);
                pool.cur = tem.s;
                pool.ptr = used;
                pool.start = 0;
                pool.end = blockSize;
            }
            return true;
        }

        /// <summary>poolGrowUntil</summary>
        private static bool poolGrowUntil(STRING_POOL pool, int needed)
        {
            for (; ; )
            {
                int available = pool.cur == null ? 0 : pool.end - pool.ptr;
                if (available >= needed)
                    return true;
                if (!poolGrow(pool))
                    return false;
            }
        }

        /// <summary>copyString</summary>
        private static XCHARP copyString(XCHARP s, XML_Parser parser)
        {
            int charsRequired = s.Length + 1;
            if (!MALLOC(parser, charsRequired))
                return XCHARP.NULL;
            var result = new byte[charsRequired];
            Buffer.BlockCopy(s.a, s.i, result, 0, charsRequired);
            return new XCHARP(result, 0);
        }

        // ------------------------------------------------------------------ accounting

        /// <summary>accountingGetCurrentAmplification</summary>
        private static float accountingGetCurrentAmplification(XML_Parser rootParser)
        {
            const int lenOfShortestInclude = 22; // sizeof("<!ENTITY a SYSTEM 'b'>") - 1
            ulong countBytesOutput = rootParser.m_accounting.countBytesDirect + rootParser.m_accounting.countBytesIndirect;
            float amplificationFactor = rootParser.m_accounting.countBytesDirect != 0
                ? (float)countBytesOutput / (float)rootParser.m_accounting.countBytesDirect
                : (float)(lenOfShortestInclude + rootParser.m_accounting.countBytesIndirect) / (float)lenOfShortestInclude;
            return amplificationFactor;
        }

        /// <summary>accountingReportStats</summary>
        private static void accountingReportStats(XML_Parser originParser, string epilog)
        {
            XML_Parser rootParser = getRootParserOf(originParser, out _);
            if (rootParser.m_accounting.debugLevel == 0u)
                return;
            float amplificationFactor = accountingGetCurrentAmplification(rootParser);
            Console.Error.Write(
                $"expat: Accounting({rootParser.GetHashCode():x}): Direct {rootParser.m_accounting.countBytesDirect,10}, indirect {rootParser.m_accounting.countBytesIndirect,10}, amplification {amplificationFactor,8:F2}{epilog}");
        }

        /// <summary>accountingOnAbort</summary>
        private static void accountingOnAbort(XML_Parser originParser) => accountingReportStats(originParser, " ABORTING\n");

        /// <summary>accountingDiffTolerated: bytesMore = after - before.</summary>
        private static bool accountingDiffTolerated(XML_Parser originParser, int tok, int before, int after, XML_Account account)
        {
            /* Note: We need to check the token type *first* to be sure that
             *       we can even access variable <after>, safely.
             *       E.g. for XML_TOK_NONE <after> may hold an invalid pointer. */
            switch (tok)
            {
                case XML_TOK_INVALID:
                case XML_TOK_PARTIAL:
                case XML_TOK_PARTIAL_CHAR:
                case XML_TOK_NONE:
                    return true;
            }

            if (account == XML_Account.XML_ACCOUNT_NONE)
                return true; /* because these bytes have been accounted for, already */

            XML_Parser rootParser = getRootParserOf(originParser, out _);
            bool isDirect = account == XML_Account.XML_ACCOUNT_DIRECT && originParser == rootParser;
            long bytesMore = after - before;

            if (isDirect)
            {
                /* Detect and avoid integer overflow */
                if (rootParser.m_accounting.countBytesDirect > ulong.MaxValue - (ulong)bytesMore)
                    return false;
                rootParser.m_accounting.countBytesDirect += (ulong)bytesMore;
            }
            else
            {
                if (rootParser.m_accounting.countBytesIndirect > ulong.MaxValue - (ulong)bytesMore)
                    return false;
                rootParser.m_accounting.countBytesIndirect += (ulong)bytesMore;
            }

            ulong countBytesOutput = rootParser.m_accounting.countBytesDirect + rootParser.m_accounting.countBytesIndirect;
            float amplificationFactor = accountingGetCurrentAmplification(rootParser);
            bool tolerated = (countBytesOutput < rootParser.m_accounting.activationThresholdBytes)
                             || (amplificationFactor <= rootParser.m_accounting.maximumAmplificationFactor);

            if (rootParser.m_accounting.debugLevel >= 2u)
                accountingReportStats(rootParser, "\n");

            return tolerated;
        }

        /// <summary>entityTrackingReportStats</summary>
        private static void entityTrackingReportStats(XML_Parser rootParser, ENTITY entity, string action)
        {
            if (rootParser.m_entity_stats.debugLevel == 0u)
                return;
            string entityName = System.Text.Encoding.UTF8.GetString(entity.name.ToArray());
            Console.Error.WriteLine(
                $"expat: Entities({rootParser.GetHashCode():x}): Count {rootParser.m_entity_stats.countEverOpened,9}, depth {rootParser.m_entity_stats.currentDepth,2}/{rootParser.m_entity_stats.maximumDepthSeen,2} {(entity.is_param ? "%" : "&")}{entityName}; {action} length {entity.textLen}");
        }

        /// <summary>entityTrackingOnOpen</summary>
        private static void entityTrackingOnOpen(XML_Parser originParser, ENTITY entity)
        {
            XML_Parser rootParser = getRootParserOf(originParser, out _);
            rootParser.m_entity_stats.countEverOpened++;
            rootParser.m_entity_stats.currentDepth++;
            if (rootParser.m_entity_stats.currentDepth > rootParser.m_entity_stats.maximumDepthSeen)
                rootParser.m_entity_stats.maximumDepthSeen++;
            entityTrackingReportStats(rootParser, entity, "OPEN ");
        }

        /// <summary>entityTrackingOnClose</summary>
        private static void entityTrackingOnClose(XML_Parser originParser, ENTITY entity)
        {
            XML_Parser rootParser = getRootParserOf(originParser, out _);
            entityTrackingReportStats(rootParser, entity, "CLOSE");
            rootParser.m_entity_stats.currentDepth--;
        }

        /// <summary>getRootParserOf</summary>
        private static XML_Parser getRootParserOf(XML_Parser parser, out uint outLevelDiff)
        {
            XML_Parser rootParser = parser;
            uint stepsTakenUpwards = 0;
            while (rootParser.m_parentParser != null)
            {
                rootParser = rootParser.m_parentParser;
                stepsTakenUpwards++;
            }
            outLevelDiff = stepsTakenUpwards;
            return rootParser;
        }

        /// <summary>getDebugLevel</summary>
        private static ulong getDebugLevel(string variableName, ulong defaultDebugLevel)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
            if (value == null)
                return defaultDebugLevel;
            if (!ulong.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out ulong debugLevel))
                return defaultDebugLevel;
            return debugLevel;
        }
    }
}
