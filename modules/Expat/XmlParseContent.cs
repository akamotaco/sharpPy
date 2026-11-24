using System;
using static SharpPy.Modules.Expat.XmlTok;
using static SharpPy.Modules.Expat.XmlRole;
using static SharpPy.Modules.Expat.XML_Error;
using static SharpPy.Modules.Expat.XML_Parsing;
using XML_Parser = SharpPy.Modules.Expat.XML_ParserStruct;

namespace SharpPy.Modules.Expat
{
    /// <summary>xmlparse.c: content, CDATA and ignore sections, start tags, namespaces, encodings.</summary>
    internal static partial class XmlParse
    {
        /// <summary>
        /// storeRawNames: if tags are still open when a parse call ends, their raw names (pointing
        /// into the parse buffer) are copied into the tags' own buffers.
        /// </summary>
        private static bool storeRawNames(XML_Parser parser)
        {
            TAG tag = parser.m_tagStack;
            while (tag != null)
            {
                int bufSize;
                int nameLen = tag.name.strLen + 1;
                int rawNameLen;
                int rawNameBuf = nameLen;
                /* Stop if already stored.  Since m_tagStack is a stack, we can stop
                   at the first entry that has already been copied; everything
                   below it in the stack is already been accounted for in a
                   previous call to this function. */
                if (tag.rawNameBuf == tag.buf && tag.rawName == rawNameBuf)
                    break;
                /* For re-use purposes we need to ensure that the
                   size of tag->buf is a multiple of sizeof(XML_Char). */
                rawNameLen = tag.rawNameLength;
                bufSize = nameLen + rawNameLen;
                if (bufSize > tag.bufEnd)
                {
                    if (!REALLOC(parser, tag.bufEnd, bufSize))
                        return false;
                    var temp = new byte[bufSize];
                    Buffer.BlockCopy(tag.buf, 0, temp, 0, tag.bufEnd);
                    /* if tag->name.str points to tag->buf (only when namespace
                       processing is off) then we have to update it */
                    if (tag.name.str == new XCHARP(tag.buf, 0))
                        tag.name.str = new XCHARP(temp, 0);
                    /* if tag->name.localPart is set (when namespace processing is on)
                       then update it as well, since it will always point into tag->buf */
                    if (!tag.name.localPart.IsNull && tag.name.localPart.a == tag.buf)
                        tag.name.localPart = new XCHARP(temp, tag.name.localPart.i);
                    tag.buf = temp;
                    tag.bufEnd = bufSize;
                }
                Buffer.BlockCopy(tag.rawNameBuf, tag.rawName, tag.buf, rawNameBuf, tag.rawNameLength);
                tag.rawNameBuf = tag.buf;
                tag.rawName = rawNameBuf;
                tag = tag.parent;
            }
            return true;
        }

        /// <summary>contentProcessor</summary>
        private static XML_Error contentProcessor(XML_Parser parser, int start, int end, ref int endPtr)
        {
            XML_Error result = doContent(parser, parser.m_parentParser != null ? 1 : 0, parser.m_encoding,
                                         parser.m_buffer, start, end, ref endPtr,
                                         !parser.m_parsingStatus.finalBuffer, XML_Account.XML_ACCOUNT_DIRECT);
            if (result == XML_ERROR_NONE)
            {
                if (!storeRawNames(parser))
                    return XML_ERROR_NO_MEMORY;
            }
            return result;
        }

        /// <summary>externalEntityInitProcessor</summary>
        private static XML_Error externalEntityInitProcessor(XML_Parser parser, int start, int end, ref int endPtr)
        {
            XML_Error result = initializeEncoding(parser);
            if (result != XML_ERROR_NONE)
                return result;
            parser.m_processor = externalEntityInitProcessor2;
            return externalEntityInitProcessor2(parser, start, end, ref endPtr);
        }

        /// <summary>externalEntityInitProcessor2</summary>
        private static XML_Error externalEntityInitProcessor2(XML_Parser parser, int start, int end, ref int endPtr)
        {
            int next = start; /* XmlContentTok doesn't always set the last arg */
            int tok = parser.m_encoding.XmlContentTok(parser.m_buffer, start, end, ref next);
            switch (tok)
            {
                case XML_TOK_BOM:
                    if (!accountingDiffTolerated(parser, tok, start, next, XML_Account.XML_ACCOUNT_DIRECT))
                    {
                        accountingOnAbort(parser);
                        return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                    }
                    /* If we are at the end of the buffer, this would cause the next stage,
                       i.e. externalEntityInitProcessor3, to pass control directly to
                       doContent (by detecting XML_TOK_NONE) without processing any xml text
                       declaration - causing the error XML_ERROR_MISPLACED_XML_PI in doContent.
                    */
                    if (next == end && !parser.m_parsingStatus.finalBuffer)
                    {
                        endPtr = next;
                        return XML_ERROR_NONE;
                    }
                    start = next;
                    break;
                case XML_TOK_PARTIAL:
                    if (!parser.m_parsingStatus.finalBuffer)
                    {
                        endPtr = start;
                        return XML_ERROR_NONE;
                    }
                    parser.m_eventPtr = start;
                    return XML_ERROR_UNCLOSED_TOKEN;
                case XML_TOK_PARTIAL_CHAR:
                    if (!parser.m_parsingStatus.finalBuffer)
                    {
                        endPtr = start;
                        return XML_ERROR_NONE;
                    }
                    parser.m_eventPtr = start;
                    return XML_ERROR_PARTIAL_CHAR;
            }
            parser.m_processor = externalEntityInitProcessor3;
            return externalEntityInitProcessor3(parser, start, end, ref endPtr);
        }

        /// <summary>externalEntityInitProcessor3</summary>
        private static XML_Error externalEntityInitProcessor3(XML_Parser parser, int start, int end, ref int endPtr)
        {
            int tok;
            int next = start; /* XmlContentTok doesn't always set the last arg */
            parser.m_eventPtr = start;
            tok = parser.m_encoding.XmlContentTok(parser.m_buffer, start, end, ref next);
            /* Note: These bytes are accounted later in:
                     - processXmlDecl
                     - externalEntityContentProcessor
            */
            parser.m_eventEndPtr = next;

            switch (tok)
            {
                case XML_TOK_XML_DECL:
                    {
                        XML_Error result = processXmlDecl(parser, true, start, next);
                        if (result != XML_ERROR_NONE)
                            return result;
                        switch (parser.m_parsingStatus.parsing)
                        {
                            case XML_SUSPENDED:
                                endPtr = next;
                                return XML_ERROR_NONE;
                            case XML_FINISHED:
                                return XML_ERROR_ABORTED;
                            case XML_PARSING:
                                if (parser.m_reenter)
                                    return XML_ERROR_UNEXPECTED_STATE; // LCOV_EXCL_LINE
                                start = next;
                                break;
                            default:
                                start = next;
                                break;
                        }
                    }
                    break;
                case XML_TOK_PARTIAL:
                    if (!parser.m_parsingStatus.finalBuffer)
                    {
                        endPtr = start;
                        return XML_ERROR_NONE;
                    }
                    return XML_ERROR_UNCLOSED_TOKEN;
                case XML_TOK_PARTIAL_CHAR:
                    if (!parser.m_parsingStatus.finalBuffer)
                    {
                        endPtr = start;
                        return XML_ERROR_NONE;
                    }
                    return XML_ERROR_PARTIAL_CHAR;
            }
            parser.m_processor = externalEntityContentProcessor;
            parser.m_tagLevel = 1;
            return externalEntityContentProcessor(parser, start, end, ref endPtr);
        }

        /// <summary>externalEntityContentProcessor</summary>
        private static XML_Error externalEntityContentProcessor(XML_Parser parser, int start, int end, ref int endPtr)
        {
            XML_Error result = doContent(parser, 1, parser.m_encoding, parser.m_buffer, start, end, ref endPtr,
                                         !parser.m_parsingStatus.finalBuffer, XML_Account.XML_ACCOUNT_ENTITY_EXPANSION);
            if (result == XML_ERROR_NONE)
            {
                if (!storeRawNames(parser))
                    return XML_ERROR_NO_MEMORY;
            }
            return result;
        }

        private static readonly byte[] s_newline = { 0xA, 0 };

        /// <summary>doContent</summary>
        private static XML_Error doContent(XML_Parser parser, int startTagLevel, ENCODING enc, byte[] buf, int s,
                                           int end, ref int nextPtr, bool haveMore, XML_Account account)
        {
            /* save one level of indirection */
            DTD dtd = parser.m_dtd;

            ref int eventPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventPtr
                : ref parser.m_openInternalEntities.internalEventPtr);
            ref int eventEndPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventEndPtr
                : ref parser.m_openInternalEntities.internalEventEndPtr);
            eventPP = s;

