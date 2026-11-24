using System;
using static SharpPy.Modules.Expat.XmlTok;
using static SharpPy.Modules.Expat.XmlRole;
using static SharpPy.Modules.Expat.XML_Error;
using static SharpPy.Modules.Expat.XML_Parsing;
using XML_Parser = SharpPy.Modules.Expat.XML_ParserStruct;

namespace SharpPy.Modules.Expat
{
    /// <summary>xmlparse.c: the prolog and DTD, entities, attribute and entity values, the DTD tables.</summary>
    internal static partial class XmlParse
    {
        private static readonly XCHARP externalSubsetName = XCHARP.Literal("#");
        private static readonly XCHARP atypeCDATA = XCHARP.Literal("CDATA");
        private static readonly XCHARP atypeID = XCHARP.Literal("ID");
        private static readonly XCHARP atypeIDREF = XCHARP.Literal("IDREF");
        private static readonly XCHARP atypeIDREFS = XCHARP.Literal("IDREFS");
        private static readonly XCHARP atypeENTITY = XCHARP.Literal("ENTITY");
        private static readonly XCHARP atypeENTITIES = XCHARP.Literal("ENTITIES");
        private static readonly XCHARP atypeNMTOKEN = XCHARP.Literal("NMTOKEN");
        private static readonly XCHARP atypeNMTOKENS = XCHARP.Literal("NMTOKENS");
        private static readonly XCHARP notationPrefix = XCHARP.Literal("NOTATION(");
        private static readonly XCHARP enumValueSep = XCHARP.Literal("|");
        private static readonly XCHARP enumValueStart = XCHARP.Literal("(");

        // labels of doProlog (elementContent:, closeGroup:) as case values no role has
        private const int ELEMENT_CONTENT = 1000, CLOSE_GROUP = 1001;

        /// <summary>doProlog</summary>
        private static XML_Error doProlog(XML_Parser parser, ENCODING enc, byte[] buf, int s, int end, int tok, int next,
                                          ref int nextPtr, bool haveMore, bool allowClosingDoctype, XML_Account account)
        {
            /* save one level of indirection */
            DTD dtd = parser.m_dtd;

            ref int eventPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventPtr
                : ref parser.m_openInternalEntities.internalEventPtr);
            ref int eventEndPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventEndPtr
                : ref parser.m_openInternalEntities.internalEventEndPtr);
            XML_Content_Quant quant = XML_Content_Quant.XML_CQUANT_NONE;