            for (; ; )
            {
                int next = s; /* XmlContentTok doesn't always set the last arg */
                int tok = enc.XmlContentTok(buf, s, end, ref next);
                int accountAfter = (tok == XML_TOK_TRAILING_RSQB || tok == XML_TOK_TRAILING_CR)
                    ? (haveMore ? s /* i.e. 0 bytes */ : end)
                    : next;
                if (!accountingDiffTolerated(parser, tok, s, accountAfter, account))
                {
                    accountingOnAbort(parser);
                    return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                }
                eventEndPP = next;
                switch (tok)
                {
                    case XML_TOK_TRAILING_CR:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        eventEndPP = end;
                        if (parser.m_characterDataHandler != null)
                        {
                            beforeHandler(parser);
                            try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(s_newline, 0), 1); }
                            finally { afterHandler(parser); }
                        }
                        else if (parser.m_defaultHandler != null)
                            reportDefault(parser, enc, buf, s, end);
                        /* We are at the end of the final buffer, should we check for
                           XML_SUSPENDED, XML_FINISHED?
                        */
                        if (startTagLevel == 0)
                            return XML_ERROR_NO_ELEMENTS;
                        if (parser.m_tagLevel != startTagLevel)
                            return XML_ERROR_ASYNC_ENTITY;
                        nextPtr = end;
                        return XML_ERROR_NONE;
                    case XML_TOK_NONE:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        if (startTagLevel > 0)
                        {
                            if (parser.m_tagLevel != startTagLevel)
                                return XML_ERROR_ASYNC_ENTITY;
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_NO_ELEMENTS;
                    case XML_TOK_INVALID:
                        eventPP = next;
                        return XML_ERROR_INVALID_TOKEN;
                    case XML_TOK_PARTIAL:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_UNCLOSED_TOKEN;
                    case XML_TOK_PARTIAL_CHAR:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_PARTIAL_CHAR;
                    case XML_TOK_ENTITY_REF:
                        {
                            XCHARP name;
                            ENTITY entity;
                            byte ch = (byte)enc.PredefinedEntityName(buf, s + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (ch != 0)
                            {
                                /* NOTE: We are replacing 4-6 characters original input for 1 character
                                 *       so there is no amplification and hence recording without
                                 *       protection. */
                                accountingDiffTolerated(parser, tok, 0, 1, XML_Account.XML_ACCOUNT_ENTITY_EXPANSION);
                                if (parser.m_characterDataHandler != null)
                                {
                                    beforeHandler(parser);
                                    try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(new byte[] { ch, 0 }, 0), 1); }
                                    finally { afterHandler(parser); }
                                }
                                else if (parser.m_defaultHandler != null)
                                    reportDefault(parser, enc, buf, s, next);
                                break;
                            }
                            name = poolStoreString(dtd.pool, enc, buf, s + enc.minBytesPerChar, next - enc.minBytesPerChar);
                            if (name.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            entity = lookup<ENTITY>(parser, dtd.generalEntities, name, false);
                            poolDiscard(dtd.pool);
                            /* First, determine if a check for an existing declaration is needed;
                               if yes, check that the entity exists, and that it is internal,
                               otherwise call the skipped entity or default handler.
                            */
                            if (!dtd.hasParamEntityRefs || dtd.standalone)
                            {
                                if (entity == null)
                                    return XML_ERROR_UNDEFINED_ENTITY;
                                else if (!entity.is_internal)
                                    return XML_ERROR_ENTITY_DECLARED_IN_PE;
                            }
                            else if (entity == null)
                            {
                                if (parser.m_skippedEntityHandler != null)
                                {
                                    beforeHandler(parser);
                                    try { parser.m_skippedEntityHandler(parser.m_handlerArg, name, 0); }
                                    finally { afterHandler(parser); }
                                }
                                else if (parser.m_defaultHandler != null)
                                    reportDefault(parser, enc, buf, s, next);
                                break;
                            }
                            if (entity.open)
                                return XML_ERROR_RECURSIVE_ENTITY_REF;
                            if (!entity.notation.IsNull)
                                return XML_ERROR_BINARY_ENTITY_REF;
                            if (!entity.textPtr.IsNull)
                            {
                                XML_Error result;
                                if (!parser.m_defaultExpandInternalEntities)
                                {
                                    if (parser.m_skippedEntityHandler != null)
                                    {
                                        beforeHandler(parser);
                                        try { parser.m_skippedEntityHandler(parser.m_handlerArg, entity.name, 0); }
                                        finally { afterHandler(parser); }
                                    }
                                    else if (parser.m_defaultHandler != null)
                                        reportDefault(parser, enc, buf, s, next);
                                    break;
                                }
                                result = processEntity(parser, entity, false, EntityType.ENTITY_INTERNAL);
                                if (result != XML_ERROR_NONE)
                                    return result;
                            }
                            else if (parser.m_externalEntityRefHandler != null)
                            {
                                XCHARP context;
                                entity.open = true;
                                context = getContext(parser);
                                entity.open = false;
                                if (context.IsNull)
                                    return XML_ERROR_NO_MEMORY;
                                int status;
                                beforeHandler(parser);
                                try
                                {
                                    status = parser.m_externalEntityRefHandler(parser.m_externalEntityRefHandlerArg, context,
                                                                               entity.@base, entity.systemId, entity.publicId);
                                }
                                finally { afterHandler(parser); }
                                if (status == 0)
                                    return XML_ERROR_EXTERNAL_ENTITY_HANDLING;
                                poolDiscard(parser.m_tempPool);
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                            break;
                        }
                    case XML_TOK_START_TAG_NO_ATTS:
                    /* fall through */
                    case XML_TOK_START_TAG_WITH_ATTS:
                        {
                            TAG tag;
                            XML_Error result;
                            int toPtr;
                            if (parser.m_freeTagList != null)
                            {
                                tag = parser.m_freeTagList;
                                parser.m_freeTagList = parser.m_freeTagList.parent;
                            }
                            else
                            {
                                if (!MALLOC(parser, TAG.CSize))
                                    return XML_ERROR_NO_MEMORY;
                                tag = new TAG();
                                if (!MALLOC(parser, INIT_TAG_BUF_SIZE))
                                {
                                    FREE(parser, TAG.CSize);
                                    return XML_ERROR_NO_MEMORY;
                                }
                                tag.buf = new byte[INIT_TAG_BUF_SIZE];
                                tag.bufEnd = INIT_TAG_BUF_SIZE;
                            }
                            tag.bindings = null;
                            tag.parent = parser.m_tagStack;
                            parser.m_tagStack = tag;
                            tag.name.localPart = XCHARP.NULL;
                            tag.name.prefix = XCHARP.NULL;
                            tag.rawNameBuf = buf;
                            tag.rawName = s + enc.minBytesPerChar;
                            tag.rawNameLength = enc.NameLength(buf, tag.rawName);
                            ++parser.m_tagLevel;
                            {
                                int rawNameEnd = tag.rawName + tag.rawNameLength;
                                int fromPtr = tag.rawName;
                                toPtr = 0;
                                for (; ; )
                                {
                                    XML_Convert_Result convert_res = XmlConvert(enc, buf, ref fromPtr, rawNameEnd, tag.buf, ref toPtr, tag.bufEnd - 1);
                                    int convLen = toPtr;
                                    if (fromPtr >= rawNameEnd || convert_res == XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE)
                                    {
                                        tag.name.strLen = convLen;
                                        break;
                                    }
                                    int bufSize = tag.bufEnd * 2;
                                    {
                                        if (!REALLOC(parser, tag.bufEnd, bufSize))
                                            return XML_ERROR_NO_MEMORY;
                                        var temp = new byte[bufSize];
                                        Buffer.BlockCopy(tag.buf, 0, temp, 0, tag.bufEnd);
                                        tag.buf = temp;
                                        tag.bufEnd = bufSize;
                                        toPtr = convLen;
                                    }
                                }
                            }
                            tag.name.str = new XCHARP(tag.buf, 0);
                            tag.buf[toPtr] = 0;
                            result = storeAtts(parser, enc, buf, s, tag.name, ref tag.bindings, account);
                            if (result != XML_ERROR_NONE)
                                return result;
                            if (parser.m_startElementHandler != null)
                            {
                                beforeHandler(parser);
                                try { parser.m_startElementHandler(parser.m_handlerArg, tag.name.str, parser.m_appAtts); }
                                finally { afterHandler(parser); }
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                            poolClear(parser.m_tempPool);
                            break;
                        }
                    case XML_TOK_EMPTY_ELEMENT_NO_ATTS:
                    /* fall through */
                    case XML_TOK_EMPTY_ELEMENT_WITH_ATTS:
                        {
                            int rawName = s + enc.minBytesPerChar;
                            XML_Error result;
                            BINDING bindings = null;
                            bool noElmHandlers = true;
                            TAG_NAME name = new TAG_NAME();
                            name.str = poolStoreString(parser.m_tempPool, enc, buf, rawName, rawName + enc.NameLength(buf, rawName));
                            if (name.str.IsNull)
                                return XML_ERROR_NO_MEMORY;
                            poolFinish(parser.m_tempPool);
                            result = storeAtts(parser, enc, buf, s, name, ref bindings,
                                               XML_Account.XML_ACCOUNT_NONE /* token spans whole start tag */);
                            if (result != XML_ERROR_NONE)
                            {
                                freeBindings(parser, bindings);
                                return result;
                            }
                            poolFinish(parser.m_tempPool);
                            if (parser.m_startElementHandler != null)
                            {
                                beforeHandler(parser);
                                try { parser.m_startElementHandler(parser.m_handlerArg, name.str, parser.m_appAtts); }
                                finally { afterHandler(parser); }
                                noElmHandlers = false;
                            }
                            if (parser.m_endElementHandler != null)
                            {
                                if (parser.m_startElementHandler != null)
                                    eventPP = eventEndPP;
                                beforeHandler(parser);
                                try { parser.m_endElementHandler(parser.m_handlerArg, name.str); }
                                finally { afterHandler(parser); }
                                noElmHandlers = false;
                            }
                            if (noElmHandlers && parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                            poolClear(parser.m_tempPool);
                            freeBindings(parser, bindings);
                        }
                        if (parser.m_tagLevel == 0 && parser.m_parsingStatus.parsing != XML_FINISHED)
                        {
                            if (parser.m_parsingStatus.parsing == XML_SUSPENDED
                                || (parser.m_parsingStatus.parsing == XML_PARSING && parser.m_reenter))
                                parser.m_processor = epilogProcessor;
                            else
                                return epilogProcessor(parser, next, end, ref nextPtr);
                        }
                        break;
                    case XML_TOK_END_TAG:
                        if (parser.m_tagLevel == startTagLevel)
                            return XML_ERROR_ASYNC_ENTITY;
                        else
                        {
                            int len;
                            int rawName;
                            TAG tag = parser.m_tagStack;
                            rawName = s + enc.minBytesPerChar * 2;
                            len = enc.NameLength(buf, rawName);
                            if (len != tag.rawNameLength
                                || !tag.rawNameBuf.AsSpan(tag.rawName, len).SequenceEqual(buf.AsSpan(rawName, len)))
                            {
                                eventPP = rawName;
                                return XML_ERROR_TAG_MISMATCH;
                            }
                            parser.m_tagStack = tag.parent;
                            tag.parent = parser.m_freeTagList;
                            parser.m_freeTagList = tag;
                            --parser.m_tagLevel;
                            if (parser.m_endElementHandler != null)
                            {
                                XCHARP localPart;
                                XCHARP prefix;
                                XCHARP uri;
                                localPart = tag.name.localPart;
                                if (parser.m_ns && !localPart.IsNull)
                                {
                                    /* localPart and prefix may have been overwritten in
                                       tag->name.str, since this points to the binding->uri
                                       buffer which gets reused; so we have to add them again
                                    */
                                    uri = tag.name.str + tag.name.uriLen;
                                    int u = 0, k = 0;
                                    /* don't need to check for space - already done in storeAtts() */
                                    while (localPart[k] != 0)
                                        uri.Set(u++, localPart[k++]);
                                    prefix = tag.name.prefix;
                                    if (parser.m_ns_triplets && !prefix.IsNull)
                                    {
                                        uri.Set(u++, parser.m_namespaceSeparator);
                                        k = 0;
                                        while (prefix[k] != 0)
                                            uri.Set(u++, prefix[k++]);
                                    }
                                    uri.Set(u, 0);
                                }
                                beforeHandler(parser);
                                try { parser.m_endElementHandler(parser.m_handlerArg, tag.name.str); }
                                finally { afterHandler(parser); }
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                            while (tag.bindings != null)
                            {
                                BINDING b = tag.bindings;
                                if (parser.m_endNamespaceDeclHandler != null)
                                {
                                    beforeHandler(parser);
                                    try { parser.m_endNamespaceDeclHandler(parser.m_handlerArg, b.prefix.name); }
                                    finally { afterHandler(parser); }
                                }
                                tag.bindings = tag.bindings.nextTagBinding;
                                b.nextTagBinding = parser.m_freeBindingList;
                                parser.m_freeBindingList = b;
                                b.prefix.binding = b.prevPrefixBinding;
                            }
                            if (parser.m_tagLevel == 0 && parser.m_parsingStatus.parsing != XML_FINISHED)
                            {
                                if (parser.m_parsingStatus.parsing == XML_SUSPENDED
                                    || (parser.m_parsingStatus.parsing == XML_PARSING && parser.m_reenter))
                                    parser.m_processor = epilogProcessor;
                                else
                                    return epilogProcessor(parser, next, end, ref nextPtr);
                            }
                        }
                        break;
                    case XML_TOK_CHAR_REF:
                        {
                            int n = enc.CharRefNumber(buf, s);
                            if (n < 0)
                                return XML_ERROR_BAD_CHAR_REF;
                            if (parser.m_characterDataHandler != null)
                            {
                                byte[] cbuf = new byte[XML_ENCODE_MAX + 1];
                                int clen = XmlUtf8Encode(n, cbuf, 0);
                                beforeHandler(parser);
                                try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(cbuf, 0), clen); }
                                finally { afterHandler(parser); }
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                        }
                        break;
                    case XML_TOK_XML_DECL:
                        return XML_ERROR_MISPLACED_XML_PI;
                    case XML_TOK_DATA_NEWLINE:
                        if (parser.m_characterDataHandler != null)
                        {
                            beforeHandler(parser);
                            try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(s_newline, 0), 1); }
                            finally { afterHandler(parser); }
                        }
                        else if (parser.m_defaultHandler != null)
                            reportDefault(parser, enc, buf, s, next);
                        break;
                    case XML_TOK_CDATA_SECT_OPEN:
                        {
                            XML_Error result;
                            if (parser.m_startCdataSectionHandler != null)
                            {
                                beforeHandler(parser);
                                try { parser.m_startCdataSectionHandler(parser.m_handlerArg); }
                                finally { afterHandler(parser); }
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                            result = doCdataSection(parser, enc, buf, ref next, end, ref nextPtr, haveMore, account);
                            if (result != XML_ERROR_NONE)
                                return result;
                            else if (next < 0)
                            {
                                parser.m_processor = cdataSectionProcessor;
                                return result;
                            }
                        }
                        break;
                    case XML_TOK_TRAILING_RSQB:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        if (parser.m_characterDataHandler != null)
                        {
                            if (MUST_CONVERT(enc))
                            {
                                int dataPtr = 0;
                                XmlConvert(enc, buf, ref s, end, parser.m_dataBuf, ref dataPtr, parser.m_dataBuf.Length);
                                beforeHandler(parser);
                                try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(parser.m_dataBuf, 0), dataPtr); }
                                finally { afterHandler(parser); }
                            }
                            else
                            {
                                beforeHandler(parser);
                                try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(buf, s), end - s); }
                                finally { afterHandler(parser); }
                            }
                        }
                        else if (parser.m_defaultHandler != null)
                            reportDefault(parser, enc, buf, s, end);
                        /* We are at the end of the final buffer, should we check for
                           XML_SUSPENDED, XML_FINISHED?
                        */
                        if (startTagLevel == 0)
                        {
                            eventPP = end;
                            return XML_ERROR_NO_ELEMENTS;
                        }
                        if (parser.m_tagLevel != startTagLevel)
                        {
                            eventPP = end;
                            return XML_ERROR_ASYNC_ENTITY;
                        }
                        nextPtr = end;
                        return XML_ERROR_NONE;
                    case XML_TOK_DATA_CHARS:
                        {
                            XML_CharacterDataHandler charDataHandler = parser.m_characterDataHandler;
                            if (charDataHandler != null)
                            {
                                if (MUST_CONVERT(enc))
                                {
                                    for (; ; )
                                    {
                                        int dataPtr = 0;
                                        XML_Convert_Result convert_res = XmlConvert(enc, buf, ref s, next, parser.m_dataBuf, ref dataPtr, parser.m_dataBuf.Length);
                                        eventEndPP = s;
                                        beforeHandler(parser);
                                        try { charDataHandler(parser.m_handlerArg, new XCHARP(parser.m_dataBuf, 0), dataPtr); }
                                        finally { afterHandler(parser); }
                                        if (convert_res == XML_Convert_Result.XML_CONVERT_COMPLETED
                                            || convert_res == XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE)
                                            break;
                                        eventPP = s;
                                    }
                                }
                                else
                                {
                                    beforeHandler(parser);
                                    try { charDataHandler(parser.m_handlerArg, new XCHARP(buf, s), next - s); }
                                    finally { afterHandler(parser); }
                                }
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                        }
                        break;
                    case XML_TOK_PI:
                        if (!reportProcessingInstruction(parser, enc, buf, s, next))
                            return XML_ERROR_NO_MEMORY;
                        break;
                    case XML_TOK_COMMENT:
                        if (!reportComment(parser, enc, buf, s, next))
                            return XML_ERROR_NO_MEMORY;
                        break;
                    default:
                        /* All of the tokens produced by XmlContentTok() have their own
                         * explicit cases, so this default is not strictly necessary.
                         * However it is a useful safety net, so we retain the code and
                         * simply exclude it from the coverage tests.
                         *
                         * LCOV_EXCL_START
                         */
                        if (parser.m_defaultHandler != null)
                            reportDefault(parser, enc, buf, s, next);
                        break;
                        /* LCOV_EXCL_STOP */
                }
                switch (parser.m_parsingStatus.parsing)
                {
                    case XML_SUSPENDED:
                        eventPP = next;
                        nextPtr = next;
                        return XML_ERROR_NONE;
                    case XML_FINISHED:
                        eventPP = next;
                        return XML_ERROR_ABORTED;
                    case XML_PARSING:
                        if (parser.m_reenter)
                        {
                            nextPtr = next;
                            return XML_ERROR_NONE;
                        }
                        eventPP = s = next;
                        break;
                    default:
                        eventPP = s = next;
                        break;
                }
            }
            /* not reached */
        }

        /// <summary>
        /// freeBindings: this function does not call free() on the allocated memory, merely moving
        /// it to the parser's m_freeBindingList where it can be freed or reused as appropriate.
        /// </summary>
        private static void freeBindings(XML_Parser parser, BINDING bindings)
        {
            while (bindings != null)
            {
                BINDING b = bindings;

                /* m_startNamespaceDeclHandler will have been called for this
                 * binding in addBindings(), so call the end handler now.
                 */
                if (parser.m_endNamespaceDeclHandler != null)
                {
                    beforeHandler(parser);
                    try { parser.m_endNamespaceDeclHandler(parser.m_handlerArg, b.prefix.name); }
                    finally { afterHandler(parser); }
                }

                bindings = bindings.nextTagBinding;
                b.nextTagBinding = parser.m_freeBindingList;
                parser.m_freeBindingList = b;
                b.prefix.binding = b.prevPrefixBinding;
            }
        }

        /// <summary>
        /// storeAtts: precondition: all arguments must be non-NULL;
        /// Purpose:
        /// - normalize attributes
        /// - check attributes for well-formedness
        /// - generate namespace aware attribute names (URI, prefix)
        /// - build list of attributes for startElementHandler
        /// - default attributes
        /// - process namespace declarations (check and report them)
        /// - generate namespace aware element name (URI, prefix)
        /// </summary>
        private static XML_Error storeAtts(XML_Parser parser, ENCODING enc, byte[] buf, int attStr,
                                           TAG_NAME tagNamePtr, ref BINDING bindingsPtr, XML_Account account)
        {
            DTD dtd = parser.m_dtd; /* save one level of indirection */
            int attIndex = 0;
            XCHARP uri;
            int nPrefixes = 0;
            BINDING binding;
            XCHARP localPart;

            /* lookup the element type name */
            ELEMENT_TYPE elementType = lookup<ELEMENT_TYPE>(parser, dtd.elementTypes, tagNamePtr.str, false);
            if (elementType == null)
            {
                XCHARP name = poolCopyString(dtd.pool, tagNamePtr.str);
                if (name.IsNull)
                    return XML_ERROR_NO_MEMORY;
                elementType = lookup<ELEMENT_TYPE>(parser, dtd.elementTypes, name, true);
                if (elementType == null)
                    return XML_ERROR_NO_MEMORY;
                if (elementType.defaultAttForName.parser == null)
                    hashTableInit(elementType.defaultAttForName, parser);
                if (parser.m_ns && !setElementTypePrefix(parser, elementType))
                    return XML_ERROR_NO_MEMORY;
            }
            int nDefaultAtts = (int)elementType.nDefaultAtts;

            /* get the attributes from the tokenizer */
            int n = enc.GetAtts(buf, attStr, parser.m_attsSize, parser.m_atts);

            if (n + nDefaultAtts > parser.m_attsSize)
            {
                int oldAttsSize = parser.m_attsSize;
                parser.m_attsSize = n + nDefaultAtts + INIT_ATTS_SIZE;
                if (!REALLOC(parser, (long)oldAttsSize * SIZEOF_ATTRIBUTE, (long)parser.m_attsSize * SIZEOF_ATTRIBUTE))
                {
                    parser.m_attsSize = oldAttsSize;
                    return XML_ERROR_NO_MEMORY;
                }
                var temp = new ATTRIBUTE[parser.m_attsSize];
                Array.Copy(parser.m_atts, temp, oldAttsSize);
                parser.m_atts = temp;
                parser.m_appAtts = new XCHARP[parser.m_attsSize * 4];
                if (n > oldAttsSize)
                    enc.GetAtts(buf, attStr, n, parser.m_atts);
            }

            XCHARP[] appAtts = parser.m_appAtts;
            for (int i = 0; i < n; i++)
            {
                ref ATTRIBUTE currAtt = ref parser.m_atts[i];
                /* add the name and value to the attribute list */
                ATTRIBUTE_ID attId = getAttributeId(parser, enc, buf, currAtt.name, currAtt.name + enc.NameLength(buf, currAtt.name));
                if (attId == null)
                    return XML_ERROR_NO_MEMORY;
                /* Detect duplicate attributes by their QNames. This does not work when
                   namespace processing is turned on and different prefixes for the same
                   namespace are used. For this case we have a check further down.
                */
                if (attId.name[-1] != 0)
                {
                    if (enc == parser.m_encoding)
                        parser.m_eventPtr = parser.m_atts[i].name;
                    return XML_ERROR_DUPLICATE_ATTRIBUTE;
                }
                attId.name.Set(-1, 1);
                appAtts[attIndex++] = attId.name;
                if (!parser.m_atts[i].normalized)
                {
                    bool isCdata = true;

                    /* figure out whether declared as other than CDATA */
                    if (attId.maybeTokenized)
                    {
                        NAME_AND_DEFAULT_ATTRIBUTE nameAndDefaultAttribute =
                            lookup<NAME_AND_DEFAULT_ATTRIBUTE>(parser, elementType.defaultAttForName, attId.name, false);
                        if (nameAndDefaultAttribute != null)
                        {
                            DEFAULT_ATTRIBUTE att = elementType.defaultAtts[nameAndDefaultAttribute.attIndex];
                            isCdata = att.isCdata;
                        }
                    }

                    /* normalize the attribute value */
                    XML_Error result = storeAttributeValue(parser, enc, isCdata, buf, parser.m_atts[i].valuePtr,
                                                           parser.m_atts[i].valueEnd, parser.m_tempPool, account);
                    if (result != XML_ERROR_NONE)
                        return result;
                    appAtts[attIndex] = poolStart(parser.m_tempPool);
                    poolFinish(parser.m_tempPool);
                }
                else
                {
                    /* the value did not need normalizing */
                    appAtts[attIndex] = poolStoreString(parser.m_tempPool, enc, buf, parser.m_atts[i].valuePtr,
                                                        parser.m_atts[i].valueEnd);
                    if (appAtts[attIndex].IsNull)
                        return XML_ERROR_NO_MEMORY;
                    poolFinish(parser.m_tempPool);
                }
                /* handle prefixed attribute names */
                if (attId.prefix != null)
                {
                    if (attId.xmlns)
                    {
                        /* deal with namespace declarations here */
                        XML_Error result = addBinding(parser, attId.prefix, attId, appAtts[attIndex], ref bindingsPtr);
                        if (result != XML_ERROR_NONE)
                            return result;
                        --attIndex;
                    }
                    else
                    {
                        /* deal with other prefixed names later */
                        attIndex++;
                        nPrefixes++;
                        attId.name.Set(-1, 2);
                    }
                }
                else
                    attIndex++;
            }

            /* set-up for XML_GetSpecifiedAttributeCount and XML_GetIdAttributeIndex */
            parser.m_nSpecifiedAtts = attIndex;
            if (elementType.idAtt != null && elementType.idAtt.name[-1] != 0)
            {
                for (int i = 0; i < attIndex; i += 2)
                    if (appAtts[i] == elementType.idAtt.name)
                    {
                        parser.m_idAttIndex = i;
                        break;
                    }
            }
            else
                parser.m_idAttIndex = -1;

            /* do attribute defaulting */
            for (int i = 0; i < nDefaultAtts; i++)
            {
                DEFAULT_ATTRIBUTE da = elementType.defaultAtts[i];
                if (da.id.name[-1] == 0 && !da.value.IsNull)
                {
                    if (da.id.prefix != null)
                    {
                        if (da.id.xmlns)
                        {
                            XML_Error result = addBinding(parser, da.id.prefix, da.id, da.value, ref bindingsPtr);
                            if (result != XML_ERROR_NONE)
                                return result;
                        }
                        else
                        {
                            da.id.name.Set(-1, 2);
                            nPrefixes++;
                            appAtts[attIndex++] = da.id.name;
                            appAtts[attIndex++] = da.value;
                        }
                    }
                    else
                    {
                        da.id.name.Set(-1, 1);
                        appAtts[attIndex++] = da.id.name;
                        appAtts[attIndex++] = da.value;
                    }
                }
            }
            appAtts[attIndex] = XCHARP.NULL;

            /* expand prefixed attribute names, check for duplicates,
               and clear flags that say whether attributes were specified */
            int ii = 0;
            if (nPrefixes != 0)
            {
                uint j; /* hash table index */
                ulong version = parser.m_nsAttsVersion;

                /* Detect and prevent invalid shift */
                if (parser.m_nsAttsPower >= sizeof(uint) * 8 /* bits per byte */)
                    return XML_ERROR_NO_MEMORY;

                uint nsAttsSize = 1u << parser.m_nsAttsPower;
                byte oldNsAttsPower = parser.m_nsAttsPower;
                /* size of hash table must be at least 2 * (# of prefixed attributes) */
                if (parser.m_nsAttsPower == 0 || (nPrefixes >> (parser.m_nsAttsPower - 1)) != 0)
                {
                    /* hash table size must also be a power of 2 and >= 8 */
                    while ((nPrefixes >> parser.m_nsAttsPower++) != 0)
                        ;
                    if (parser.m_nsAttsPower < 3)
                        parser.m_nsAttsPower = 3;

                    /* Detect and prevent invalid shift */
                    if (parser.m_nsAttsPower >= sizeof(uint) * 8 /* bits per byte */)
                    {
                        /* Restore actual size of memory in m_nsAtts */
                        parser.m_nsAttsPower = oldNsAttsPower;
                        return XML_ERROR_NO_MEMORY;
                    }

                    nsAttsSize = 1u << parser.m_nsAttsPower;
                    long oldBytes = parser.m_nsAtts == null ? -1 : (long)parser.m_nsAtts.Length * NS_ATT.CSize;
                    if (!REALLOC(parser, oldBytes, (long)nsAttsSize * NS_ATT.CSize))
                    {
                        /* Restore actual size of memory in m_nsAtts */
                        parser.m_nsAttsPower = oldNsAttsPower;
                        return XML_ERROR_NO_MEMORY;
                    }
                    parser.m_nsAtts = new NS_ATT[nsAttsSize];
                    version = 0; /* force re-initialization of m_nsAtts hash table */
                }

                /* using a version flag saves us from initializing m_nsAtts every time */
                if (version == 0)
                { /* initialize version flags when version wraps around */
                    version = INIT_ATTS_VERSION;
                    for (j = nsAttsSize; j != 0;)
                        parser.m_nsAtts[--j].version = version;
                }
                parser.m_nsAttsVersion = --version;

                /* expand prefixed names and check for duplicates */
                for (; ii < attIndex; ii += 2)
                {
                    XCHARP s = appAtts[ii];
                    if (s[-1] == 2)
                    { /* prefixed */
                        var sip_state = new siphash(copy_salt_to_sipkey(parser));
                        s.Set(-1, 0); /* clear flag */
                        ATTRIBUTE_ID id = lookup<ATTRIBUTE_ID>(parser, dtd.attributeIds, s, false);
                        if (id == null || id.prefix == null)
                        {
                            /* This code is walking through the appAtts array, dealing
                             * with (in this case) a prefixed attribute name.  To be in
                             * the array, the attribute must have already been bound, so
                             * has to have passed through the hash table lookup once
                             * already.  That implies that an entry for it already
                             * exists, so the lookup above will return a pointer to
                             * already allocated memory.  There is no opportunaity for
                             * the allocator to fail, so the condition above cannot be
                             * fulfilled.
                             *
                             * Since it is difficult to be certain that the above
                             * analysis is complete, we retain the test and merely
                             * remove the code from coverage tests.
                             */
                            return XML_ERROR_NO_MEMORY; /* LCOV_EXCL_LINE */
                        }
                        BINDING b = id.prefix.binding;
                        if (b == null)
                            return XML_ERROR_UNBOUND_PREFIX;

                        if (!poolAppendChars(parser.m_tempPool, b.uri, 0, b.uriLen))
                            return XML_ERROR_NO_MEMORY;

                        sip_state.update(b.uri, 0, b.uriLen);

                        while (s[0] != (byte)':')
                            s = s + 1;
                        s = s + 1;

                        sip_state.update(s.a, s.i, keylen(s));

                        {
                            int len = s.Length + 1;
                            if (!poolAppendChars(parser.m_tempPool, s, len))
                                return XML_ERROR_NO_MEMORY;
                        }

                        ulong uriHash = sip_state.final();

                        { /* Check hash table for duplicate of expanded name (uriName).
                             Derived from code in lookup(parser, HASH_TABLE *table, ...).
                          */
                            byte step = 0;
                            ulong mask = nsAttsSize - 1;
                            j = (uint)(uriHash & mask); /* index into hash table */
                            while (parser.m_nsAtts[j].version == version)
                            {
                                /* for speed we compare stored hash values first */
                                if (uriHash == parser.m_nsAtts[j].hash)
                                {
                                    XCHARP s1 = poolStart(parser.m_tempPool);
                                    XCHARP s2 = parser.m_nsAtts[j].uriName;
                                    /* s1 is null terminated, but not s2 */
                                    int k = 0;
                                    for (; s1[k] == s2[k] && s1[k] != 0; k++)
                                        ;
                                    if (s1[k] == 0)
                                        return XML_ERROR_DUPLICATE_ATTRIBUTE;
                                }
                                if (step == 0)
                                    step = PROBE_STEP(uriHash, mask, parser.m_nsAttsPower);
                                if (j < step)
                                    j += nsAttsSize - step;
                                else
                                    j -= step;
                            }
                        }

                        if (parser.m_ns_triplets)
                        { /* append namespace separator and prefix */
                            parser.m_tempPool.cur[parser.m_tempPool.ptr - 1] = parser.m_namespaceSeparator;
                            s = b.prefix.name;
                            int len = s.Length + 1;
                            if (!poolAppendChars(parser.m_tempPool, s, len))
                                return XML_ERROR_NO_MEMORY;
                        }

                        /* store expanded name in attribute list */
                        s = poolStart(parser.m_tempPool);
                        poolFinish(parser.m_tempPool);
                        appAtts[ii] = s;

                        /* fill empty slot with new version, uriName and hash value */
                        parser.m_nsAtts[j].version = version;
                        parser.m_nsAtts[j].hash = uriHash;
                        parser.m_nsAtts[j].uriName = s;

                        if (--nPrefixes == 0)
                        {
                            ii += 2;
                            break;
                        }
                    }
                    else /* not prefixed */
                        s.Set(-1, 0); /* clear flag */
                }
            }
            /* clear flags for the remaining attributes */
            for (; ii < attIndex; ii += 2)
                appAtts[ii].Set(-1, 0);
            for (binding = bindingsPtr; binding != null; binding = binding.nextTagBinding)
                binding.attId.name.Set(-1, 0);

            if (!parser.m_ns)
                return XML_ERROR_NONE;

            /* expand the element type name */
            if (elementType.prefix != null)
            {
                binding = elementType.prefix.binding;
                if (binding == null)
                    return XML_ERROR_UNBOUND_PREFIX;
                localPart = tagNamePtr.str;
                while (localPart[0] != (byte)':')
                    localPart = localPart + 1;
                localPart = localPart + 1;
            }
            else if (dtd.defaultPrefix.binding != null)
            {
                binding = dtd.defaultPrefix.binding;
                localPart = tagNamePtr.str;
            }
            else
                return XML_ERROR_NONE;
            int prefixLen = 0;
            if (parser.m_ns_triplets && !binding.prefix.name.IsNull)
                prefixLen = binding.prefix.name.Length + 1;
            tagNamePtr.localPart = localPart;
            tagNamePtr.uriLen = binding.uriLen;
            tagNamePtr.prefix = binding.prefix.name;
            tagNamePtr.prefixLen = prefixLen;
            int localPartLen = localPart.Length + 1;

            int totalLen = localPartLen + binding.uriLen + prefixLen;
            if (totalLen > binding.uriAlloc)
            {
                if (!MALLOC(parser, totalLen + EXPAND_SPARE))
                    return XML_ERROR_NO_MEMORY;
                var newUri = new byte[totalLen + EXPAND_SPARE];
                binding.uriAlloc = totalLen + EXPAND_SPARE;
                Buffer.BlockCopy(binding.uri, 0, newUri, 0, binding.uriLen);
                for (TAG p = parser.m_tagStack; p != null; p = p.parent)
                    if (p.name.str == new XCHARP(binding.uri, 0))
                        p.name.str = new XCHARP(newUri, 0);
                FREE(parser, binding.uri.Length);
                binding.uri = newUri;
            }
            /* if m_namespaceSeparator != '\0' then uri includes it already */
            uri = new XCHARP(binding.uri, binding.uriLen);
            Buffer.BlockCopy(localPart.a, localPart.i, uri.a, uri.i, localPartLen);
            /* we always have a namespace separator between localPart and prefix */
            if (prefixLen != 0)
            {
                uri = uri + (localPartLen - 1);
                uri.Set(0, parser.m_namespaceSeparator); /* replace null terminator */
                Buffer.BlockCopy(binding.prefix.name.a, binding.prefix.name.i, uri.a, uri.i + 1, prefixLen);
            }
            tagNamePtr.str = new XCHARP(binding.uri, 0);
            return XML_ERROR_NONE;
        }

        /// <summary>is_rfc3986_uri_char</summary>
        private static bool is_rfc3986_uri_char(byte candidate)
        {
            // For the RFC 3986 ANBF grammar see
            // https://datatracker.ietf.org/doc/html/rfc3986#appendix-A
            if ((candidate >= 'A' && candidate <= 'Z') || (candidate >= 'a' && candidate <= 'z')
                || (candidate >= '0' && candidate <= '9'))
                return true;
            switch ((char)candidate)
            {
                case '%': // pct-encoded
                case '-': // unreserved
                case '.':
                case '_':
                case '~':
                case ':': // gen-delims
                case '/':
                case '?':
                case '#':
                case '[':
                case ']':
                case '@':
                case '!': // sub-delims
                case '$':
                case '&':
                case '\'':
                case '(':
                case ')':
                case '*':
                case '+':
                case ',':
                case ';':
                case '=':
                    return true;
                default:
                    return false;
            }
        }

        private static readonly byte[] xmlNamespace = XmlTok.Ascii("http://www.w3.org/XML/1998/namespace");
        private static readonly byte[] xmlnsNamespace = XmlTok.Ascii("http://www.w3.org/2000/xmlns/");

        /// <summary>
        /// addBinding: prefix must be non-NULL; attId can be NULL, but must be non-NULL if
        /// bindingsPtr is not NULL; uri must be non-NULL.
        /// </summary>
        private static XML_Error addBinding(XML_Parser parser, PREFIX prefix, ATTRIBUTE_ID attId, XCHARP uri, ref BINDING bindingsPtr)
        {
            int xmlLen = xmlNamespace.Length;
            int xmlnsLen = xmlnsNamespace.Length;

            bool mustBeXML = false;
            bool isXML = true;
            bool isXMLNS = true;

            BINDING b;
            int len;

            /* empty URI is only valid for default namespace per XML NS 1.0 (not 1.1) */
            if (uri[0] == 0 && !prefix.name.IsNull)
                return XML_ERROR_UNDECLARING_PREFIX;

            if (!prefix.name.IsNull && prefix.name[0] == (byte)'x' && prefix.name[1] == (byte)'m'
                && prefix.name[2] == (byte)'l')
            {
                /* Not allowed to bind xmlns */
                if (prefix.name[3] == (byte)'n' && prefix.name[4] == (byte)'s' && prefix.name[5] == 0)
                    return XML_ERROR_RESERVED_PREFIX_XMLNS;

                if (prefix.name[3] == 0)
                    mustBeXML = true;
            }

            for (len = 0; uri[len] != 0; len++)
            {
                if (isXML && (len >= xmlLen || uri[len] != xmlNamespace[len]))
                    isXML = false;

                if (!mustBeXML && isXMLNS && (len >= xmlnsLen || uri[len] != xmlnsNamespace[len]))
                    isXMLNS = false;

                // NOTE: While Expat does not validate namespace URIs against RFC 3986
                //       today (and is not REQUIRED to do so with regard to the XML 1.0
                //       namespaces specification) we have to at least make sure, that
                //       the application on top of Expat (that is likely splitting expanded
                //       element names ("qualified names") of form
                //       "[uri sep] local [sep prefix] '\0'" back into 1, 2 or 3 pieces
                //       in its element handler code) cannot be confused by an attacker
                //       putting additional namespace separator characters into namespace
                //       declarations.  That would be ambiguous and not to be expected.
                //
                //       While the HTML API docs of function XML_ParserCreateNS have been
                //       advising against use of a namespace separator character that can
                //       appear in a URI for >20 years now, some widespread applications
                //       are using URI characters (':' (colon) in particular) for a
                //       namespace separator, in practice.  To keep these applications
                //       functional, we only reject namespaces URIs containing the
                //       application-chosen namespace separator if the chosen separator
                //       is a non-URI character with regard to RFC 3986.
                if (parser.m_ns && uri[len] == parser.m_namespaceSeparator && !is_rfc3986_uri_char(uri[len]))
                    return XML_ERROR_SYNTAX;
            }
            isXML = isXML && len == xmlLen;
            isXMLNS = isXMLNS && len == xmlnsLen;

            if (mustBeXML != isXML)
                return mustBeXML ? XML_ERROR_RESERVED_PREFIX_XML : XML_ERROR_RESERVED_NAMESPACE_URI;

            if (isXMLNS)
                return XML_ERROR_RESERVED_NAMESPACE_URI;

            if (parser.m_namespaceSeparator != 0)
                len++;
            if (parser.m_freeBindingList != null)
            {
                b = parser.m_freeBindingList;
                if (len > b.uriAlloc)
                {
                    if (!REALLOC(parser, b.uriAlloc, len + EXPAND_SPARE))
                        return XML_ERROR_NO_MEMORY;
                    var temp = new byte[len + EXPAND_SPARE];
                    Buffer.BlockCopy(b.uri, 0, temp, 0, b.uriAlloc);
                    b.uri = temp;
                    b.uriAlloc = len + EXPAND_SPARE;
                }
                parser.m_freeBindingList = b.nextTagBinding;
            }
            else
            {
                if (!MALLOC(parser, BINDING.CSize))
                    return XML_ERROR_NO_MEMORY;
                b = new BINDING();
                if (!MALLOC(parser, len + EXPAND_SPARE))
                {
                    FREE(parser, BINDING.CSize);
                    return XML_ERROR_NO_MEMORY;
                }
                b.uri = new byte[len + EXPAND_SPARE];
                b.uriAlloc = len + EXPAND_SPARE;
            }
            b.uriLen = len;
            Buffer.BlockCopy(uri.a, uri.i, b.uri, 0, len);
            if (parser.m_namespaceSeparator != 0)
                b.uri[len - 1] = parser.m_namespaceSeparator;
            b.prefix = prefix;
            b.attId = attId;
            b.prevPrefixBinding = prefix.binding;
            /* NULL binding when default namespace undeclared */
            if (uri[0] == 0 && prefix == parser.m_dtd.defaultPrefix)
                prefix.binding = null;
            else
                prefix.binding = b;
            b.nextTagBinding = bindingsPtr;
            bindingsPtr = b;
            /* if attId == NULL then we are not starting a namespace scope */
            if (attId != null && parser.m_startNamespaceDeclHandler != null)
            {
                beforeHandler(parser);
                try { parser.m_startNamespaceDeclHandler(parser.m_handlerArg, prefix.name, prefix.binding != null ? uri : XCHARP.NULL); }
                finally { afterHandler(parser); }
            }
            return XML_ERROR_NONE;
        }

        /// <summary>
        /// cdataSectionProcessor: the idea here is to avoid using stack for each CDATA section
        /// when the whole file is parsed with one call.
        /// </summary>
        private static XML_Error cdataSectionProcessor(XML_Parser parser, int start, int end, ref int endPtr)
        {
            XML_Error result = doCdataSection(parser, parser.m_encoding, parser.m_buffer, ref start, end, ref endPtr,
                                              !parser.m_parsingStatus.finalBuffer, XML_Account.XML_ACCOUNT_DIRECT);
            if (result != XML_ERROR_NONE)
                return result;
            if (start >= 0)
            {
                if (parser.m_parentParser != null)
                { /* we are parsing an external entity */
                    parser.m_processor = externalEntityContentProcessor;
                    return externalEntityContentProcessor(parser, start, end, ref endPtr);
                }
                else
                {
                    parser.m_processor = contentProcessor;
                    return contentProcessor(parser, start, end, ref endPtr);
                }
            }
            return result;
        }

        /// <summary>
        /// doCdataSection: startPtr gets set to non-null if the section is closed, and to null if
        /// the section is not yet closed (-1 here).
        /// </summary>
        private static XML_Error doCdataSection(XML_Parser parser, ENCODING enc, byte[] buf, ref int startPtr, int end,
                                                ref int nextPtr, bool haveMore, XML_Account account)
        {
            int s = startPtr;
            ref int eventPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventPtr
                : ref parser.m_openInternalEntities.internalEventPtr);
            ref int eventEndPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventEndPtr
                : ref parser.m_openInternalEntities.internalEventEndPtr);
            eventPP = s;
            startPtr = -1;

            for (; ; )
            {
                int next = s; /* in case of XML_TOK_NONE or XML_TOK_PARTIAL */
                int tok = enc.XmlCdataSectionTok(buf, s, end, ref next);
                if (!accountingDiffTolerated(parser, tok, s, next, account))
                {
                    accountingOnAbort(parser);
                    return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                }
                eventEndPP = next;
                switch (tok)
                {
                    case XML_TOK_CDATA_SECT_CLOSE:
                        if (parser.m_endCdataSectionHandler != null)
                        {
                            beforeHandler(parser);
                            try { parser.m_endCdataSectionHandler(parser.m_handlerArg); }
                            finally { afterHandler(parser); }
                        }
                        else if (parser.m_defaultHandler != null)
                            reportDefault(parser, enc, buf, s, next);
                        startPtr = next;
                        nextPtr = next;
                        if (parser.m_parsingStatus.parsing == XML_FINISHED)
                            return XML_ERROR_ABORTED;
                        else
                            return XML_ERROR_NONE;
                    case XML_TOK_DATA_NEWLINE:
                        if (parser.m_characterDataHandler != null)
                        {
                            beforeHandler(parser);
                            try { parser.m_characterDataHandler(parser.m_handlerArg, new XCHARP(s_newline, 0), 1); }
                            finally { afterHandler(parser); }
                        }
                        else if (parser.m_defaultHandler != null)
                            reportDefault(parser, enc, buf, s, next);
                        break;
                    case XML_TOK_DATA_CHARS:
                        {
                            XML_CharacterDataHandler charDataHandler = parser.m_characterDataHandler;
                            if (charDataHandler != null)
                            {
                                if (MUST_CONVERT(enc))
                                {
                                    for (; ; )
                                    {
                                        int dataPtr = 0;
                                        XML_Convert_Result convert_res = XmlConvert(enc, buf, ref s, next, parser.m_dataBuf, ref dataPtr, parser.m_dataBuf.Length);
                                        eventEndPP = next;
                                        beforeHandler(parser);
                                        try { charDataHandler(parser.m_handlerArg, new XCHARP(parser.m_dataBuf, 0), dataPtr); }
                                        finally { afterHandler(parser); }
                                        if (convert_res == XML_Convert_Result.XML_CONVERT_COMPLETED
                                            || convert_res == XML_Convert_Result.XML_CONVERT_INPUT_INCOMPLETE)
                                            break;
                                        eventPP = s;
                                    }
                                }
                                else
                                {
                                    beforeHandler(parser);
                                    try { charDataHandler(parser.m_handlerArg, new XCHARP(buf, s), next - s); }
                                    finally { afterHandler(parser); }
                                }
                            }
                            else if (parser.m_defaultHandler != null)
                                reportDefault(parser, enc, buf, s, next);
                        }
                        break;
                    case XML_TOK_INVALID:
                        eventPP = next;
                        return XML_ERROR_INVALID_TOKEN;
                    case XML_TOK_PARTIAL_CHAR:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_PARTIAL_CHAR;
                    case XML_TOK_PARTIAL:
                    case XML_TOK_NONE:
                        if (haveMore)
                        {
                            nextPtr = s;
                            return XML_ERROR_NONE;
                        }
                        return XML_ERROR_UNCLOSED_CDATA_SECTION;
                    default:
                        /* Every token returned by XmlCdataSectionTok() has its own
                         * explicit case, so this default case will never be executed.
                         * We retain it as a safety net and exclude it from the coverage
                         * statistics.
                         *
                         * LCOV_EXCL_START
                         */
                        eventPP = next;
                        return XML_ERROR_UNEXPECTED_STATE;
                        /* LCOV_EXCL_STOP */
                }

                switch (parser.m_parsingStatus.parsing)
                {
                    case XML_SUSPENDED:
                        eventPP = next;
                        nextPtr = next;
                        return XML_ERROR_NONE;
                    case XML_FINISHED:
                        eventPP = next;
                        return XML_ERROR_ABORTED;
                    case XML_PARSING:
                        if (parser.m_reenter)
                            return XML_ERROR_UNEXPECTED_STATE; // LCOV_EXCL_LINE
                        eventPP = s = next;
                        break;
                    default:
                        eventPP = s = next;
                        break;
                }
            }
            /* not reached */
        }

        /// <summary>
        /// ignoreSectionProcessor: the idea here is to avoid using stack for each IGNORE section
        /// when the whole file is parsed with one call.
        /// </summary>
        private static XML_Error ignoreSectionProcessor(XML_Parser parser, int start, int end, ref int endPtr)
        {
            XML_Error result = doIgnoreSection(parser, parser.m_encoding, parser.m_buffer, ref start, end, ref endPtr,
                                               !parser.m_parsingStatus.finalBuffer);
            if (result != XML_ERROR_NONE)
                return result;
            if (start >= 0)
            {
                parser.m_processor = prologProcessor;
                return prologProcessor(parser, start, end, ref endPtr);
            }
            return result;
        }

        /// <summary>
        /// doIgnoreSection: startPtr gets set to non-null if the section is closed, and to null if
        /// the section is not yet closed.
        /// </summary>
        private static XML_Error doIgnoreSection(XML_Parser parser, ENCODING enc, byte[] buf, ref int startPtr, int end,
                                                 ref int nextPtr, bool haveMore)
        {
            int next = startPtr; /* in case of XML_TOK_NONE or XML_TOK_PARTIAL */
            int tok;
            int s = startPtr;
            ref int eventPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventPtr
                : ref parser.m_openInternalEntities.internalEventPtr);
            ref int eventEndPP = ref (enc == parser.m_encoding
                ? ref parser.m_eventEndPtr
                : ref parser.m_openInternalEntities.internalEventEndPtr);
            eventPP = s;
            startPtr = -1;
            tok = enc.XmlIgnoreSectionTok(buf, s, end, ref next);
            if (!accountingDiffTolerated(parser, tok, s, next, XML_Account.XML_ACCOUNT_DIRECT))
            {
                accountingOnAbort(parser);
                return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
            }
            eventEndPP = next;
            switch (tok)
            {
                case XML_TOK_IGNORE_SECT:
                    if (parser.m_defaultHandler != null)
                        reportDefault(parser, enc, buf, s, next);
                    startPtr = next;
                    nextPtr = next;
                    if (parser.m_parsingStatus.parsing == XML_FINISHED)
                        return XML_ERROR_ABORTED;
                    else
                        return XML_ERROR_NONE;
                case XML_TOK_INVALID:
                    eventPP = next;
                    return XML_ERROR_INVALID_TOKEN;
                case XML_TOK_PARTIAL_CHAR:
                    if (haveMore)
                    {
                        nextPtr = s;
                        return XML_ERROR_NONE;
                    }
                    return XML_ERROR_PARTIAL_CHAR;
                case XML_TOK_PARTIAL:
                case XML_TOK_NONE:
                    if (haveMore)
                    {
                        nextPtr = s;
                        return XML_ERROR_NONE;
                    }
                    return XML_ERROR_SYNTAX; /* XML_ERROR_UNCLOSED_IGNORE_SECTION */
                default:
                    /* All of the tokens that XmlIgnoreSectionTok() returns have
                     * explicit cases to handle them, so this default case is never
                     * executed.  We keep it as a safety net anyway, and remove it
                     * from our test coverage statistics.
                     *
                     * LCOV_EXCL_START
                     */
                    eventPP = next;
                    return XML_ERROR_UNEXPECTED_STATE;
                    /* LCOV_EXCL_STOP */
            }
            /* not reached */
        }

        /// <summary>initializeEncoding</summary>
        private static XML_Error initializeEncoding(XML_Parser parser)
        {
            byte[] s = parser.m_protocolEncodingName.IsNull ? null : parser.m_protocolEncodingName.ToArray();
            if (XmlInitEncoding(parser.m_initEncoding, e => parser.m_encoding = e, s, parser.m_ns))
                return XML_ERROR_NONE;
            return handleUnknownEncoding(parser, parser.m_protocolEncodingName);
        }

        /// <summary>processXmlDecl</summary>
        private static XML_Error processXmlDecl(XML_Parser parser, bool isGeneralTextEntity, int s, int next)
        {
            int encodingName = -1;
            XCHARP storedEncName = XCHARP.NULL;
            ENCODING newEncoding = null;
            int version = -1;
            int versionend = -1;
            XCHARP storedversion = XCHARP.NULL;
            int standalone = -1;

            if (!accountingDiffTolerated(parser, XML_TOK_XML_DECL, s, next, XML_Account.XML_ACCOUNT_DIRECT))
            {
                accountingOnAbort(parser);
                return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
            }

            if (!XmlParseXmlDecl(parser.m_ns, isGeneralTextEntity, parser.m_encoding, parser.m_buffer, s, next,
                                 ref parser.m_eventPtr, ref version, ref versionend, ref encodingName, ref newEncoding,
                                 ref standalone))
            {
                if (isGeneralTextEntity)
                    return XML_ERROR_TEXT_DECL;
                else
                    return XML_ERROR_XML_DECL;
            }
            if (!isGeneralTextEntity && standalone == 1)
            {
                parser.m_dtd.standalone = true;
                if (parser.m_paramEntityParsing == XML_ParamEntityParsing.XML_PARAM_ENTITY_PARSING_UNLESS_STANDALONE)
                    parser.m_paramEntityParsing = XML_ParamEntityParsing.XML_PARAM_ENTITY_PARSING_NEVER;
            }
            if (parser.m_xmlDeclHandler != null)
            {
                if (encodingName >= 0)
                {
                    storedEncName = poolStoreString(parser.m_temp2Pool, parser.m_encoding, parser.m_buffer, encodingName,
                                                    encodingName + parser.m_encoding.NameLength(parser.m_buffer, encodingName));
                    if (storedEncName.IsNull)
                        return XML_ERROR_NO_MEMORY;
                    poolFinish(parser.m_temp2Pool);
                }
                if (version >= 0)
                {
                    storedversion = poolStoreString(parser.m_temp2Pool, parser.m_encoding, parser.m_buffer, version,
                                                    versionend - parser.m_encoding.minBytesPerChar);
                    if (storedversion.IsNull)
                        return XML_ERROR_NO_MEMORY;
                }
                beforeHandler(parser);
                try { parser.m_xmlDeclHandler(parser.m_handlerArg, storedversion, storedEncName, standalone); }
                finally { afterHandler(parser); }
            }
            else if (parser.m_defaultHandler != null)
                reportDefault(parser, parser.m_encoding, parser.m_buffer, s, next);
            if (parser.m_protocolEncodingName.IsNull)
            {
                if (newEncoding != null)
                {
                    /* Check that the specified encoding does not conflict with what
                     * the parser has already deduced.  Do we have the same number
                     * of bytes in the smallest representation of a character?  If
                     * this is UTF-16, is it the same endianness?
                     */
                    if (newEncoding.minBytesPerChar != parser.m_encoding.minBytesPerChar
                        || (newEncoding.minBytesPerChar == 2 && newEncoding != parser.m_encoding))
                    {
                        parser.m_eventPtr = encodingName;
                        return XML_ERROR_INCORRECT_ENCODING;
                    }
                    parser.m_encoding = newEncoding;
                }
                else if (encodingName >= 0)
                {
                    XML_Error result;
                    if (storedEncName.IsNull)
                    {
                        storedEncName = poolStoreString(parser.m_temp2Pool, parser.m_encoding, parser.m_buffer, encodingName,
                                                        encodingName + parser.m_encoding.NameLength(parser.m_buffer, encodingName));
                        if (storedEncName.IsNull)
                            return XML_ERROR_NO_MEMORY;
                    }
                    result = handleUnknownEncoding(parser, storedEncName);
                    poolClear(parser.m_temp2Pool);
                    if (result == XML_ERROR_UNKNOWN_ENCODING)
                        parser.m_eventPtr = encodingName;
                    return result;
                }
            }

            if (!storedEncName.IsNull || !storedversion.IsNull)
                poolClear(parser.m_temp2Pool);

            return XML_ERROR_NONE;
        }

        /// <summary>handleUnknownEncoding</summary>
        private static XML_Error handleUnknownEncoding(XML_Parser parser, XCHARP encodingName)
        {
            if (parser.m_unknownEncodingHandler != null)
            {
                var info = new XML_Encoding();
                int i;
                for (i = 0; i < 256; i++)
                    info.map[i] = -1;
                info.convert = null;
                info.data = null;
                info.release = null;
                int status;
                beforeHandler(parser);
                try { status = parser.m_unknownEncodingHandler(parser.m_unknownEncodingHandlerData, encodingName, info); }
                finally { afterHandler(parser); }
                parser.m_unknownEncodingRelease = info.release;
                parser.m_unknownEncodingData = info.data;
                if (status != 0)
                {
                    ENCODING enc;
                    if (!MALLOC(parser, 3112 /* XmlSizeOfUnknownEncoding() */))
                    {
                        if (parser.m_unknownEncodingRelease != null)
                            callUnknownEncodingRelease(parser);
                        else
                            parser.m_unknownEncodingData = null;
                        return XML_ERROR_NO_MEMORY;
                    }
                    parser.m_unknownEncodingMem = true;
                    parser.m_unknownEncodingConvert = info.convert;
                    enc = parser.m_ns
                        ? XmlInitUnknownEncodingNS(info.map, info.convert != null ? callUnknownEncodingConvert : null, parser)
                        : XmlInitUnknownEncoding(info.map, info.convert != null ? callUnknownEncodingConvert : null, parser);
                    if (enc != null)
                    {
                        parser.m_encoding = enc;
                        return XML_ERROR_NONE;
                    }
                    parser.m_unknownEncodingConvert = null;
                }
                if (parser.m_unknownEncodingRelease != null)
                    callUnknownEncodingRelease(parser);
                else
                    parser.m_unknownEncodingData = null;
            }
            return XML_ERROR_UNKNOWN_ENCODING;
        }

        /// <summary>prologInitProcessor</summary>
        private static XML_Error prologInitProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            XML_Error result = initializeEncoding(parser);
            if (result != XML_ERROR_NONE)
                return result;
            parser.m_processor = prologProcessor;
            return prologProcessor(parser, s, end, ref nextPtr);
        }

        /// <summary>externalParEntInitProcessor</summary>
        private static XML_Error externalParEntInitProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            XML_Error result = initializeEncoding(parser);
            if (result != XML_ERROR_NONE)
                return result;

            /* we know now that XML_Parse(Buffer) has been called,
               so we consider the external parameter entity read */
            parser.m_dtd.paramEntityRead = true;

            if (parser.m_prologState.inEntityValue)
            {
                parser.m_processor = entityValueInitProcessor;
                return entityValueInitProcessor(parser, s, end, ref nextPtr);
            }
            else
            {
                parser.m_processor = externalParEntProcessor;
                return externalParEntProcessor(parser, s, end, ref nextPtr);
            }
        }

        /// <summary>entityValueInitProcessor</summary>
        private static XML_Error entityValueInitProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            int tok;
            int start = s;
            int next = start;
            parser.m_eventPtr = start;

            for (; ; )
            {
                tok = parser.m_encoding.XmlPrologTok(parser.m_buffer, start, end, ref next);
                /* Note: Except for XML_TOK_BOM below, these bytes are accounted later in:
                         - storeEntityValue
                         - processXmlDecl
                */
                parser.m_eventEndPtr = next;
                if (tok <= 0)
                {
                    if (!parser.m_parsingStatus.finalBuffer && tok != XML_TOK_INVALID)
                    {
                        nextPtr = s;
                        return XML_ERROR_NONE;
                    }
                    switch (tok)
                    {
                        case XML_TOK_INVALID:
                            return XML_ERROR_INVALID_TOKEN;
                        case XML_TOK_PARTIAL:
                            return XML_ERROR_UNCLOSED_TOKEN;
                        case XML_TOK_PARTIAL_CHAR:
                            return XML_ERROR_PARTIAL_CHAR;
                        case XML_TOK_NONE: /* start == end */
                        default:
                            break;
                    }
                    /* found end of entity value - can store it now */
                    int dummy = 0;
                    return storeEntityValue(parser, parser.m_encoding, parser.m_buffer, s, end, XML_Account.XML_ACCOUNT_DIRECT, ref dummy);
                }
                else if (tok == XML_TOK_XML_DECL)
                {
                    XML_Error result = processXmlDecl(parser, false, start, next);
                    if (result != XML_ERROR_NONE)
                        return result;
                    /* At this point, m_parsingStatus.parsing cannot be XML_SUSPENDED.  For
                     * that to happen, a parameter entity parsing handler must have attempted
                     * to suspend the parser, which fails and raises an error.  The parser can
                     * be aborted, but can't be suspended.
                     */
                    if (parser.m_parsingStatus.parsing == XML_FINISHED)
                        return XML_ERROR_ABORTED;
                    nextPtr = next;
                    /* stop scanning for text declaration - we found one */
                    parser.m_processor = entityValueProcessor;
                    return entityValueProcessor(parser, next, end, ref nextPtr);
                }
                /* XmlPrologTok has now set the encoding based on the BOM it found, and we
                   must move s and nextPtr forward to consume the BOM.

                   If we didn't, and got XML_TOK_NONE from the next XmlPrologTok call, we
                   would leave the BOM in the buffer and return. On the next call to this
                   function, our XmlPrologTok call would return XML_TOK_INVALID, since it
                   is not valid to have multiple BOMs.
                */
                else if (tok == XML_TOK_BOM)
                {
                    if (!accountingDiffTolerated(parser, tok, s, next, XML_Account.XML_ACCOUNT_DIRECT))
                    {
                        accountingOnAbort(parser);
                        return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                    }
                    nextPtr = next;
                    s = next;
                }
                /* If we get this token, we have the start of what might be a
                   normal tag, but not a declaration (i.e. it doesn't begin with
                   "<!").  In a DTD context, that isn't legal.
                */
                else if (tok == XML_TOK_INSTANCE_START)
                {
                    nextPtr = next;
                    return XML_ERROR_SYNTAX;
                }
                start = next;
                parser.m_eventPtr = start;
            }
        }

        /// <summary>externalParEntProcessor</summary>
        private static XML_Error externalParEntProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            int next = s;
            int tok;

            tok = parser.m_encoding.XmlPrologTok(parser.m_buffer, s, end, ref next);
            if (tok <= 0)
            {
                if (!parser.m_parsingStatus.finalBuffer && tok != XML_TOK_INVALID)
                {
                    nextPtr = s;
                    return XML_ERROR_NONE;
                }
                switch (tok)
                {
                    case XML_TOK_INVALID:
                        return XML_ERROR_INVALID_TOKEN;
                    case XML_TOK_PARTIAL:
                        return XML_ERROR_UNCLOSED_TOKEN;
                    case XML_TOK_PARTIAL_CHAR:
                        return XML_ERROR_PARTIAL_CHAR;
                    case XML_TOK_NONE: /* start == end */
                    default:
                        break;
                }
            }
            /* This would cause the next stage, i.e. doProlog to be passed XML_TOK_BOM.
               However, when parsing an external subset, doProlog will not accept a BOM
               as valid, and report a syntax error, so we have to skip the BOM, and
               account for the BOM bytes.
            */
            else if (tok == XML_TOK_BOM)
            {
                if (!accountingDiffTolerated(parser, tok, s, next, XML_Account.XML_ACCOUNT_DIRECT))
                {
                    accountingOnAbort(parser);
                    return XML_ERROR_AMPLIFICATION_LIMIT_BREACH;
                }
                s = next;
                tok = parser.m_encoding.XmlPrologTok(parser.m_buffer, s, end, ref next);
            }

            parser.m_processor = prologProcessor;
            return doProlog(parser, parser.m_encoding, parser.m_buffer, s, end, tok, next, ref nextPtr,
                            !parser.m_parsingStatus.finalBuffer, true, XML_Account.XML_ACCOUNT_DIRECT);
        }

        /// <summary>entityValueProcessor</summary>
        private static XML_Error entityValueProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            int start = s;
            int next = s;
            ENCODING enc = parser.m_encoding;
            int tok;

            for (; ; )
            {
                tok = enc.XmlPrologTok(parser.m_buffer, start, end, ref next);
                /* Note: These bytes are accounted later in:
                         - storeEntityValue
                */
                if (tok <= 0)
                {
                    if (!parser.m_parsingStatus.finalBuffer && tok != XML_TOK_INVALID)
                    {
                        nextPtr = s;
                        return XML_ERROR_NONE;
                    }
                    switch (tok)
                    {
                        case XML_TOK_INVALID:
                            return XML_ERROR_INVALID_TOKEN;
                        case XML_TOK_PARTIAL:
                            return XML_ERROR_UNCLOSED_TOKEN;
                        case XML_TOK_PARTIAL_CHAR:
                            return XML_ERROR_PARTIAL_CHAR;
                        case XML_TOK_NONE: /* start == end */
                        default:
                            break;
                    }
                    /* found end of entity value - can store it now */
                    int dummy = 0;
                    return storeEntityValue(parser, enc, parser.m_buffer, s, end, XML_Account.XML_ACCOUNT_DIRECT, ref dummy);
                }
                /* If we get this token, we have the start of what might be a
                   normal tag, but not a declaration (i.e. it doesn't begin with
                   "<!").  In a DTD context, that isn't legal.
                */
                else if (tok == XML_TOK_INSTANCE_START)
                {
                    nextPtr = next;
                    return XML_ERROR_SYNTAX;
                }
                start = next;
            }
        }

        /// <summary>prologProcessor</summary>
        private static XML_Error prologProcessor(XML_Parser parser, int s, int end, ref int nextPtr)
        {
            int next = s;
            int tok = parser.m_encoding.XmlPrologTok(parser.m_buffer, s, end, ref next);
            return doProlog(parser, parser.m_encoding, parser.m_buffer, s, end, tok, next, ref nextPtr,
                            !parser.m_parsingStatus.finalBuffer, true, XML_Account.XML_ACCOUNT_DIRECT);
        }
    }
}