            for (; ; )
            {
                int role;
                bool handleDefault = true;
                bool alreadyChecked = false;
                eventPP = s;
                eventEndPP = next;
                if (tok <= 0)
                {
                    if (haveMore && tok != XML_TOK_INVALID)
                    {
                        nextPtr = s;
                        return XML_ERROR_NONE;
                    }
                    switch (tok)
                    {
                        case XML_TOK_INVALID:
                            eventPP = next;
                            return XML_ERROR_INVALID_TOKEN;
                        case XML_TOK_PARTIAL:
                            return XML_ERROR_UNCLOSED_TOKEN;
                        case XML_TOK_PARTIAL_CHAR:
                            return XML_ERROR_PARTIAL_CHAR;
                        case -XML_TOK_PROLOG_S:
                            tok = -tok;
                            break;
                        case XML_TOK_NONE:
                            /* for internal PE NOT referenced between declarations */
                            if (enc != parser.m_encoding && !parser.m_openInternalEntities.betweenDecl)
                            {
                                nextPtr = s;
                                return XML_ERROR_NONE;
                            }
                            /* WFC: PE Between Declarations - must check that PE contains
                               complete markup, not only for external PEs, but also for
                               internal PEs if the reference occurs between declarations.
                            */
                            if (parser.m_isParamEntity || enc != parser.m_encoding)
                            {
                                if (parser.m_prologState.XmlTokenRole(XML_TOK_NONE, buf, end, end, enc) == XML_ROLE_ERROR)
                                    return XML_ERROR_INCOMPLETE_PE;
                                nextPtr = s;
                                return XML_ERROR_NONE;
                            }
                            return XML_ERROR_NO_ELEMENTS;
                        default:
                            tok = -tok;
                            next = end;
                            break;
                    }
                }
                role = parser.m_prologState.XmlTokenRole(tok, buf, s, next, enc);
                switch (role)
                {
                    case XML_ROLE_INSTANCE_START: // bytes accounted in contentProcessor
                    case XML_ROLE_XML_DECL:       // bytes accounted in processXmlDecl
                    case XML_ROLE_TEXT_DECL:      // bytes accounted in processXmlDecl
                        break;
                    default:
                        if (!accountingDiffTolerated(parser, tok, s, next, account))
                        {
                            accountingOnAbort(parser);
                            return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                        }
                        break;
                }
                switch (role)
                {
                    case XML_ROLE_XML_DECL:
                        {
                            XML_Error result = processXmlDecl(parser, false, s, next);
                            if (result != XML_ERROR_NONE)
                                return result;
                            enc = parser.m_encoding;
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_DOCTYPE_NAME:
                        if (parser.m_startDoctypeDeclHandler != null)
                        {
                            parser.m_doctypeName = poolStoreString(parser.m_tempPool, enc, buf, s, next);
                            if (parser.m_doctypeName.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            poolFinish(parser.m_tempPool);
                            parser.m_doctypePubid = XCHARP.NULL;
                            handleDefault = false;
                        }
                        parser.m_doctypeSysid = XCHARP.NULL; /* always initialize to NULL */
                        break;
                    case XML_ROLE_DOCTYPE_INTERNAL_SUBSET:
                        if (parser.m_startDoctypeDeclHandler != null)
                        {
                            beforeHandler(parser);
                            try
                            {
                                parser.m_startDoctypeDeclHandler(parser.m_handlerArg, parser.m_doctypeName,
                                                                 parser.m_doctypeSysid, parser.m_doctypePubid, 1);
                            }
                            finally { afterHandler(parser); }
                            parser.m_doctypeName = XCHARP.NULL;
                            poolClear(parser.m_tempPool);
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_TEXT_DECL:
                        {
                            XML_Error result = processXmlDecl(parser, true, s, next);
                            if (result != XML_ERROR_NONE)
                                return result;
                            enc = parser.m_encoding;
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_DOCTYPE_PUBLIC_ID:
                        parser.m_useForeignDTD = false;
                        parser.m_declEntity = lookup<ENTITY>(parser, dtd.paramEntities, externalSubsetName, true);
                        if (parser.m_declEntity == null)
                            return XML_ERROR_NO_MEMORY;
                        dtd.hasParamEntityRefs = true;
                        if (parser.m_startDoctypeDeclHandler != null)
                        {
                            XCHARP pubId;
                            if (!enc.IsPublicId(buf, s, next, ref eventPP))
                                return XML_ERROR_PUBLICID;
                            pubId = poolStoreString(parser.m_tempPool, enc, buf, s + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (pubId.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            normalizePublicId(pubId);
                            poolFinish(parser.m_tempPool);
                            parser.m_doctypePubid = pubId;
                            handleDefault = false;
                            alreadyChecked = true;
                        }
                        goto case XML_ROLE_ENTITY_PUBLIC_ID;
                    case XML_ROLE_ENTITY_PUBLIC_ID:
                        if (!alreadyChecked && !enc.IsPublicId(buf, s, next, ref eventPP))
                            return XML_ERROR_PUBLICID;
                        // alreadyChecked:
                        if (dtd.keepProcessing && parser.m_declEntity != null)
                        {
                            XCHARP tem = poolStoreString(dtd.pool, enc, buf, s + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (tem.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            normalizePublicId(tem);
                            parser.m_declEntity.publicId = tem;
                            poolFinish(dtd.pool);
                            /* Don't suppress the default handler if we fell through from
                             * the XML_ROLE_DOCTYPE_PUBLIC_ID case.
                             */
                            if (parser.m_entityDeclHandler != null && role == XML_ROLE_ENTITY_PUBLIC_ID)
                                handleDefault = false;
                        }
                        break;
                    case XML_ROLE_DOCTYPE_CLOSE:
                        if (allowClosingDoctype != true)
                        {
                            /* Must not close doctype context */
                            return XML_ERROR_INVALID_TOKEN;
                        }
                        if (!parser.m_doctypeName.IsNull)
                        {
                            beforeHandler(parser);
                            try
                            {
                                parser.m_startDoctypeDeclHandler(parser.m_handlerArg, parser.m_doctypeName,
                                                                 parser.m_doctypeSysid, parser.m_doctypePubid, 0);
                            }
                            finally { afterHandler(parser); }
                            poolClear(parser.m_tempPool);
                            handleDefault = false;
                        }
                        /* parser->m_doctypeSysid will be non-NULL in the case of a previous
                           XML_ROLE_DOCTYPE_SYSTEM_ID, even if parser->m_startDoctypeDeclHandler
                           was not set, indicating an external subset
                        */
                        if (!parser.m_doctypeSysid.IsNull || parser.m_useForeignDTD)
                        {
                            bool hadParamEntityRefs = dtd.hasParamEntityRefs;
                            dtd.hasParamEntityRefs = true;
                            if (parser.m_paramEntityParsing != 0 && parser.m_externalEntityRefHandler != null)
                            {
                                ENTITY entity = lookup<ENTITY>(parser, dtd.paramEntities, externalSubsetName, true);
                                if (entity == null)
                                {
                                    /* The external subset name "#" will have already been
                                     * inserted into the hash table at the start of the
                                     * external entity parsing, so no allocation will happen
                                     * and lookup() cannot fail.
                                     */
                                    return XML_ERROR_NO_MEMORY; /* LCOV_EXCL_LINE */
                                }
                                if (parser.m_useForeignDTD)
                                    entity.@base = parser.m_curBase;
                                dtd.paramEntityRead = false;
                                int status;
                                beforeHandler(parser);
                                try
                                {
                                    status = parser.m_externalEntityRefHandler(parser.m_externalEntityRefHandlerArg, XCHARP.NULL,
                                                                               entity.@base, entity.systemId, entity.publicId);
                                }
                                finally { afterHandler(parser); }
                                if (status == 0)
                                    return XML_ERROR_EXTERNAL_ENTITY_HANDLING;
                                if (dtd.paramEntityRead)
                                {
                                    if (!dtd.standalone && parser.m_notStandaloneHandler != null)
                                    {
                                        int handlerStatus;
                                        beforeHandler(parser);
                                        try { handlerStatus = parser.m_notStandaloneHandler(parser.m_handlerArg); }
                                        finally { afterHandler(parser); }
                                        if (handlerStatus == 0)
                                            return XML_ERROR_NOT_STANDALONE;
                                    }
                                }
                                /* if we didn't read the foreign DTD then this means that there
                                   is no external subset and we must reset dtd->hasParamEntityRefs
                                */
                                else if (parser.m_doctypeSysid.IsNull)
                                    dtd.hasParamEntityRefs = hadParamEntityRefs;
                                /* end of DTD - no need to update dtd->keepProcessing */
                            }
                            parser.m_useForeignDTD = false;
                        }
                        if (parser.m_endDoctypeDeclHandler != null)
                        {
                            beforeHandler(parser);
                            try { parser.m_endDoctypeDeclHandler(parser.m_handlerArg); }
                            finally { afterHandler(parser); }
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_INSTANCE_START:
                        /* if there is no DOCTYPE declaration then now is the
                           last chance to read the foreign DTD
                        */
                        if (parser.m_useForeignDTD)
                        {
                            bool hadParamEntityRefs = dtd.hasParamEntityRefs;
                            dtd.hasParamEntityRefs = true;
                            if (parser.m_paramEntityParsing != 0 && parser.m_externalEntityRefHandler != null)
                            {
                                ENTITY entity = lookup<ENTITY>(parser, dtd.paramEntities, externalSubsetName, true);
                                if (entity == null)
                                    return XML_ERROR_NO_MEMORY;
                                entity.@base = parser.m_curBase;
                                dtd.paramEntityRead = false;
                                int status;
                                beforeHandler(parser);
                                try
                                {
                                    status = parser.m_externalEntityRefHandler(parser.m_externalEntityRefHandlerArg, XCHARP.NULL,
                                                                               entity.@base, entity.systemId, entity.publicId);
                                }
                                finally { afterHandler(parser); }
                                if (status == 0)
                                    return XML_ERROR_EXTERNAL_ENTITY_HANDLING;
                                if (dtd.paramEntityRead)
                                {
                                    if (!dtd.standalone && parser.m_notStandaloneHandler != null)
                                    {
                                        int handlerStatus;
                                        beforeHandler(parser);
                                        try { handlerStatus = parser.m_notStandaloneHandler(parser.m_handlerArg); }
                                        finally { afterHandler(parser); }
                                        if (handlerStatus == 0)
                                            return XML_ERROR_NOT_STANDALONE;
                                    }
                                }
                                /* if we didn't read the foreign DTD then this means that there
                                   is no external subset and we must reset dtd->hasParamEntityRefs
                                */
                                else
                                    dtd.hasParamEntityRefs = hadParamEntityRefs;
                                /* end of DTD - no need to update dtd->keepProcessing */
                            }
                        }
                        parser.m_processor = contentProcessor;
                        return contentProcessor(parser, s, end, ref nextPtr);
                    case XML_ROLE_ATTLIST_ELEMENT_NAME:
                        parser.m_declElementType = getElementType(parser, enc, buf, s, next);
                        if (parser.m_declElementType == null)
                            return XML_ERROR_NO_MEMORY;
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ATTRIBUTE_NAME:
                        parser.m_declAttributeId = getAttributeId(parser, enc, buf, s, next);
                        if (parser.m_declAttributeId == null)
                            return XML_ERROR_NO_MEMORY;
                        parser.m_declAttributeIsCdata = false;
                        parser.m_declAttributeType = XCHARP.NULL;
                        parser.m_declAttributeIsId = false;
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ATTRIBUTE_TYPE_CDATA:
                        parser.m_declAttributeIsCdata = true;
                        parser.m_declAttributeType = atypeCDATA;
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ATTRIBUTE_TYPE_ID:
                        parser.m_declAttributeIsId = true;
                        parser.m_declAttributeType = atypeID;
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ATTRIBUTE_TYPE_IDREF:
                    case XML_ROLE_ATTRIBUTE_TYPE_IDREFS:
                    case XML_ROLE_ATTRIBUTE_TYPE_ENTITY:
                    case XML_ROLE_ATTRIBUTE_TYPE_ENTITIES:
                    case XML_ROLE_ATTRIBUTE_TYPE_NMTOKEN:
                    case XML_ROLE_ATTRIBUTE_TYPE_NMTOKENS:
                        parser.m_declAttributeType = role switch
                        {
                            XML_ROLE_ATTRIBUTE_TYPE_IDREF => atypeIDREF,
                            XML_ROLE_ATTRIBUTE_TYPE_IDREFS => atypeIDREFS,
                            XML_ROLE_ATTRIBUTE_TYPE_ENTITY => atypeENTITY,
                            XML_ROLE_ATTRIBUTE_TYPE_ENTITIES => atypeENTITIES,
                            XML_ROLE_ATTRIBUTE_TYPE_NMTOKEN => atypeNMTOKEN,
                            _ => atypeNMTOKENS,
                        };
                        // checkAttListDeclHandler:
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ATTRIBUTE_ENUM_VALUE:
                    case XML_ROLE_ATTRIBUTE_NOTATION_VALUE:
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                        {
                            XCHARP prefix;
                            if (!parser.m_declAttributeType.IsNull)
                                prefix = enumValueSep;
                            else
                                prefix = role == XML_ROLE_ATTRIBUTE_NOTATION_VALUE ? notationPrefix : enumValueStart;
                            if (poolAppendString(parser.m_tempPool, prefix).IsNull)
                                return XML_ERROR_NO_MEMORY;
                            if (poolAppend(parser.m_tempPool, enc, buf, s, next).IsNull)
                                return XML_ERROR_NO_MEMORY;
                            parser.m_declAttributeType = poolStart(parser.m_tempPool);
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_IMPLIED_ATTRIBUTE_VALUE:
                    case XML_ROLE_REQUIRED_ATTRIBUTE_VALUE:
                        if (dtd.keepProcessing)
                        {
                            if (!defineAttribute(parser.m_declElementType, parser.m_declAttributeId,
                                                 parser.m_declAttributeIsCdata, parser.m_declAttributeIsId, XCHARP.NULL, parser))
                                return XML_ERROR_NO_MEMORY;
                            if (parser.m_attlistDeclHandler != null && !parser.m_declAttributeType.IsNull)
                            {
                                if (parser.m_declAttributeType[0] == (byte)'('
                                    || (parser.m_declAttributeType[0] == (byte)'N' && parser.m_declAttributeType[1] == (byte)'O'))
                                {
                                    /* Enumerated or Notation type */
                                    if (!poolAppendChar(parser.m_tempPool, (byte)')') || !poolAppendChar(parser.m_tempPool, 0))
                                        return XML_ERROR_NO_MEMORY;
                                    parser.m_declAttributeType = poolStart(parser.m_tempPool);
                                    poolFinish(parser.m_tempPool);
                                }
                                eventEndPP = s;
                                beforeHandler(parser);
                                try
                                {
                                    parser.m_attlistDeclHandler(parser.m_handlerArg, parser.m_declElementType.name,
                                                                parser.m_declAttributeId.name, parser.m_declAttributeType,
                                                                XCHARP.NULL, role == XML_ROLE_REQUIRED_ATTRIBUTE_VALUE ? 1 : 0);
                                }
                                finally { afterHandler(parser); }
                                handleDefault = false;
                            }
                        }
                        poolClear(parser.m_tempPool);
                        break;
                    case XML_ROLE_DEFAULT_ATTRIBUTE_VALUE:
                    case XML_ROLE_FIXED_ATTRIBUTE_VALUE:
                        if (dtd.keepProcessing)
                        {
                            XCHARP attVal;
                            XML_Error result = storeAttributeValue(parser, enc, parser.m_declAttributeIsCdata, buf,
                                                                   s + enc.minBytesPerChar, next - enc.minBytesPerChar,
                                                                   dtd.pool, XML_Account.XML_ACCOUNT_NONE);
                            if (result != XML_ERROR_NONE)
                                return result;
                            attVal = poolStart(dtd.pool);
                            poolFinish(dtd.pool);
                            /* ID attributes aren't allowed to have a default */
                            if (!defineAttribute(parser.m_declElementType, parser.m_declAttributeId,
                                                 parser.m_declAttributeIsCdata, false, attVal, parser))
                                return XML_ERROR_NO_MEMORY;
                            if (parser.m_attlistDeclHandler != null && !parser.m_declAttributeType.IsNull)
                            {
                                if (parser.m_declAttributeType[0] == (byte)'('
                                    || (parser.m_declAttributeType[0] == (byte)'N' && parser.m_declAttributeType[1] == (byte)'O'))
                                {
                                    /* Enumerated or Notation type */
                                    if (!poolAppendChar(parser.m_tempPool, (byte)')') || !poolAppendChar(parser.m_tempPool, 0))
                                        return XML_ERROR_NO_MEMORY;
                                    parser.m_declAttributeType = poolStart(parser.m_tempPool);
                                    poolFinish(parser.m_tempPool);
                                }
                                eventEndPP = s;
                                beforeHandler(parser);
                                try
                                {
                                    parser.m_attlistDeclHandler(parser.m_handlerArg, parser.m_declElementType.name,
                                                                parser.m_declAttributeId.name, parser.m_declAttributeType,
                                                                attVal, role == XML_ROLE_FIXED_ATTRIBUTE_VALUE ? 1 : 0);
                                }
                                finally { afterHandler(parser); }
                                poolClear(parser.m_tempPool);
                                handleDefault = false;
                            }
                        }
                        break;
                    case XML_ROLE_ENTITY_VALUE:
                        if (dtd.keepProcessing)
                        {
                            // This will store the given replacement text in
                            // parser->m_declEntity->textPtr.
                            XML_Error result = callStoreEntityValue(parser, enc, buf, s + enc.minBytesPerChar,
                                                                    next - enc.minBytesPerChar, XML_Account.XML_ACCOUNT_NONE);
                            if (parser.m_declEntity != null)
                            {
                                parser.m_declEntity.textPtr = poolStart(dtd.entityValuePool);
                                parser.m_declEntity.textLen = poolLength(dtd.entityValuePool);
                                poolFinish(dtd.entityValuePool);
                                if (parser.m_entityDeclHandler != null)
                                {
                                    eventEndPP = s;
                                    beforeHandler(parser);
                                    try
                                    {
                                        parser.m_entityDeclHandler(parser.m_handlerArg, parser.m_declEntity.name,
                                                                   parser.m_declEntity.is_param ? 1 : 0, parser.m_declEntity.textPtr,
                                                                   parser.m_declEntity.textLen, parser.m_curBase, XCHARP.NULL,
                                                                   XCHARP.NULL, XCHARP.NULL);
                                    }
                                    finally { afterHandler(parser); }
                                    handleDefault = false;
                                }
                            }
                            else
                                poolDiscard(dtd.entityValuePool);
                            if (result != XML_ERROR_NONE)
                                return result;
                        }
                        break;
                    case XML_ROLE_DOCTYPE_SYSTEM_ID:
                        parser.m_useForeignDTD = false;
                        dtd.hasParamEntityRefs = true;
                        if (parser.m_startDoctypeDeclHandler != null)
                        {
                            parser.m_doctypeSysid = poolStoreString(parser.m_tempPool, enc, buf, s + enc.minBytesPerChar,
                                                                    next - enc.minBytesPerChar);
                            if (parser.m_doctypeSysid.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            poolFinish(parser.m_tempPool);
                            handleDefault = false;
                        }
                        else
                            /* use externalSubsetName to make parser->m_doctypeSysid non-NULL
                               for the case where no parser->m_startDoctypeDeclHandler is set */
                            parser.m_doctypeSysid = externalSubsetName;
                        if (!dtd.standalone && parser.m_paramEntityParsing == 0 && parser.m_notStandaloneHandler != null)
                        {
                            int status;
                            beforeHandler(parser);
                            try { status = parser.m_notStandaloneHandler(parser.m_handlerArg); }
                            finally { afterHandler(parser); }
                            if (status == 0)
                                return XML_ERROR_NOT_STANDALONE;
                        }
                        if (parser.m_declEntity == null)
                        {
                            parser.m_declEntity = lookup<ENTITY>(parser, dtd.paramEntities, externalSubsetName, true);
                            if (parser.m_declEntity == null)
                                return XML_ERROR_NO_MEMORY;
                            parser.m_declEntity.publicId = XCHARP.NULL;
                        }
                        goto case XML_ROLE_ENTITY_SYSTEM_ID;
                    case XML_ROLE_ENTITY_SYSTEM_ID:
                        if (dtd.keepProcessing && parser.m_declEntity != null)
                        {
                            parser.m_declEntity.systemId = poolStoreString(dtd.pool, enc, buf, s + enc.minBytesPerChar,
                                                                           next - enc.minBytesPerChar);
                            if (parser.m_declEntity.systemId.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            parser.m_declEntity.@base = parser.m_curBase;
                            poolFinish(dtd.pool);
                            /* Don't suppress the default handler if we fell through from
                             * the XML_ROLE_DOCTYPE_SYSTEM_ID case.
                             */
                            if (parser.m_entityDeclHandler != null && role == XML_ROLE_ENTITY_SYSTEM_ID)
                                handleDefault = false;
                        }
                        break;
                    case XML_ROLE_ENTITY_COMPLETE:
                        if (dtd.keepProcessing && parser.m_declEntity != null && parser.m_entityDeclHandler != null)
                        {
                            eventEndPP = s;
                            beforeHandler(parser);
                            try
                            {
                                parser.m_entityDeclHandler(parser.m_handlerArg, parser.m_declEntity.name,
                                                           parser.m_declEntity.is_param ? 1 : 0, XCHARP.NULL, 0,
                                                           parser.m_declEntity.@base, parser.m_declEntity.systemId,
                                                           parser.m_declEntity.publicId, XCHARP.NULL);
                            }
                            finally { afterHandler(parser); }
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_ENTITY_NOTATION_NAME:
                        if (dtd.keepProcessing && parser.m_declEntity != null)
                        {
                            parser.m_declEntity.notation = poolStoreString(dtd.pool, enc, buf, s, next);
                            if (parser.m_declEntity.notation.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            poolFinish(dtd.pool);
                            if (parser.m_unparsedEntityDeclHandler != null)
                            {
                                eventEndPP = s;
                                beforeHandler(parser);
                                try
                                {
                                    parser.m_unparsedEntityDeclHandler(parser.m_handlerArg, parser.m_declEntity.name,
                                                                       parser.m_declEntity.@base, parser.m_declEntity.systemId,
                                                                       parser.m_declEntity.publicId, parser.m_declEntity.notation);
                                }
                                finally { afterHandler(parser); }
                                handleDefault = false;
                            }
                            else if (parser.m_entityDeclHandler != null)
                            {
                                eventEndPP = s;
                                beforeHandler(parser);
                                try
                                {
                                    parser.m_entityDeclHandler(parser.m_handlerArg, parser.m_declEntity.name, 0, XCHARP.NULL, 0,
                                                               parser.m_declEntity.@base, parser.m_declEntity.systemId,
                                                               parser.m_declEntity.publicId, parser.m_declEntity.notation);
                                }
                                finally { afterHandler(parser); }
                                handleDefault = false;
                            }
                        }
                        break;
                    case XML_ROLE_GENERAL_ENTITY_NAME:
                        {
                            if (enc.PredefinedEntityName(buf, s, next) != 0)
                            {
                                parser.m_declEntity = null;
                                break;
                            }
                            if (dtd.keepProcessing)
                            {
                                XCHARP name = poolStoreString(dtd.pool, enc, buf, s, next);
                                if (name.IsNull)
                                    return XML_ERROR_NO_MEMORY;
                                parser.m_declEntity = lookup<ENTITY>(parser, dtd.generalEntities, name, true);
                                if (parser.m_declEntity == null)
                                    return XML_ERROR_NO_MEMORY;
                                if (parser.m_declEntity.name != name)
                                {
                                    poolDiscard(dtd.pool);
                                    parser.m_declEntity = null;
                                }
                                else
                                {
                                    poolFinish(dtd.pool);
                                    parser.m_declEntity.publicId = XCHARP.NULL;
                                    parser.m_declEntity.is_param = false;
                                    /* if we have a parent parser or are reading an internal parameter
                                       entity, then the entity declaration is not considered "internal"
                                    */
                                    parser.m_declEntity.is_internal = !(parser.m_parentParser != null || parser.m_openInternalEntities != null);
                                    if (parser.m_entityDeclHandler != null)
                                        handleDefault = false;
                                }
                            }
                            else
                            {
                                poolDiscard(dtd.pool);
                                parser.m_declEntity = null;
                            }
                        }
                        break;
                    case XML_ROLE_PARAM_ENTITY_NAME:
                        if (dtd.keepProcessing)
                        {
                            XCHARP name = poolStoreString(dtd.pool, enc, buf, s, next);
                            if (name.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            parser.m_declEntity = lookup<ENTITY>(parser, dtd.paramEntities, name, true);
                            if (parser.m_declEntity == null)
                                return XML_ERROR_NO_MEMORY;
                            if (parser.m_declEntity.name != name)
                            {
                                poolDiscard(dtd.pool);
                                parser.m_declEntity = null;
                            }
                            else
                            {
                                poolFinish(dtd.pool);
                                parser.m_declEntity.publicId = XCHARP.NULL;
                                parser.m_declEntity.is_param = true;
                                /* if we have a parent parser or are reading an internal parameter
                                   entity, then the entity declaration is not considered "internal"
                                */
                                parser.m_declEntity.is_internal = !(parser.m_parentParser != null || parser.m_openInternalEntities != null);
                                if (parser.m_entityDeclHandler != null)
                                    handleDefault = false;
                            }
                        }
                        else
                        {
                            poolDiscard(dtd.pool);
                            parser.m_declEntity = null;
                        }
                        break;
                    case XML_ROLE_NOTATION_NAME:
                        parser.m_declNotationPublicId = XCHARP.NULL;
                        parser.m_declNotationName = XCHARP.NULL;
                        if (parser.m_notationDeclHandler != null)
                        {
                            parser.m_declNotationName = poolStoreString(parser.m_tempPool, enc, buf, s, next);
                            if (parser.m_declNotationName.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            poolFinish(parser.m_tempPool);
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_NOTATION_PUBLIC_ID:
                        if (!enc.IsPublicId(buf, s, next, ref eventPP))
                            return XML_ERROR_PUBLICID;
                        if (!parser.m_declNotationName.IsNull)
                        { /* means m_notationDeclHandler != NULL */
                            XCHARP tem = poolStoreString(parser.m_tempPool, enc, buf, s + enc.minBytesPerChar,
                                                         next - enc.minBytesPerChar);
                            if (tem.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            normalizePublicId(tem);
                            parser.m_declNotationPublicId = tem;
                            poolFinish(parser.m_tempPool);
                            handleDefault = false;
                        }
                        break;
                    case XML_ROLE_NOTATION_SYSTEM_ID:
                        if (!parser.m_declNotationName.IsNull && parser.m_notationDeclHandler != null)
                        {
                            XCHARP systemId = poolStoreString(parser.m_tempPool, enc, buf, s + enc.minBytesPerChar,
                                                              next - enc.minBytesPerChar);
                            if (systemId.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            eventEndPP = s;
                            beforeHandler(parser);
                            try
                            {
                                parser.m_notationDeclHandler(parser.m_handlerArg, parser.m_declNotationName, parser.m_curBase,
                                                             systemId, parser.m_declNotationPublicId);
                            }
                            finally { afterHandler(parser); }
                            handleDefault = false;
                        }
                        poolClear(parser.m_tempPool);
                        break;
                    case XML_ROLE_NOTATION_NO_SYSTEM_ID:
                        if (!parser.m_declNotationPublicId.IsNull && parser.m_notationDeclHandler != null)
                        {
                            eventEndPP = s;
                            beforeHandler(parser);
                            try
                            {
                                parser.m_notationDeclHandler(parser.m_handlerArg, parser.m_declNotationName, parser.m_curBase,
                                                             XCHARP.NULL, parser.m_declNotationPublicId);
                            }
                            finally { afterHandler(parser); }
                            handleDefault = false;
                        }
                        poolClear(parser.m_tempPool);
                        break;
                    case XML_ROLE_ERROR:
                        switch (tok)
                        {
                            case XML_TOK_PARAM_ENTITY_REF:
                                /* PE references in internal subset are
                                   not allowed within declarations. */
                                return XML_ERROR_PARAM_ENTITY_REF;
                            case XML_TOK_XML_DECL:
                                return XML_ERROR_MISPLACED_XML_PI;
                            default:
                                return XML_ERROR_SYNTAX;
                        }
                    case XML_ROLE_IGNORE_SECT:
                        {
                            XML_Error result;
                            if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                            handleDefault = false;
                            result = doIgnoreSection(parser, enc, buf, ref next, end, ref nextPtr, haveMore);
                            if (result != XML_ERROR_NONE)
                                return result;
                            else if (next < 0)
                            {
                                parser.m_processor = ignoreSectionProcessor;
                                return result;
                            }
                        }
                        break;
                    case XML_ROLE_GROUP_OPEN:
                        if (parser.m_prologState.level >= parser.m_groupSize)
                        {
                            if (parser.m_groupSize != 0)
                            {
                                if (!REALLOC(parser, parser.m_groupSize, parser.m_groupSize * 2))
                                    return XML_ERROR_NO_MEMORY;
                                var new_connector = new byte[parser.m_groupSize * 2];
                                Buffer.BlockCopy(parser.m_groupConnector, 0, new_connector, 0, (int)parser.m_groupSize);
                                parser.m_groupSize *= 2;
                                parser.m_groupConnector = new_connector;
                            }
                            else
                            {
                                if (!MALLOC(parser, 32))
                                {
                                    parser.m_groupSize = 0;
                                    return XML_ERROR_NO_MEMORY;
                                }
                                parser.m_groupSize = 32;
                                parser.m_groupConnector = new byte[32];
                            }
                        }
                        parser.m_groupConnector[parser.m_prologState.level] = 0;
                        if (dtd.in_eldecl)
                        {
                            int myindex = nextScaffoldPart(parser);
                            if (myindex < 0)
                                return XML_ERROR_NO_MEMORY;
                            if (dtd.scaffLevel >= dtd.scaffIndexSize)
                            {
                                long new_size = dtd.scaffIndexSize * 2;
                                if (!REALLOC(parser, dtd.scaffIndexSize * sizeof(int), new_size * sizeof(int)))
                                    return XML_ERROR_NO_MEMORY;
                                var new_scaff_index = new int[new_size];
                                Array.Copy(dtd.scaffIndex, new_scaff_index, dtd.scaffIndexSize);
                                dtd.scaffIndex = new_scaff_index;
                                dtd.scaffIndexSize = new_size;
                            }
                            dtd.scaffIndex[dtd.scaffLevel] = myindex;
                            dtd.scaffLevel++;
                            dtd.scaffold[myindex].type = XML_Content_Type.XML_CTYPE_SEQ;
                            if (parser.m_elementDeclHandler != null)
                                handleDefault = false;
                        }
                        break;
                    case XML_ROLE_GROUP_SEQUENCE:
                        if (parser.m_groupConnector[parser.m_prologState.level] == (byte)'|')
                            return XML_ERROR_SYNTAX;
                        parser.m_groupConnector[parser.m_prologState.level] = (byte)',';
                        if (dtd.in_eldecl && parser.m_elementDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_GROUP_CHOICE:
                        if (parser.m_groupConnector[parser.m_prologState.level] == (byte)',')
                            return XML_ERROR_SYNTAX;
                        if (dtd.in_eldecl && parser.m_groupConnector[parser.m_prologState.level] == 0
                            && dtd.scaffold[dtd.scaffIndex[dtd.scaffLevel - 1]].type != XML_Content_Type.XML_CTYPE_MIXED)
                        {
                            dtd.scaffold[dtd.scaffIndex[dtd.scaffLevel - 1]].type = XML_Content_Type.XML_CTYPE_CHOICE;
                            if (parser.m_elementDeclHandler != null)
                                handleDefault = false;
                        }
                        parser.m_groupConnector[parser.m_prologState.level] = (byte)'|';
                        break;
                    case XML_ROLE_PARAM_ENTITY_REF:
                    case XML_ROLE_INNER_PARAM_ENTITY_REF:
                        dtd.hasParamEntityRefs = true;
                        if (parser.m_paramEntityParsing == 0)
                            dtd.keepProcessing = dtd.standalone;
                        else
                        {
                            XCHARP name;
                            ENTITY entity;
                            name = poolStoreString(dtd.pool, enc, buf, s + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (name.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            entity = lookup<ENTITY>(parser, dtd.paramEntities, name, false);
                            poolDiscard(dtd.pool);
                            /* first, determine if a check for an existing declaration is needed;
                               if yes, check that the entity exists, and that it is internal,
                               otherwise call the skipped entity handler
                            */
                            if (parser.m_prologState.documentEntity
                                && (dtd.standalone ? parser.m_openInternalEntities == null : !dtd.hasParamEntityRefs))
                            {
                                if (entity == null)
                                    return XML_ERROR_UNDEFINED_ENTITY;
                                else if (!entity.is_internal)
                                {
                                    /* It's hard to exhaustively search the code to be sure,
                                     * but there doesn't seem to be a way of executing the
                                     * following line.  There are two cases:
                                     *
                                     * If 'standalone' is false, the DTD must have no
                                     * parameter entities or we wouldn't have passed the outer
                                     * 'if' statement.  That means the only entity in the hash
                                     * table is the external subset name "#" which cannot be
                                     * given as a parameter entity name in XML syntax, so the
                                     * lookup must have returned NULL and we don't even reach
                                     * the test for an internal entity.
                                     *
                                     * If 'standalone' is true, it does not seem to be
                                     * possible to create entities taking this code path that
                                     * are not internal entities, so fail the test above.
                                     *
                                     * Because this analysis is very uncertain, the code is
                                     * being left in place and merely removed from the
                                     * coverage test statistics.
                                     */
                                    return XML_ERROR_ENTITY_DECLARED_IN_PE; /* LCOV_EXCL_LINE */
                                }
                            }
                            else if (entity == null)
                            {
                                dtd.keepProcessing = dtd.standalone;
                                /* cannot report skipped entities in declarations */
                                if (role == XML_ROLE_PARAM_ENTITY_REF && parser.m_skippedEntityHandler != null)
                                {
                                    beforeHandler(parser);
                                    try { parser.m_skippedEntityHandler(parser.m_handlerArg, name, 1); }
                                    finally { afterHandler(parser); }
                                    handleDefault = false;
                                }
                                break;
                            }
                            if (entity.open)
                                return XML_ERROR_RECURSIVE_ENTITY_REF;
                            if (!entity.textPtr.IsNull)
                            {
                                XML_Error result;
                                bool betweenDecl = role == XML_ROLE_PARAM_ENTITY_REF;
                                result = processEntity(parser, entity, betweenDecl, EntityType.ENTITY_INTERNAL);
                                if (result != XML_ERROR_NONE)
                                    return result;
                                handleDefault = false;
                                break;
                            }
                            if (parser.m_externalEntityRefHandler != null)
                            {
                                dtd.paramEntityRead = false;
                                entity.open = true;
                                entityTrackingOnOpen(parser, entity);
                                int status;
                                beforeHandler(parser);
                                try
                                {
                                    status = parser.m_externalEntityRefHandler(parser.m_externalEntityRefHandlerArg, XCHARP.NULL,
                                                                               entity.@base, entity.systemId, entity.publicId);
                                }
                                finally { afterHandler(parser); }
                                if (status == 0)
                                {
                                    entityTrackingOnClose(parser, entity);
                                    entity.open = false;
                                    return XML_ERROR_EXTERNAL_ENTITY_HANDLING;
                                }
                                entityTrackingOnClose(parser, entity);
                                entity.open = false;
                                handleDefault = false;
                                if (!dtd.paramEntityRead)
                                {
                                    dtd.keepProcessing = dtd.standalone;
                                    break;
                                }
                            }
                            else
                            {
                                dtd.keepProcessing = dtd.standalone;
                                break;
                            }
                        }
                        if (!dtd.standalone && parser.m_notStandaloneHandler != null)
                        {
                            int status;
                            beforeHandler(parser);
                            try { status = parser.m_notStandaloneHandler(parser.m_handlerArg); }
                            finally { afterHandler(parser); }
                            if (status == 0)
                                return XML_ERROR_NOT_STANDALONE;
                        }
                        break;

                    /* Element declaration stuff */

                    case XML_ROLE_ELEMENT_NAME:
                        if (parser.m_elementDeclHandler != null)
                        {
                            parser.m_declElementType = getElementType(parser, enc, buf, s, next);
                            if (parser.m_declElementType == null)
                                return XML_ERROR_NO_MEMORY;
                            dtd.scaffLevel = 0;
                            dtd.scaffCount = 0;
                            dtd.in_eldecl = true;
                            handleDefault = false;
                        }
                        break;

                    case XML_ROLE_CONTENT_ANY:
                    case XML_ROLE_CONTENT_EMPTY:
                        if (dtd.in_eldecl)
                        {
                            if (parser.m_elementDeclHandler != null)
                            {
                                // NOTE: We are avoiding MALLOC(..) here to so that
                                //       applications that are not using XML_FreeContentModel but
                                //       plain free(..) or .free_fcn() to free the content model's
                                //       memory are safe.
                                var content = new XML_Content
                                {
                                    quant = XML_Content_Quant.XML_CQUANT_NONE,
                                    name = XCHARP.NULL,
                                    numchildren = 0,
                                    children = Array.Empty<XML_Content>(),
                                    type = role == XML_ROLE_CONTENT_ANY ? XML_Content_Type.XML_CTYPE_ANY : XML_Content_Type.XML_CTYPE_EMPTY,
                                };
                                eventEndPP = s;
                                beforeHandler(parser);
                                try { parser.m_elementDeclHandler(parser.m_handlerArg, parser.m_declElementType.name, content); }
                                finally { afterHandler(parser); }
                                handleDefault = false;
                            }
                            dtd.in_eldecl = false;
                        }
                        break;

                    case XML_ROLE_CONTENT_PCDATA:
                        if (dtd.in_eldecl)
                        {
                            dtd.scaffold[dtd.scaffIndex[dtd.scaffLevel - 1]].type = XML_Content_Type.XML_CTYPE_MIXED;
                            if (parser.m_elementDeclHandler != null)
                                handleDefault = false;
                        }
                        break;

                    case XML_ROLE_CONTENT_ELEMENT:
                        quant = XML_Content_Quant.XML_CQUANT_NONE;
                        goto case ELEMENT_CONTENT;
                    case XML_ROLE_CONTENT_ELEMENT_OPT:
                        quant = XML_Content_Quant.XML_CQUANT_OPT;
                        goto case ELEMENT_CONTENT;
                    case XML_ROLE_CONTENT_ELEMENT_REP:
                        quant = XML_Content_Quant.XML_CQUANT_REP;
                        goto case ELEMENT_CONTENT;
                    case XML_ROLE_CONTENT_ELEMENT_PLUS:
                        quant = XML_Content_Quant.XML_CQUANT_PLUS;
                        goto case ELEMENT_CONTENT;
                    case ELEMENT_CONTENT:
                        if (dtd.in_eldecl)
                        {
                            ELEMENT_TYPE el;
                            XCHARP name;
                            int nameLen;
                            int nxt = quant == XML_Content_Quant.XML_CQUANT_NONE ? next : next - enc.minBytesPerChar;
                            int myindex = nextScaffoldPart(parser);
                            if (myindex < 0)
                                return XML_ERROR_NO_MEMORY;
                            dtd.scaffold[myindex].type = XML_Content_Type.XML_CTYPE_NAME;
                            dtd.scaffold[myindex].quant = quant;
                            el = getElementType(parser, enc, buf, s, nxt);
                            if (el == null)
                                return XML_ERROR_NO_MEMORY;
                            name = el.name;
                            dtd.scaffold[myindex].name = name;
                            nameLen = name.Length + 1;
                            // Detect and prevent integer overflow
                            if ((uint)nameLen > uint.MaxValue - dtd.contentStringLen)
                                return XML_ERROR_NO_MEMORY;
                            dtd.contentStringLen += (uint)nameLen;
                            if (parser.m_elementDeclHandler != null)
                                handleDefault = false;
                        }
                        break;

                    case XML_ROLE_GROUP_CLOSE:
                        quant = XML_Content_Quant.XML_CQUANT_NONE;
                        goto case CLOSE_GROUP;
                    case XML_ROLE_GROUP_CLOSE_OPT:
                        quant = XML_Content_Quant.XML_CQUANT_OPT;
                        goto case CLOSE_GROUP;
                    case XML_ROLE_GROUP_CLOSE_REP:
                        quant = XML_Content_Quant.XML_CQUANT_REP;
                        goto case CLOSE_GROUP;
                    case XML_ROLE_GROUP_CLOSE_PLUS:
                        quant = XML_Content_Quant.XML_CQUANT_PLUS;
                        goto case CLOSE_GROUP;
                    case CLOSE_GROUP:
                        if (dtd.in_eldecl)
                        {
                            if (parser.m_elementDeclHandler != null)
                                handleDefault = false;
                            dtd.scaffLevel--;
                            dtd.scaffold[dtd.scaffIndex[dtd.scaffLevel]].quant = quant;
                            if (dtd.scaffLevel == 0)
                            {
                                if (!handleDefault)
                                {
                                    XML_Content model = build_model(parser);
                                    if (model == null)
                                        return XML_ERROR_NO_MEMORY;
                                    eventEndPP = s;
                                    beforeHandler(parser);
                                    try { parser.m_elementDeclHandler(parser.m_handlerArg, parser.m_declElementType.name, model); }
                                    finally { afterHandler(parser); }
                                }
                                dtd.in_eldecl = false;
                                dtd.contentStringLen = 0;
                            }
                        }
                        break;
                    /* End element declaration stuff */

                    case XML_ROLE_PI:
                        if (!reportProcessingInstruction(parser, enc, buf, s, next))
                            return XML_ERROR_NO_MEMORY;
                        handleDefault = false;
                        break;
                    case XML_ROLE_COMMENT:
                        if (!reportComment(parser, enc, buf, s, next))
                            return XML_ERROR_NO_MEMORY;
                        handleDefault = false;
                        break;
                    case XML_ROLE_NONE:
                        switch (tok)
                        {
                            case XML_TOK_BOM:
                                handleDefault = false;
                                break;
                        }
                        break;
                    case XML_ROLE_DOCTYPE_NONE:
                        if (parser.m_startDoctypeDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ENTITY_NONE:
                        if (dtd.keepProcessing && parser.m_entityDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_NOTATION_NONE:
                        if (parser.m_notationDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ATTLIST_NONE:
                        if (dtd.keepProcessing && parser.m_attlistDeclHandler != null)
                            handleDefault = false;
                        break;
                    case XML_ROLE_ELEMENT_NONE:
                        if (parser.m_elementDeclHandler != null)
                            handleDefault = false;
                        break;
                } /* end of big switch */

                if (handleDefault && parser.m_defaultHandler != null)
                    reportDefault(parser, enc, buf, s, next);

                switch (parser.m_parsingStatus.parsing)
                {
                    case XML_SUSPENDED:
                        nextPtr = next;
                        return XML_ERROR_NONE;
                    case XML_FINISHED:
                        return XML_ERROR_ABORTED;
                    case XML_PARSING:
                        if (parser.m_reenter)
                        {
                            nextPtr = next;
                            return XML_ERROR_NONE;
                        }
                        s = next;
                        tok = enc.XmlPrologTok(buf, s, end, ref next);
                        break;
                    default:
                        s = next;
                        tok = enc.XmlPrologTok(buf, s, end, ref next);
                        break;
                }
            }
            /* not reached */
        }

        /// <summary>epilogProcessor</summary>
        private static XML_Error epilogProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            parser.m_processor = epilogProcessor;
            parser.m_eventPtr = s;
            for (; ; )
            {
                int next = -1;
                int tok = parser.m_encoding.XmlPrologTok(parser.m_buffer, s, end, ref next);
                if (!accountingDiffTolerated(parser, tok, s, next, XML_Account.XML_ACCOUNT_DIRECT))
                {
                    accountingOnAbort(parser);
                    return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                }
                parser.m_eventEndPtr = next;
                switch (tok)
                {
                    /* report partial linebreak - it might be the last token */
                    case -XML_TOK_PROLOG_S:
                        if (parser.m_defaultHandler != null)
                        {
                            reportDefault(parser, parser.m_encoding, parser.m_buffer, s, next);
                            if (parser.m_parsingStatus.parsing == XML_FINISHED)
                                return XML_ERROR_ABORTED;
                        }
                        nextPtr = next;
                        return XML_ERROR_NONE;
                    case XML_TOK_NONE:
                        nextPtr = s;
                        return XML_ERROR_NONE;
                    case XML_TOK_PROLOG_S:
                        if (parser.m_defaultHandler != null)
                            reportDefault(parser, parser.m_encoding, parser.m_buffer, s, next);
                        break;
                    case XML_TOK_PI:
                        if (!reportProcessingInstruction(parser, parser.m_encoding, parser.m_buffer, s, next))
                            return XML_ERROR_NO_MEMORY;
                        break;
                    case XML_TOK_COMMENT:
                        if (!reportComment(parser, parser.m_encoding, parser.m_buffer, s, next))
                            return XML_ERROR_NO_MEMORY;
                        break;
                    case XML_TOK_INVALID:
                        parser.m_eventPtr = next;
                        return XML_ERROR_INVALID_TOKEN;
                    case XML_TOK_PARTIAL:
                        if (!parser.m_parsingStatus.finalBuffer)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_UNCLOSED_TOKEN;
                    case XML_TOK_PARTIAL_CHAR:
                        if (!parser.m_parsingStatus.finalBuffer)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_PARTIAL_CHAR;
                    default:
                        return XML_ERROR_JUNK_AFTER_DOC_ELEMENT;
                }
                switch (parser.m_parsingStatus.parsing)
                {
                    case XML_SUSPENDED:
                        parser.m_eventPtr = next;
                        nextPtr = next;
                        return XML_ERROR_NONE;
                    case XML_FINISHED:
                        parser.m_eventPtr = next;
                        return XML_ERROR_ABORTED;
                    case XML_PARSING:
                        if (parser.m_reenter)
                            return XML_ERROR_UNEXPECTED_STATE; // LCOV_EXCL_LINE
                        parser.m_eventPtr = s = next;
                        break;
                    default:
                        parser.m_eventPtr = s = next;
                        break;
                }
            }
        }

        /// <summary>processEntity</summary>
        private static XML_Error processEntity(XML_Parser parser, ENTITY entity, bool betweenDecl, EntityType type)
        {
            OPEN_INTERNAL_ENTITY openEntity;
            switch (type)
            {
                case EntityType.ENTITY_INTERNAL:
                    parser.m_processor = internalEntityProcessor;
                    break;
            }

            if (parser.m_freeEntities != null)
            {
                openEntity = parser.m_freeEntities;
                parser.m_freeEntities = openEntity.next;
            }
            else
            {
                if (!MALLOC(parser, OPEN_INTERNAL_ENTITY.CSize))
                    return XML_ERROR_NO_MEMORY;
                openEntity = new OPEN_INTERNAL_ENTITY();
            }

            entity.open = true;
            entity.hasMore = true;
            entityTrackingOnOpen(parser, entity);
            entity.processed = 0;
            switch (type)
            {
                case EntityType.ENTITY_INTERNAL:
                    openEntity.next = parser.m_openInternalEntities;
                    parser.m_openInternalEntities = openEntity;
                    break;
                case EntityType.ENTITY_ATTRIBUTE:
                    openEntity.next = parser.m_openAttributeEntities;
                    parser.m_openAttributeEntities = openEntity;
                    break;
                default:
                    openEntity.next = parser.m_openValueEntities;
                    parser.m_openValueEntities = openEntity;
                    break;
            }
            openEntity.entity = entity;
            openEntity.type = type;
            openEntity.startTagLevel = parser.m_tagLevel;
            openEntity.betweenDecl = betweenDecl;
            openEntity.internalEventPtr = -1;
            openEntity.internalEventEndPtr = -1;

            // Only internal entities make use of the reenter flag
            // therefore no need to set it for other entity types
            if (type == EntityType.ENTITY_INTERNAL)
                triggerReenter(parser);
            return XML_ERROR_NONE;
        }

        /// <summary>internalEntityProcessor</summary>
        private static XML_Error internalEntityProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            ENTITY entity;
            int textStart, textEnd;
            int next;
            XML_Error result;
            OPEN_INTERNAL_ENTITY openEntity = parser.m_openInternalEntities;
            if (openEntity == null)
                return XML_ERROR_UNEXPECTED_STATE;

            entity = openEntity.entity;

            // This will return early
            if (entity.hasMore)
            {
                byte[] text = entity.textPtr.a;
                textStart = entity.textPtr.i + entity.processed;
                textEnd = entity.textPtr.i + entity.textLen;
                /* Set a safe default value in case 'next' does not get set */
                next = textStart;

                if (entity.is_param)
                {
                    int tok = parser.m_internalEncoding.XmlPrologTok(text, textStart, textEnd, ref next);
                    result = doProlog(parser, parser.m_internalEncoding, text, textStart, textEnd, tok, next, ref next,
                                      false, false, XML_Account.XML_ACCOUNT_ENTITY_EXPANSION);
                }
                else
                {
                    result = doContent(parser, openEntity.startTagLevel, parser.m_internalEncoding, text, textStart,
                                       textEnd, ref next, false, XML_Account.XML_ACCOUNT_ENTITY_EXPANSION);
                }

                if (result != XML_ERROR_NONE)
                    return result;
                // Check if entity is complete, if not, mark down how much of it is
                // processed
                if (textEnd != next
                    && (parser.m_parsingStatus.parsing == XML_SUSPENDED
                        || (parser.m_parsingStatus.parsing == XML_PARSING && parser.m_reenter)))
                {
                    entity.processed = next - entity.textPtr.i;
                    return result;
                }

                // Entity is complete. We cannot close it here since we need to first
                // process its possible inner entities (which are added to the
                // m_openInternalEntities during doProlog or doContent calls above)
                entity.hasMore = false;
                if (!entity.is_param && openEntity.startTagLevel != parser.m_tagLevel)
                    return XML_ERROR_ASYNC_ENTITY;
                triggerReenter(parser);
                return result;
            } // End of entity processing, "if" block will return here

            // Remove fully processed openEntity from open entity list.
            entityTrackingOnClose(parser, entity);
            // openEntity is m_openInternalEntities' head, as we set it at the start of
            // this function and we skipped doProlog and doContent calls with hasMore set
            // to false. This means we can directly remove the head of
            // m_openInternalEntities
            entity.open = false;
            parser.m_openInternalEntities = parser.m_openInternalEntities.next;

            /* put openEntity back in list of free instances */
            openEntity.next = parser.m_freeEntities;
            parser.m_freeEntities = openEntity;

            // If there are no more open entities we can change the processor
            if (parser.m_openInternalEntities == null)
                parser.m_processor = entity.is_param ? prologProcessor : contentProcessor;

            triggerReenter(parser);
            return XML_ERROR_NONE;
        }

        /// <summary>errorProcessor</summary>
        private static XML_Error errorProcessor(XML_Parser parser, int s, int end, ref int nextPtr) => parser.m_errorCode;

        /// <summary>storeAttributeValue</summary>
        private static XML_Error storeAttributeValue(XML_Parser parser, ENCODING enc, bool isCdata, byte[] buf, int ptr,
                                                     int end, STRING_POOL pool, XML_Account account)
        {
            int next = ptr;
            XML_Error result = XML_ERROR_NONE;

            while (true)
            {
                if (parser.m_openAttributeEntities == null)
                {
                    result = appendAttributeValue(parser, enc, isCdata, buf, next, end, pool, account, ref next);
                }
                else
                {
                    OPEN_INTERNAL_ENTITY openEntity = parser.m_openAttributeEntities;
                    if (openEntity == null)
                        return XML_ERROR_UNEXPECTED_STATE;

                    ENTITY entity = openEntity.entity;
                    byte[] text = entity.textPtr.a;
                    int textStart = entity.textPtr.i + entity.processed;
                    int textEnd = entity.textPtr.i + entity.textLen;
                    /* Set a safe default value in case 'next' does not get set */
                    int nextInEntity = textStart;
                    if (entity.hasMore)
                    {
                        result = appendAttributeValue(parser, parser.m_internalEncoding, isCdata, text, textStart, textEnd,
                                                      pool, XML_Account.XML_ACCOUNT_ENTITY_EXPANSION, ref nextInEntity);
                        if (result != XML_ERROR_NONE)
                            break;
                        // Check if entity is complete, if not, mark down how much of it is
                        // processed. A XML_TOK_NONE token indicates that the entity is
                        // complete.
                        if (nextInEntity < textEnd)
                        {
                            entity.processed = nextInEntity - entity.textPtr.i;
                            continue;
                        }
                        entity.hasMore = false;
                        continue;
                    } // End of entity processing, "if" block skips the rest

                    // Remove fully processed openEntity from open entity list.
                    entityTrackingOnClose(parser, entity);
                    // openEntity is m_openAttributeEntities' head, since we set it at the
                    // start of this function and because we skipped appendAttributeValue
                    // call with hasMore set to false. This means we can directly remove the
                    // head of m_openAttributeEntities
                    entity.open = false;
                    parser.m_openAttributeEntities = parser.m_openAttributeEntities.next;

                    /* put openEntity back in list of free instances */
                    openEntity.next = parser.m_freeEntities;
                    parser.m_freeEntities = openEntity;
                }

                // Break if an error occurred or there is nothing left to process
                if (result != XML_ERROR_NONE || (parser.m_openAttributeEntities == null && end == next))
                    break;
            }

            if (result != XML_ERROR_NONE)
                return result;
            if (!isCdata && poolLength(pool) != 0 && poolLastChar(pool) == 0x20)
                poolChop(pool);
            if (!poolAppendChar(pool, 0))
                return XML_ERROR_NO_MEMORY;
            return XML_ERROR_NONE;
        }

        /// <summary>appendAttributeValue</summary>
        private static XML_Error appendAttributeValue(XML_Parser parser, ENCODING enc, bool isCdata, byte[] buf, int ptr,
                                                      int end, STRING_POOL pool, XML_Account account, ref int nextPtr)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            for (; ; )
            {
                int next = ptr; /* XmlAttributeValueTok doesn't always set the last arg */
                int tok = enc.XmlAttributeValueTok(buf, ptr, end, ref next);
                if (!accountingDiffTolerated(parser, tok, ptr, next, account))
                {
                    accountingOnAbort(parser);
                    return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                }
                switch (tok)
                {
                    case XML_TOK_NONE:
                        nextPtr = next;
                        return XML_ERROR_NONE;
                    case XML_TOK_INVALID:
                        if (enc == parser.m_encoding)
                            parser.m_eventPtr = next;
                        return XML_ERROR_INVALID_TOKEN;
                    case XML_TOK_PARTIAL:
                        if (enc == parser.m_encoding)
                            parser.m_eventPtr = ptr;
                        return XML_ERROR_INVALID_TOKEN;
                    case XML_TOK_CHAR_REF:
                        {
                            byte[] cbuf = new byte[XML_ENCODE_MAX];
                            int n = enc.CharRefNumber(buf, ptr);
                            if (n < 0)
                            {
                                if (enc == parser.m_encoding)
                                    parser.m_eventPtr = ptr;
                                return XML_ERROR_BAD_CHAR_REF;
                            }
                            if (!isCdata && n == 0x20 /* space */
                                && (poolLength(pool) == 0 || poolLastChar(pool) == 0x20))
                                break;
                            n = XmlUtf8Encode(n, cbuf, 0);
                            /* The XmlEncode() functions can never return 0 here.  That
                             * error return happens if the code point passed in is either
                             * negative or greater than or equal to 0x110000.  The
                             * XmlCharRefNumber() functions will all return a number
                             * strictly less than 0x110000 or a negative value if an error
                             * occurred.  The negative value is intercepted above, so
                             * XmlEncode() is never passed a value it might return an
                             * error for.
                             */
                            if (!poolAppendChars(pool, cbuf, 0, n))
                                return XML_ERROR_NO_MEMORY;
                        }
                        break;
                    case XML_TOK_DATA_CHARS:
                        if (poolAppend(pool, enc, buf, ptr, next).IsNull)
                            return XML_ERROR_NO_MEMORY;
                        break;
                    case XML_TOK_TRAILING_CR:
                    case XML_TOK_ATTRIBUTE_VALUE_S:
                    case XML_TOK_DATA_NEWLINE:
                        if (tok == XML_TOK_TRAILING_CR)
                            next = ptr + enc.minBytesPerChar;
                        if (!isCdata && (poolLength(pool) == 0 || poolLastChar(pool) == 0x20))
                            break;
                        if (!poolAppendChar(pool, 0x20))
                            return XML_ERROR_NO_MEMORY;
                        break;
                    case XML_TOK_ENTITY_REF:
                        {
                            XCHARP name;
                            ENTITY entity;
                            bool checkEntityDecl;
                            byte ch = (byte)enc.PredefinedEntityName(buf, ptr + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (ch != 0)
                            {
                                /* NOTE: We are replacing 4-6 characters original input for 1 character
                                 *       so there is no amplification and hence recording without
                                 *       protection. */
                                accountingDiffTolerated(parser, tok, 0, 1, XML_Account.XML_ACCOUNT_ENTITY_EXPANSION);
                                if (!poolAppendChar(pool, ch))
                                    return XML_ERROR_NO_MEMORY;
                                break;
                            }
                            name = poolStoreString(parser.m_temp2Pool, enc, buf, ptr + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (name.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            entity = lookup<ENTITY>(parser, dtd.generalEntities, name, false);
                            poolDiscard(parser.m_temp2Pool);
                            /* First, determine if a check for an existing declaration is needed;
                               if yes, check that the entity exists, and that it is internal.
                            */
                            if (pool == dtd.pool) /* are we called from prolog? */
                                checkEntityDecl = parser.m_prologState.documentEntity
                                                  && (dtd.standalone ? parser.m_openInternalEntities == null : !dtd.hasParamEntityRefs);
                            else /* if (pool == &parser->m_tempPool): we are called from content */
                                checkEntityDecl = !dtd.hasParamEntityRefs || dtd.standalone;
                            if (checkEntityDecl)
                            {
                                if (entity == null)
                                    return XML_ERROR_UNDEFINED_ENTITY;
                                else if (!entity.is_internal)
                                    return XML_ERROR_ENTITY_DECLARED_IN_PE;
                            }
                            else if (entity == null)
                            {
                                /* Cannot report skipped entity here - see comments on
                                   parser->m_skippedEntityHandler.
                                if (parser->m_skippedEntityHandler)
                                  parser->m_skippedEntityHandler(parser->m_handlerArg, name, 0);
                                */
                                /* Cannot call the default handler because this would be
                                   out of sync with the call to the startElementHandler.
                                if ((pool == &parser->m_tempPool) && parser->m_defaultHandler)
                                  reportDefault(parser, enc, ptr, next);
                                */
                                break;
                            }
                            if (entity.open)
                            {
                                if (enc == parser.m_encoding)
                                {
                                    /* It does not appear that this line can be executed.
                                     *
                                     * The "if (entity->open)" check catches recursive entity
                                     * definitions.  In order to be called with an open
                                     * entity, it must have gone through this code before and
                                     * been through the recursive call to
                                     * appendAttributeValue() some lines below.  That call
                                     * sets the local encoding ("enc") to the parser's
                                     * internal encoding (internal_utf8 or internal_utf16),
                                     * which can never be the same as the principle encoding.
                                     * It doesn't appear there is another code path that gets
                                     * here with entity->open being TRUE.
                                     *
                                     * Since it is not certain that this logic is watertight,
                                     * we keep the line and merely exclude it from coverage
                                     * tests.
                                     */
                                    parser.m_eventPtr = ptr; /* LCOV_EXCL_LINE */
                                }
                                return XML_ERROR_RECURSIVE_ENTITY_REF;
                            }
                            if (!entity.notation.IsNull)
                            {
                                if (enc == parser.m_encoding)
                                    parser.m_eventPtr = ptr;
                                return XML_ERROR_BINARY_ENTITY_REF;
                            }
                            if (entity.textPtr.IsNull)
                            {
                                if (enc == parser.m_encoding)
                                    parser.m_eventPtr = ptr;
                                return XML_ERROR_ATTRIBUTE_EXTERNAL_ENTITY_REF;
                            }
                            else
                            {
                                XML_Error result;
                                result = processEntity(parser, entity, false, EntityType.ENTITY_ATTRIBUTE);
                                if (result == XML_ERROR_NONE)
                                    nextPtr = next;
                                return result;
                            }
                        }
                    default:
                        /* The only token returned by XmlAttributeValueTok() that does
                         * not have an explicit case here is XML_TOK_PARTIAL_CHAR.
                         * Getting that would require an entity name to contain an
                         * incomplete XML character (e.g. \xE2\x82); however previous
                         * tokenisers will have already recognised and rejected such
                         * names before XmlAttributeValueTok() gets a look-in.  This
                         * default case should be retained as a safety net, but the code
                         * excluded from coverage tests.
                         *
                         * LCOV_EXCL_START
                         */
                        if (enc == parser.m_encoding)
                            parser.m_eventPtr = ptr;
                        return XML_ERROR_UNEXPECTED_STATE;
                        /* LCOV_EXCL_STOP */
                }
                ptr = next;
            }
            /* not reached */
        }

        /// <summary>storeEntityValue</summary>
        private static XML_Error storeEntityValue(XML_Parser parser, ENCODING enc, byte[] buf, int entityTextPtr,
                                                  int entityTextEnd, XML_Account account, ref int nextPtr)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            STRING_POOL pool = dtd.entityValuePool;
            XML_Error result = XML_ERROR_NONE;
            bool oldInEntityValue = parser.m_prologState.inEntityValue;
            parser.m_prologState.inEntityValue = true;
            /* never return Null for the value argument in EntityDeclHandler,
               since this would indicate an external entity; therefore we
               have to make sure that entityValuePool.start is not null */
            if (pool.blocks == null)
            {
                if (!poolGrow(pool))
                    return XML_ERROR_NO_MEMORY;
            }

            int next = entityTextPtr;

            /* Nothing to tokenize. */
            if (entityTextPtr >= entityTextEnd)
            {
                result = XML_ERROR_NONE;
                goto endEntityValue;
            }

            for (; ; )
            {
                next = entityTextPtr; /* XmlEntityValueTok doesn't always set the last arg */
                int tok = enc.XmlEntityValueTok(buf, entityTextPtr, entityTextEnd, ref next);

                if (!accountingDiffTolerated(parser, tok, entityTextPtr, next, account))
                {
                    accountingOnAbort(parser);
                    result = XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                    goto endEntityValue;
                }

                switch (tok)
                {
                    case XML_TOK_PARAM_ENTITY_REF:
                        if (parser.m_isParamEntity || enc != parser.m_encoding)
                        {
                            XCHARP name;
                            ENTITY entity;
                            name = poolStoreString(parser.m_tempPool, enc, buf, entityTextPtr + enc.minBytesPerChar,
                                                   next - enc.minBytesPerChar);
                            if (name.IsNull)
                            {
                                result = XML_ERROR_NO_MEMORY;
                                goto endEntityValue;
                            }
                            entity = lookup<ENTITY>(parser, dtd.paramEntities, name, false);
                            poolDiscard(parser.m_tempPool);
                            if (entity == null)
                            {
                                /* not a well-formedness error - see XML 1.0: WFC Entity Declared */
                                /* cannot report skipped entity here - see comments on
                                   parser->m_skippedEntityHandler
                                if (parser->m_skippedEntityHandler)
                                  parser->m_skippedEntityHandler(parser->m_handlerArg, name, 0);
                                */
                                dtd.keepProcessing = dtd.standalone;
                                goto endEntityValue;
                            }
                            if (entity.open || entity == parser.m_declEntity)
                            {
                                if (enc == parser.m_encoding)
                                    parser.m_eventPtr = entityTextPtr;
                                result = XML_ERROR_RECURSIVE_ENTITY_REF;
                                goto endEntityValue;
                            }
                            if (!entity.systemId.IsNull)
                            {
                                if (parser.m_externalEntityRefHandler != null)
                                {
                                    dtd.paramEntityRead = false;
                                    entity.open = true;
                                    entityTrackingOnOpen(parser, entity);
                                    int status;
                                    beforeHandler(parser);
                                    try
                                    {
                                        status = parser.m_externalEntityRefHandler(parser.m_externalEntityRefHandlerArg, XCHARP.NULL,
                                                                                   entity.@base, entity.systemId, entity.publicId);
                                    }
                                    finally { afterHandler(parser); }
                                    if (status == 0)
                                    {
                                        entityTrackingOnClose(parser, entity);
                                        entity.open = false;
                                        result = XML_ERROR_EXTERNAL_ENTITY_HANDLING;
                                        goto endEntityValue;
                                    }
                                    entityTrackingOnClose(parser, entity);
                                    entity.open = false;
                                    if (!dtd.paramEntityRead)
                                        dtd.keepProcessing = dtd.standalone;
                                }
                                else
                                    dtd.keepProcessing = dtd.standalone;
                            }
                            else
                            {
                                result = processEntity(parser, entity, false, EntityType.ENTITY_VALUE);
                                goto endEntityValue;
                            }
                            break;
                        }
                        /* In the internal subset, PE references are not legal
                           within markup declarations, e.g entity values in this case. */
                        parser.m_eventPtr = entityTextPtr;
                        result = XML_ERROR_PARAM_ENTITY_REF;
                        goto endEntityValue;
                    case XML_TOK_NONE:
                        result = XML_ERROR_NONE;
                        goto endEntityValue;
                    case XML_TOK_ENTITY_REF:
                    case XML_TOK_DATA_CHARS:
                        if (poolAppend(pool, enc, buf, entityTextPtr, next).IsNull)
                        {
                            result = XML_ERROR_NO_MEMORY;
                            goto endEntityValue;
                        }
                        break;
                    case XML_TOK_TRAILING_CR:
                    case XML_TOK_DATA_NEWLINE:
                        if (tok == XML_TOK_TRAILING_CR)
                            next = entityTextPtr + enc.minBytesPerChar;
                        if (!poolAppendChar(pool, 0xA))
                        {
                            result = XML_ERROR_NO_MEMORY;
                            goto endEntityValue;
                        }
                        break;
                    case XML_TOK_CHAR_REF:
                        {
                            byte[] cbuf = new byte[XML_ENCODE_MAX];
                            int n = enc.CharRefNumber(buf, entityTextPtr);
                            if (n < 0)
                            {
                                if (enc == parser.m_encoding)
                                    parser.m_eventPtr = entityTextPtr;
                                result = XML_ERROR_BAD_CHAR_REF;
                                goto endEntityValue;
                            }
                            n = XmlUtf8Encode(n, cbuf, 0);
                            if (!poolAppendChars(pool, cbuf, 0, n))
                            {
                                result = XML_ERROR_NO_MEMORY;
                                goto endEntityValue;
                            }
                        }
                        break;
                    case XML_TOK_PARTIAL:
                        if (enc == parser.m_encoding)
                            parser.m_eventPtr = entityTextPtr;
                        result = XML_ERROR_INVALID_TOKEN;
                        goto endEntityValue;
                    case XML_TOK_INVALID:
                        if (enc == parser.m_encoding)
                            parser.m_eventPtr = next;
                        result = XML_ERROR_INVALID_TOKEN;
                        goto endEntityValue;
                    default:
                        /* This default case should be unnecessary -- all the tokens
                         * that XmlEntityValueTok() can return have their own explicit
                         * cases -- but should be retained for safety.  We do however
                         * exclude it from the coverage statistics.
                         *
                         * LCOV_EXCL_START
                         */
                        if (enc == parser.m_encoding)
                            parser.m_eventPtr = entityTextPtr;
                        result = XML_ERROR_UNEXPECTED_STATE;
                        goto endEntityValue;
                        /* LCOV_EXCL_STOP */
                }
                entityTextPtr = next;
            }
        /* not reached */

        endEntityValue:
            parser.m_prologState.inEntityValue = oldInEntityValue;
            nextPtr = next;
            return result;
        }

        /// <summary>callStoreEntityValue</summary>
        private static XML_Error callStoreEntityValue(XML_Parser parser, ENCODING enc, byte[] buf, int entityTextPtr,
                                                      int entityTextEnd, XML_Account account)
        {
            int next = entityTextPtr;
            XML_Error result = XML_ERROR_NONE;
            while (true)
            {
                if (parser.m_openValueEntities == null)
                {
                    result = storeEntityValue(parser, enc, buf, next, entityTextEnd, account, ref next);
                }
                else
                {
                    OPEN_INTERNAL_ENTITY openEntity = parser.m_openValueEntities;
                    if (openEntity == null)
                        return XML_ERROR_UNEXPECTED_STATE;

                    ENTITY entity = openEntity.entity;
                    byte[] text = entity.textPtr.a;
                    int textStart = entity.textPtr.i + entity.processed;
                    int textEnd = entity.textPtr.i + entity.textLen;
                    /* Set a safe default value in case 'next' does not get set */
                    int nextInEntity = textStart;
                    if (entity.hasMore)
                    {
                        result = storeEntityValue(parser, parser.m_internalEncoding, text, textStart, textEnd,
                                                  XML_Account.XML_ACCOUNT_ENTITY_EXPANSION, ref nextInEntity);
                        if (result != XML_ERROR_NONE)
                            break;
                        // Check if entity is complete, if not, mark down how much of it is
                        // processed. A XML_TOK_NONE token indicates that the entity is
                        // complete.
                        if (textEnd != nextInEntity)
                        {
                            entity.processed = nextInEntity - entity.textPtr.i;
                            continue;
                        }
                        entity.hasMore = false;
                        continue;
                    } // End of entity processing, "if" block skips the rest

                    // Remove fully processed openEntity from open entity list.
                    entityTrackingOnClose(parser, entity);
                    // openEntity is m_openValueEntities' head, since we set it at the
                    // start of this function and because we skipped storeEntityValue call
                    // with hasMore set to false. This means we can directly remove the head
                    // of m_openValueEntities
                    entity.open = false;
                    parser.m_openValueEntities = parser.m_openValueEntities.next;

                    /* put openEntity back in list of free instances */
                    openEntity.next = parser.m_freeEntities;
                    parser.m_freeEntities = openEntity;
                }

                // Break if an error occurred or there is nothing left to process
                if (result != XML_ERROR_NONE || (parser.m_openValueEntities == null && entityTextEnd == next))
                    break;
            }

            return result;
        }

        /// <summary>normalizeLines</summary>
        private static void normalizeLines(XCHARP s)
        {
            int k = 0;
            for (; ; k++)
            {
                if (s[k] == 0)
                    return;
                if (s[k] == 0xD)
                    break;
            }
            int p = k;
            do
            {
                if (s[k] == 0xD)
                {
                    s.Set(p++, 0xA);
                    if (s[++k] == 0xA)
                        k++;
                }
                else
                    s.Set(p++, s[k++]);
            } while (s[k] != 0);
            s.Set(p, 0);
        }

        /// <summary>reportProcessingInstruction</summary>
        private static bool reportProcessingInstruction(XML_Parser parser, ENCODING enc, byte[] buf, int start, int end)
        {
            XCHARP target;
            XCHARP data;
            int tem;
            if (parser.m_processingInstructionHandler == null)
            {
                if (parser.m_defaultHandler != null)
                    reportDefault(parser, enc, buf, start, end);
                return true;
            }
            start += enc.minBytesPerChar * 2;
            tem = start + enc.NameLength(buf, start);
            target = poolStoreString(parser.m_tempPool, enc, buf, start, tem);
            if (target.IsNull)
                return false;
            poolFinish(parser.m_tempPool);
            data = poolStoreString(parser.m_tempPool, enc, buf, enc.SkipS(buf, tem), end - enc.minBytesPerChar * 2);
            if (data.IsNull)
                return false;
            normalizeLines(data);
            beforeHandler(parser);
            try { parser.m_processingInstructionHandler(parser.m_handlerArg, target, data); }
            finally { afterHandler(parser); }
            poolClear(parser.m_tempPool);
            return true;
        }

        /// <summary>reportComment</summary>
        private static bool reportComment(XML_Parser parser, ENCODING enc, byte[] buf, int start, int end)
        {
            XCHARP data;
            if (parser.m_commentHandler == null)
            {
                if (parser.m_defaultHandler != null)
                    reportDefault(parser, enc, buf, start, end);
                return true;
            }
            data = poolStoreString(parser.m_tempPool, enc, buf, start + enc.minBytesPerChar * 4, end - enc.minBytesPerChar * 3);
            if (data.IsNull)
                return false;
            normalizeLines(data);
            beforeHandler(parser);
            try { parser.m_commentHandler(parser.m_handlerArg, data); }
            finally { afterHandler(parser); }
            poolClear(parser.m_tempPool);
            return true;
        }

        /// <summary>reportDefault</summary>
        private static void reportDefault(XML_Parser parser, ENCODING enc, byte[] buf, int s, int end)
        {
            if (MUST_CONVERT(enc))
            {
                XML_Convert_Result convert_res;
                ref int eventPP = ref (enc == parser.m_encoding
                    ? ref parser.m_eventPtr
                    : ref parser.m_openInternalEntities.internalEventPtr);
                ref int eventEndPP = ref (enc == parser.m_encoding
                    ? ref parser.m_eventEndPtr
                    : ref parser.m_openInternalEntities.internalEventEndPtr);
                do
                {
                    int dataPtr = 0;
                    convert_res = XmlConvert(enc, buf, ref s, end, parser.m_dataBuf, ref dataPtr, parser.m_dataBuf.Length);
                    eventEndPP = s;
                    beforeHandler(parser);
                    try { parser.m_defaultHandler(parser.m_handlerArg, new XCHARP(parser.m_dataBuf, 0), dataPtr); }
                    finally { afterHandler(parser); }
                    eventPP = s;
                } while (convert_res != XML_Convert_Result.XML_CONVERT_COMPLETED
                         && convert_res != XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE);
            }
            else
            {
                beforeHandler(parser);
                try { parser.m_defaultHandler(parser.m_handlerArg, new XCHARP(buf, s), end - s); }
                finally { afterHandler(parser); }
            }
        }

        /// <summary>defineAttribute</summary>
        private static bool defineAttribute(ELEMENT_TYPE type, ATTRIBUTE_ID attId, bool isCdata, bool isId, XCHARP value,
                                            XML_Parser parser)
        {
            if (!value.IsNull || isId)
            {
                /* The handling of default attributes gets messed up if we have
                   a default which duplicates a non-default. */
                NAMED nameFound = lookup<NAME_AND_DEFAULT_ATTRIBUTE>(parser, type.defaultAttForName, attId.name, false);
                if (nameFound != null)
                    return true;
                if (isId && type.idAtt == null && !attId.xmlns)
                    type.idAtt = attId;
            }
            if (type.nDefaultAtts == type.allocDefaultAtts)
            {
                long count = type.allocDefaultAtts * 2;
                if (count == 0)
                    count = 8;
                if (!REALLOC(parser, type.defaultAtts == null ? -1 : type.allocDefaultAtts * DEFAULT_ATTRIBUTE.CSize,
                             count * DEFAULT_ATTRIBUTE.CSize))
                    return false;
                var temp = new DEFAULT_ATTRIBUTE[count];
                if (type.defaultAtts != null)
                    Array.Copy(type.defaultAtts, temp, type.nDefaultAtts);
                type.allocDefaultAtts = count;
                type.defaultAtts = temp;
            }
            ref DEFAULT_ATTRIBUTE att = ref type.defaultAtts[type.nDefaultAtts];
            att.id = attId;
            att.value = value;
            att.isCdata = isCdata;
            if (!isCdata)
                attId.maybeTokenized = true;

            NAME_AND_DEFAULT_ATTRIBUTE nameAndDefaultAttribute =
                lookup<NAME_AND_DEFAULT_ATTRIBUTE>(parser, type.defaultAttForName, attId.name, true);
            if (nameAndDefaultAttribute == null)
                return false;
            if (!nameAndDefaultAttribute.initialized)
            {
                nameAndDefaultAttribute.attIndex = type.nDefaultAtts;
                nameAndDefaultAttribute.initialized = true;
            }

            type.nDefaultAtts += 1;
            return true;
        }

        /// <summary>setElementTypePrefix</summary>
        private static bool setElementTypePrefix(XML_Parser parser, ELEMENT_TYPE elementType)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            XCHARP name;
            for (name = elementType.name; name[0] != 0; name = name + 1)
            {
                if (name[0] == (byte)':')
                {
                    PREFIX prefix;
                    XCHARP s;
                    for (s = elementType.name; s != name; s = s + 1)
                    {
                        if (!poolAppendChar(dtd.pool, s[0]))
                            return false;
                    }
                    if (!poolAppendChar(dtd.pool, 0))
                        return false;
                    prefix = lookup<PREFIX>(parser, dtd.prefixes, poolStart(dtd.pool), true);
                    if (prefix == null)
                        return false;
                    if (prefix.name == poolStart(dtd.pool))
                        poolFinish(dtd.pool);
                    else
                        poolDiscard(dtd.pool);
                    elementType.prefix = prefix;
                    break;
                }
            }
            return true;
        }

        /// <summary>getAttributeId</summary>
        private static ATTRIBUTE_ID getAttributeId(XML_Parser parser, ENCODING enc, byte[] buf, int start, int end)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            ATTRIBUTE_ID id;
            XCHARP name;
            if (!poolAppendChar(dtd.pool, 0))
                return null;
            name = poolStoreString(dtd.pool, enc, buf, start, end);
            if (name.IsNull)
                return null;
            /* skip quotation mark - its storage will be reused (like in name[-1]) */
            name = name + 1;
            id = lookup<ATTRIBUTE_ID>(parser, dtd.attributeIds, name, true);
            if (id == null)
                return null;
            if (id.name != name)
                poolDiscard(dtd.pool);
            else
            {
                poolFinish(dtd.pool);
                if (!parser.m_ns)
                    ;
                else if (name[0] == (byte)'x' && name[1] == (byte)'m' && name[2] == (byte)'l' && name[3] == (byte)'n'
                         && name[4] == (byte)'s' && (name[5] == 0 || name[5] == (byte)':'))
                {
                    if (name[5] == 0)
                        id.prefix = dtd.defaultPrefix;
                    else
                        id.prefix = lookup<PREFIX>(parser, dtd.prefixes, name + 6, true);
                    id.xmlns = true;
                }
                else
                {
                    int i;
                    for (i = 0; name[i] != 0; i++)
                    {
                        /* attributes without prefix are *not* in the default namespace */
                        if (name[i] == (byte)':')
                        {
                            if (!poolAppendChars(dtd.pool, name, i))
                                return null;
                            if (!poolAppendChar(dtd.pool, 0))
                                return null;
                            id.prefix = lookup<PREFIX>(parser, dtd.prefixes, poolStart(dtd.pool), true);
                            if (id.prefix == null)
                                return null;
                            if (id.prefix.name == poolStart(dtd.pool))
                                poolFinish(dtd.pool);
                            else
                                poolDiscard(dtd.pool);
                            break;
                        }
                    }
                }
            }
            return id;
        }

        private const byte CONTEXT_SEP = 0x0C; // ASCII_FF

        /// <summary>getContext</summary>
        private static XCHARP getContext(XML_Parser parser)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            bool needSep = false;

            if (dtd.defaultPrefix.binding != null)
            {
                if (!poolAppendChar(parser.m_tempPool, (byte)'='))
                    return XCHARP.NULL;
                int len = dtd.defaultPrefix.binding.uriLen;
                if (parser.m_namespaceSeparator != 0)
                    len--;
                if (!poolAppendChars(parser.m_tempPool, dtd.defaultPrefix.binding.uri, 0, len))
                {
                    /* Because of memory caching, I don't believe this line can be
                     * executed.
                     *
                     * This is part of a loop copying the default prefix binding
                     * URI into the parser's temporary string pool.  Previously,
                     * that URI was copied into the same string pool, with a
                     * terminating NUL character, as part of setContext().  When
                     * the pool was cleared, that leaves a block definitely big
                     * enough to hold the URI on the free block list of the pool.
                     * The URI copy in getContext() therefore cannot run out of
                     * memory.
                     *
                     * If the pool is used between the setContext() and
                     * getContext() calls, the worst it can do is leave a bigger
                     * block on the front of the free list.  Given that this is
                     * all somewhat inobvious and program logic can be changed, we
                     * don't delete the line but we do exclude it from the test
                     * coverage statistics.
                     */
                    return XCHARP.NULL; /* LCOV_EXCL_LINE */
                }
                needSep = true;
            }

            foreach (PREFIX prefix in hashTableIter<PREFIX>(dtd.prefixes))
            {
                if (prefix.binding == null)
                {
                    /* This test appears to be (justifiable) paranoia.  There does
                     * not seem to be a way of injecting a prefix without a binding
                     * that doesn't get errored long before this function is called.
                     * The test should remain for safety's sake, so we instead
                     * exclude the following line from the coverage statistics.
                     */
                    continue; /* LCOV_EXCL_LINE */
                }
                if (needSep && !poolAppendChar(parser.m_tempPool, CONTEXT_SEP))
                    return XCHARP.NULL;
                if (!poolAppendChars(parser.m_tempPool, prefix.name, prefix.name.Length))
                    return XCHARP.NULL;
                if (!poolAppendChar(parser.m_tempPool, (byte)'='))
                    return XCHARP.NULL;
                int len = prefix.binding.uriLen;
                if (parser.m_namespaceSeparator != 0)
                    len--;
                if (!poolAppendChars(parser.m_tempPool, prefix.binding.uri, 0, len))
                    return XCHARP.NULL;
                needSep = true;
            }

            foreach (ENTITY e in hashTableIter<ENTITY>(dtd.generalEntities))
            {
                if (!e.open)
                    continue;
                if (needSep && !poolAppendChar(parser.m_tempPool, CONTEXT_SEP))
                    return XCHARP.NULL;
                if (!poolAppendChars(parser.m_tempPool, e.name, e.name.Length))
                    return XCHARP.NULL;
                needSep = true;
            }

            if (!poolAppendChar(parser.m_tempPool, 0))
                return XCHARP.NULL;
            return poolStart(parser.m_tempPool);
        }

        /// <summary>setContext</summary>
        private static bool setContext(XML_Parser parser, XCHARP context)
        {
            if (context.IsNull)
                return false;

            DTD dtd = parser.m_dtd; /* save one level of indirection */
            XCHARP s = context;

            while (context[0] != 0)
            {
                if (s[0] == CONTEXT_SEP || s[0] == 0)
                {
                    ENTITY e;
                    if (!poolAppendChar(parser.m_tempPool, 0))
                        return false;
                    e = lookup<ENTITY>(parser, dtd.generalEntities, poolStart(parser.m_tempPool), false);
                    if (e != null)
                        e.open = true;
                    if (s[0] != 0)
                        s = s + 1;
                    context = s;
                    poolDiscard(parser.m_tempPool);
                }
                else if (s[0] == (byte)'=')
                {
                    PREFIX prefix;
                    if (poolLength(parser.m_tempPool) == 0)
                        prefix = dtd.defaultPrefix;
                    else
                    {
                        if (!poolAppendChar(parser.m_tempPool, 0))
                            return false;
                        XCHARP prefixName = poolCopyStringNoFinish(dtd.pool, poolStart(parser.m_tempPool));
                        if (prefixName.IsNull)
                            return false;

                        prefix = lookup<PREFIX>(parser, dtd.prefixes, prefixName, true);

                        bool prefixNameUsed = prefix != null && prefix.name == prefixName;
                        if (prefixNameUsed)
                            poolFinish(dtd.pool);
                        else
                            poolDiscard(dtd.pool);

                        if (prefix == null)
                            return false;

                        poolDiscard(parser.m_tempPool);
                    }
                    for (context = s + 1; context[0] != CONTEXT_SEP && context[0] != 0; context = context + 1)
                        if (!poolAppendChar(parser.m_tempPool, context[0]))
                            return false;
                    if (!poolAppendChar(parser.m_tempPool, 0))
                        return false;
                    BINDING inherited = parser.m_inheritedBindings;
                    if (addBinding(parser, prefix, null, poolStart(parser.m_tempPool), ref inherited) != XML_ERROR_NONE)
                    {
                        parser.m_inheritedBindings = inherited;
                        return false;
                    }
                    parser.m_inheritedBindings = inherited;
                    poolDiscard(parser.m_tempPool);
                    if (context[0] != 0)
                        context = context + 1;
                    s = context;
                }
                else
                {
                    if (!poolAppendChar(parser.m_tempPool, s[0]))
                        return false;
                    s = s + 1;
                }
            }
            return true;
        }

        /// <summary>normalizePublicId</summary>
        private static void normalizePublicId(XCHARP publicId)
        {
            int p = 0;
            int s;
            for (s = 0; publicId[s] != 0; s++)
            {
                switch (publicId[s])
                {
                    case 0x20:
                    case 0xD:
                    case 0xA:
                        if (p != 0 && publicId[p - 1] != 0x20)
                            publicId.Set(p++, 0x20);
                        break;
                    default:
                        publicId.Set(p++, publicId[s]);
                        break;
                }
            }
            if (p != 0 && publicId[p - 1] == 0x20)
                --p;
            publicId.Set(p, 0);
        }

        /// <summary>dtdCreate</summary>
        private static DTD dtdCreate(XML_Parser parser)
        {
            if (!MALLOC(parser, DTD.CSize))
                return null;
            DTD p = new DTD();
            poolInit(p.pool, parser);
            poolInit(p.entityValuePool, parser);
            hashTableInit(p.generalEntities, parser);
            hashTableInit(p.elementTypes, parser);
            hashTableInit(p.attributeIds, parser);
            hashTableInit(p.prefixes, parser);
            p.paramEntityRead = false;
            hashTableInit(p.paramEntities, parser);
            p.defaultPrefix.name = XCHARP.NULL;
            p.defaultPrefix.binding = null;

            p.in_eldecl = false;
            p.scaffIndex = null;
            p.scaffIndexSize = 0;
            p.scaffold = null;
            p.scaffLevel = 0;
            p.scaffSize = 0;
            p.scaffCount = 0;
            p.contentStringLen = 0;

            p.keepProcessing = true;
            p.hasParamEntityRefs = false;
            p.standalone = false;
            return p;
        }

        /// <summary>dtdReset</summary>
        private static void dtdReset(DTD p, XML_Parser parser)
        {
            foreach (ELEMENT_TYPE e in hashTableIter<ELEMENT_TYPE>(p.elementTypes))
            {
                if (e.allocDefaultAtts != 0)
                    hashTableDestroy(e.defaultAttForName);
                if (e.defaultAtts != null)
                    FREE(parser, e.allocDefaultAtts * DEFAULT_ATTRIBUTE.CSize);
            }
            hashTableClear(p.generalEntities);
            p.paramEntityRead = false;
            hashTableClear(p.paramEntities);
            hashTableClear(p.elementTypes);
            hashTableClear(p.attributeIds);
            hashTableClear(p.prefixes);
            poolClear(p.pool);
            poolClear(p.entityValuePool);
            p.defaultPrefix.name = XCHARP.NULL;
            p.defaultPrefix.binding = null;

            p.in_eldecl = false;

            p.scaffIndex = null;
            p.scaffIndexSize = 0;
            p.scaffold = null;

            p.scaffLevel = 0;
            p.scaffSize = 0;
            p.scaffCount = 0;
            p.contentStringLen = 0;

            p.keepProcessing = true;
            p.hasParamEntityRefs = false;
            p.standalone = false;
        }

        /// <summary>
        /// dtdCopy: do a deep copy of the DTD. Return 0 for out of memory, non-zero otherwise.
        /// The new DTD has already been initialized.
        /// </summary>
        private static bool dtdCopy(XML_Parser oldParser, DTD newDtd, DTD oldDtd, XML_Parser parser)
        {
            /* Copy the prefix table. */
            foreach (PREFIX oldP in hashTableIter<PREFIX>(oldDtd.prefixes))
            {
                XCHARP name = poolCopyString(newDtd.pool, oldP.name);
                if (name.IsNull)
                    return false;
                if (lookup<PREFIX>(oldParser, newDtd.prefixes, name, true) == null)
                    return false;
            }

            /* Copy the attribute id table. */
            foreach (ATTRIBUTE_ID oldA in hashTableIter<ATTRIBUTE_ID>(oldDtd.attributeIds))
            {
                ATTRIBUTE_ID newA;
                XCHARP name;
                /* Remember to allocate the scratch byte before the name. */
                if (!poolAppendChar(newDtd.pool, 0))
                    return false;
                name = poolCopyString(newDtd.pool, oldA.name);
                if (name.IsNull)
                    return false;
                name = name + 1;
                newA = lookup<ATTRIBUTE_ID>(oldParser, newDtd.attributeIds, name, true);
                if (newA == null)
                    return false;
                newA.maybeTokenized = oldA.maybeTokenized;
                if (oldA.prefix != null)
                {
                    newA.xmlns = oldA.xmlns;
                    if (oldA.prefix == oldDtd.defaultPrefix)
                        newA.prefix = newDtd.defaultPrefix;
                    else
                        newA.prefix = lookup<PREFIX>(oldParser, newDtd.prefixes, oldA.prefix.name, false);
                }
            }

            /* Copy the element type table. */
            foreach (ELEMENT_TYPE oldE in hashTableIter<ELEMENT_TYPE>(oldDtd.elementTypes))
            {
                ELEMENT_TYPE newE;
                XCHARP name = poolCopyString(newDtd.pool, oldE.name);
                if (name.IsNull)
                    return false;
                newE = lookup<ELEMENT_TYPE>(oldParser, newDtd.elementTypes, name, true);
                if (newE == null)
                    return false;
                if (newE.defaultAttForName.parser == null)
                    hashTableInit(newE.defaultAttForName, parser);
                if (oldE.nDefaultAtts != 0)
                {
                    if (!MALLOC(parser, oldE.nDefaultAtts * DEFAULT_ATTRIBUTE.CSize))
                        return false;
                    newE.defaultAtts = new DEFAULT_ATTRIBUTE[oldE.nDefaultAtts];
                }
                if (oldE.idAtt != null)
                    newE.idAtt = lookup<ATTRIBUTE_ID>(oldParser, newDtd.attributeIds, oldE.idAtt.name, false);
                newE.allocDefaultAtts = newE.nDefaultAtts = oldE.nDefaultAtts;
                if (oldE.prefix != null)
                    newE.prefix = lookup<PREFIX>(oldParser, newDtd.prefixes, oldE.prefix.name, false);
                for (long i = 0; i < newE.nDefaultAtts; i++)
                {
                    XCHARP attributeName = oldE.defaultAtts[i].id.name;
                    newE.defaultAtts[i].id = lookup<ATTRIBUTE_ID>(oldParser, newDtd.attributeIds, attributeName, false);
                    newE.defaultAtts[i].isCdata = oldE.defaultAtts[i].isCdata;
                    if (!oldE.defaultAtts[i].value.IsNull)
                    {
                        newE.defaultAtts[i].value = poolCopyString(newDtd.pool, oldE.defaultAtts[i].value);
                        if (newE.defaultAtts[i].value.IsNull)
                            return false;
                    }
                    else
                        newE.defaultAtts[i].value = XCHARP.NULL;

                    NAME_AND_DEFAULT_ATTRIBUTE nameAndDefaultAttribute =
                        lookup<NAME_AND_DEFAULT_ATTRIBUTE>(parser, newE.defaultAttForName, attributeName, true);
                    if (nameAndDefaultAttribute == null)
                        return false;
                    if (!nameAndDefaultAttribute.initialized)
                    {
                        nameAndDefaultAttribute.attIndex = i;
                        nameAndDefaultAttribute.initialized = true;
                    }
                }
            }

            /* Copy the entity tables. */
            if (!copyEntityTable(oldParser, newDtd.generalEntities, newDtd.pool, oldDtd.generalEntities))
                return false;

            if (!copyEntityTable(oldParser, newDtd.paramEntities, newDtd.pool, oldDtd.paramEntities))
                return false;
            newDtd.paramEntityRead = oldDtd.paramEntityRead;

            newDtd.keepProcessing = oldDtd.keepProcessing;
            newDtd.hasParamEntityRefs = oldDtd.hasParamEntityRefs;
            newDtd.standalone = oldDtd.standalone;

            /* Don't want deep copying for scaffolding */
            newDtd.in_eldecl = oldDtd.in_eldecl;
            newDtd.scaffold = oldDtd.scaffold;
            newDtd.contentStringLen = oldDtd.contentStringLen;
            newDtd.scaffSize = oldDtd.scaffSize;
            newDtd.scaffLevel = oldDtd.scaffLevel;
            newDtd.scaffIndex = oldDtd.scaffIndex;
            newDtd.scaffIndexSize = oldDtd.scaffIndexSize;

            return true;
        } /* End dtdCopy */

        /// <summary>copyEntityTable</summary>
        private static bool copyEntityTable(XML_Parser oldParser, HASH_TABLE newTable, STRING_POOL newPool, HASH_TABLE oldTable)
        {
            XCHARP cachedOldBase = XCHARP.NULL;
            XCHARP cachedNewBase = XCHARP.NULL;

            foreach (ENTITY oldE in hashTableIter<ENTITY>(oldTable))
            {
                ENTITY newE;
                XCHARP name = poolCopyString(newPool, oldE.name);
                if (name.IsNull)
                    return false;
                newE = lookup<ENTITY>(oldParser, newTable, name, true);
                if (newE == null)
                    return false;
                if (!oldE.systemId.IsNull)
                {
                    XCHARP tem = poolCopyString(newPool, oldE.systemId);
                    if (tem.IsNull)
                        return false;
                    newE.systemId = tem;
                    if (!oldE.@base.IsNull)
                    {
                        if (oldE.@base == cachedOldBase)
                            newE.@base = cachedNewBase;
                        else
                        {
                            cachedOldBase = oldE.@base;
                            tem = poolCopyString(newPool, cachedOldBase);
                            if (tem.IsNull)
                                return false;
                            cachedNewBase = newE.@base = tem;
                        }
                    }
                    if (!oldE.publicId.IsNull)
                    {
                        tem = poolCopyString(newPool, oldE.publicId);
                        if (tem.IsNull)
                            return false;
                        newE.publicId = tem;
                    }
                }
                else
                {
                    XCHARP tem = poolCopyStringN(newPool, oldE.textPtr, oldE.textLen);
                    if (tem.IsNull)
                        return false;
                    newE.textPtr = tem;
                    newE.textLen = oldE.textLen;
                }
                if (!oldE.notation.IsNull)
                {
                    XCHARP tem = poolCopyString(newPool, oldE.notation);
                    if (tem.IsNull)
                        return false;
                    newE.notation = tem;
                }
                newE.is_param = oldE.is_param;
                newE.is_internal = oldE.is_internal;
            }
            return true;
        }

        /// <summary>nextScaffoldPart</summary>
        private static int nextScaffoldPart(XML_Parser parser)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            int next;

            if (dtd.scaffIndex == null)
            {
                if (!MALLOC(parser, parser.m_groupSize * sizeof(int)))
                    return -1;
                dtd.scaffIndex = new int[parser.m_groupSize];
                dtd.scaffIndexSize = parser.m_groupSize;
                dtd.scaffIndex[0] = 0;
            }

            if (dtd.scaffCount >= dtd.scaffSize)
            {
                CONTENT_SCAFFOLD[] temp;
                if (dtd.scaffold != null)
                {
                    /* Detect and prevent integer overflow */
                    if (dtd.scaffSize > uint.MaxValue / 2u)
                        return -1;
                    if (!REALLOC(parser, (long)dtd.scaffSize * CONTENT_SCAFFOLD.CSize, (long)dtd.scaffSize * 2 * CONTENT_SCAFFOLD.CSize))
                        return -1;
                    temp = new CONTENT_SCAFFOLD[dtd.scaffSize * 2];
                    Array.Copy(dtd.scaffold, temp, dtd.scaffSize);
                    dtd.scaffSize *= 2;
                }
                else
                {
                    if (!MALLOC(parser, INIT_SCAFFOLD_ELEMENTS * CONTENT_SCAFFOLD.CSize))
                        return -1;
                    temp = new CONTENT_SCAFFOLD[INIT_SCAFFOLD_ELEMENTS];
                    dtd.scaffSize = INIT_SCAFFOLD_ELEMENTS;
                }
                dtd.scaffold = temp;
            }
            next = (int)dtd.scaffCount++;
            if (dtd.scaffLevel != 0)
            {
                ref CONTENT_SCAFFOLD parent = ref dtd.scaffold[dtd.scaffIndex[dtd.scaffLevel - 1]];
                if (parent.lastchild != 0)
                    dtd.scaffold[parent.lastchild].nextsib = next;
                if (parent.childcnt == 0)
                    parent.firstchild = next;
                parent.lastchild = next;
                parent.childcnt++;
            }
            ref CONTENT_SCAFFOLD me = ref dtd.scaffold[next];
            me.firstchild = me.lastchild = me.childcnt = me.nextsib = 0;
            return next;
        }

        /// <summary>
        /// build_model: the C function lays the nodes out breadth first in one block, each node's
        /// children following its parent's; here the same order, nodes as objects.
        /// </summary>
        private static XML_Content build_model(XML_Parser parser)
        {
            /* Function build_model transforms the existing parser->m_dtd->scaffold
             * array of CONTENT_SCAFFOLD tree nodes into a new array of
             * XML_Content tree nodes followed by a gapless list of zero-terminated
             * strings. */
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            int count = (int)dtd.scaffCount;
            var ret = new XML_Content[count];
            var src = new int[count]; // dest->numchildren holds the scaffold index while queued
            for (int k = 0; k < count; k++)
                ret[k] = new XML_Content();

            int jobDest = 0;
            src[jobDest++] = 0;
            for (int dest = 0; dest < count; dest++)
            {
                int src_node = src[dest];
                ret[dest].type = dtd.scaffold[src_node].type;
                ret[dest].quant = dtd.scaffold[src_node].quant;
                if (ret[dest].type == XML_Content_Type.XML_CTYPE_NAME)
                {
                    ret[dest].name = new XCHARP(dtd.scaffold[src_node].name.ToArray().AsSpan().ToArray().Concat0(), 0);
                    ret[dest].numchildren = 0;
                    ret[dest].children = Array.Empty<XML_Content>();
                }
                else
                {
                    uint i;
                    int cn;
                    ret[dest].name = XCHARP.NULL;
                    ret[dest].numchildren = (uint)dtd.scaffold[src_node].childcnt;
                    ret[dest].children = new XML_Content[ret[dest].numchildren];
                    for (i = 0, cn = dtd.scaffold[src_node].firstchild; i < ret[dest].numchildren;
                         i++, cn = dtd.scaffold[cn].nextsib)
                    {
                        ret[dest].children[i] = ret[jobDest];
                        src[jobDest++] = cn;
                    }
                }
            }
            return ret[0];
        }

        /// <summary>getElementType</summary>
        private static ELEMENT_TYPE getElementType(XML_Parser parser, ENCODING enc, byte[] buf, int ptr, int end)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            XCHARP name = poolStoreString(dtd.pool, enc, buf, ptr, end);
            ELEMENT_TYPE ret;

            if (name.IsNull)
                return null;
            ret = lookup<ELEMENT_TYPE>(parser, dtd.elementTypes, name, true);
            if (ret == null)
                return null;
            if (ret.defaultAttForName.parser == null)
                hashTableInit(ret.defaultAttForName, getRootParserOf(parser, out _));
            if (ret.name != name)
                poolDiscard(dtd.pool);
            else
            {
                poolFinish(dtd.pool);
                if (!setElementTypePrefix(parser, ret))
                    return null;
            }
            return ret;
        }

        /// <summary>a NUL-terminated copy of the bytes</summary>
        private static byte[] Concat0(this byte[] b)
        {
            var r = new byte[b.Length + 1];
            Buffer.BlockCopy(b, 0, r, 0, b.Length);
            return r;
        }
    }
}
