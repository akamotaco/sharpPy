// Modules/expat/xmlrole.c (expat 2.8.5), compiled with XML_DTD: the prolog state machine, one
// method per C function. Translated from the C file by a one-off script (the C text with
// PROLOG_STATE* as a class and buffer offsets for pointers).
using static SharpPy.Modules.Expat.XmlTok;

namespace SharpPy.Modules.Expat
{
    internal delegate int PROLOG_HANDLER(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc);

    /// <summary>struct prolog_state</summary>
    internal sealed class PROLOG_STATE
    {
        internal PROLOG_HANDLER handler;
        internal uint level;
        internal int role_none;
        internal uint includeLevel;
        internal bool documentEntity;
        internal bool inEntityValue;

        // XmlTokenRole
        internal int XmlTokenRole(int tok, byte[] buf, int ptr, int end, ENCODING enc) => handler(this, tok, buf, ptr, end, enc);
    }

    internal static class XmlRole
    {
        internal const int XML_ROLE_ERROR = -1;
        internal const int XML_ROLE_NONE = 0,
            XML_ROLE_XML_DECL = 1,
            XML_ROLE_INSTANCE_START = 2,
            XML_ROLE_DOCTYPE_NONE = 3,
            XML_ROLE_DOCTYPE_NAME = 4,
            XML_ROLE_DOCTYPE_SYSTEM_ID = 5,
            XML_ROLE_DOCTYPE_PUBLIC_ID = 6,
            XML_ROLE_DOCTYPE_INTERNAL_SUBSET = 7,
            XML_ROLE_DOCTYPE_CLOSE = 8,
            XML_ROLE_GENERAL_ENTITY_NAME = 9,
            XML_ROLE_PARAM_ENTITY_NAME = 10,
            XML_ROLE_ENTITY_NONE = 11,
            XML_ROLE_ENTITY_VALUE = 12,
            XML_ROLE_ENTITY_SYSTEM_ID = 13,
            XML_ROLE_ENTITY_PUBLIC_ID = 14,
            XML_ROLE_ENTITY_COMPLETE = 15,
            XML_ROLE_ENTITY_NOTATION_NAME = 16,
            XML_ROLE_NOTATION_NONE = 17,
            XML_ROLE_NOTATION_NAME = 18,
            XML_ROLE_NOTATION_SYSTEM_ID = 19,
            XML_ROLE_NOTATION_NO_SYSTEM_ID = 20,
            XML_ROLE_NOTATION_PUBLIC_ID = 21,
            XML_ROLE_ATTRIBUTE_NAME = 22,
            XML_ROLE_ATTRIBUTE_TYPE_CDATA = 23,
            XML_ROLE_ATTRIBUTE_TYPE_ID = 24,
            XML_ROLE_ATTRIBUTE_TYPE_IDREF = 25,
            XML_ROLE_ATTRIBUTE_TYPE_IDREFS = 26,
            XML_ROLE_ATTRIBUTE_TYPE_ENTITY = 27,
            XML_ROLE_ATTRIBUTE_TYPE_ENTITIES = 28,
            XML_ROLE_ATTRIBUTE_TYPE_NMTOKEN = 29,
            XML_ROLE_ATTRIBUTE_TYPE_NMTOKENS = 30,
            XML_ROLE_ATTRIBUTE_ENUM_VALUE = 31,
            XML_ROLE_ATTRIBUTE_NOTATION_VALUE = 32,
            XML_ROLE_ATTLIST_NONE = 33,
            XML_ROLE_ATTLIST_ELEMENT_NAME = 34,
            XML_ROLE_IMPLIED_ATTRIBUTE_VALUE = 35,
            XML_ROLE_REQUIRED_ATTRIBUTE_VALUE = 36,
            XML_ROLE_DEFAULT_ATTRIBUTE_VALUE = 37,
            XML_ROLE_FIXED_ATTRIBUTE_VALUE = 38,
            XML_ROLE_ELEMENT_NONE = 39,
            XML_ROLE_ELEMENT_NAME = 40,
            XML_ROLE_CONTENT_ANY = 41,
            XML_ROLE_CONTENT_EMPTY = 42,
            XML_ROLE_CONTENT_PCDATA = 43,
            XML_ROLE_GROUP_OPEN = 44,
            XML_ROLE_GROUP_CLOSE = 45,
            XML_ROLE_GROUP_CLOSE_REP = 46,
            XML_ROLE_GROUP_CLOSE_OPT = 47,
            XML_ROLE_GROUP_CLOSE_PLUS = 48,
            XML_ROLE_GROUP_CHOICE = 49,
            XML_ROLE_GROUP_SEQUENCE = 50,
            XML_ROLE_CONTENT_ELEMENT = 51,
            XML_ROLE_CONTENT_ELEMENT_REP = 52,
            XML_ROLE_CONTENT_ELEMENT_OPT = 53,
            XML_ROLE_CONTENT_ELEMENT_PLUS = 54,
            XML_ROLE_PI = 55,
            XML_ROLE_COMMENT = 56,
            XML_ROLE_TEXT_DECL = 57,
            XML_ROLE_IGNORE_SECT = 58,
            XML_ROLE_INNER_PARAM_ENTITY_REF = 59,
            XML_ROLE_PARAM_ENTITY_REF = 60;

        private static readonly byte[] KW_ANY = XmlTok.Ascii("ANY");
        private static readonly byte[] KW_ATTLIST = XmlTok.Ascii("ATTLIST");
        private static readonly byte[] KW_CDATA = XmlTok.Ascii("CDATA");
        private static readonly byte[] KW_DOCTYPE = XmlTok.Ascii("DOCTYPE");
        private static readonly byte[] KW_ELEMENT = XmlTok.Ascii("ELEMENT");
        private static readonly byte[] KW_EMPTY = XmlTok.Ascii("EMPTY");
        private static readonly byte[] KW_ENTITIES = XmlTok.Ascii("ENTITIES");
        private static readonly byte[] KW_ENTITY = XmlTok.Ascii("ENTITY");
        private static readonly byte[] KW_FIXED = XmlTok.Ascii("FIXED");
        private static readonly byte[] KW_ID = XmlTok.Ascii("ID");
        private static readonly byte[] KW_IDREF = XmlTok.Ascii("IDREF");
        private static readonly byte[] KW_IDREFS = XmlTok.Ascii("IDREFS");
        private static readonly byte[] KW_IGNORE = XmlTok.Ascii("IGNORE");
        private static readonly byte[] KW_IMPLIED = XmlTok.Ascii("IMPLIED");
        private static readonly byte[] KW_INCLUDE = XmlTok.Ascii("INCLUDE");
        private static readonly byte[] KW_NDATA = XmlTok.Ascii("NDATA");
        private static readonly byte[] KW_NMTOKEN = XmlTok.Ascii("NMTOKEN");
        private static readonly byte[] KW_NMTOKENS = XmlTok.Ascii("NMTOKENS");
        private static readonly byte[] KW_NOTATION = XmlTok.Ascii("NOTATION");
        private static readonly byte[] KW_PCDATA = XmlTok.Ascii("PCDATA");
        private static readonly byte[] KW_PUBLIC = XmlTok.Ascii("PUBLIC");
        private static readonly byte[] KW_REQUIRED = XmlTok.Ascii("REQUIRED");
        private static readonly byte[] KW_SYSTEM = XmlTok.Ascii("SYSTEM");

        // setTopLevel
        private static void setTopLevel(PROLOG_STATE state) =>
            state.handler = state.documentEntity ? internalSubset : externalSubset1;

        /// <summary>prolog0</summary>
        private static int prolog0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            state.handler = prolog1;
            return XML_ROLE_NONE;
          case XML_TOK_XML_DECL:
            state.handler = prolog1;
            return XML_ROLE_XML_DECL;
          case XML_TOK_PI:
            state.handler = prolog1;
            return XML_ROLE_PI;
          case XML_TOK_COMMENT:
            state.handler = prolog1;
            return XML_ROLE_COMMENT;
          case XML_TOK_BOM:
            return XML_ROLE_NONE;
          case XML_TOK_DECL_OPEN:
            if (! enc.NameMatchesAscii(buf, ptr + 2 * enc.minBytesPerChar, end,
                                      KW_DOCTYPE))
              break;
            state.handler = doctype0;
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_INSTANCE_START:
            state.handler = error;
            return XML_ROLE_INSTANCE_START;
          }
          return common(state, tok);
        }

        /// <summary>prolog1</summary>
        private static int prolog1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_PI:
            return XML_ROLE_PI;
          case XML_TOK_COMMENT:
            return XML_ROLE_COMMENT;
          case XML_TOK_BOM:
            /* This case can never arise.  To reach this role function, the
             * parse must have passed through prolog0 and therefore have had
             * some form of input, even if only a space.  At that point, a
             * byte order mark is no longer a valid character (though
             * technically it should be interpreted as a non-breaking space),
             * so will be rejected by the tokenizing stages.
             */
            return XML_ROLE_NONE; 
          case XML_TOK_DECL_OPEN:
            if (! enc.NameMatchesAscii(buf, ptr + 2 * enc.minBytesPerChar, end,
                                      KW_DOCTYPE))
              break;
            state.handler = doctype0;
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_INSTANCE_START:
            state.handler = error;
            return XML_ROLE_INSTANCE_START;
          }
          return common(state, tok);
        }

        /// <summary>prolog2</summary>
        private static int prolog2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_PI:
            return XML_ROLE_PI;
          case XML_TOK_COMMENT:
            return XML_ROLE_COMMENT;
          case XML_TOK_INSTANCE_START:
            state.handler = error;
            return XML_ROLE_INSTANCE_START;
          }
          return common(state, tok);
        }

        /// <summary>doctype0</summary>
        private static int doctype0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = doctype1;
            return XML_ROLE_DOCTYPE_NAME;
          }
          return common(state, tok);
        }

        /// <summary>doctype1</summary>
        private static int doctype1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_OPEN_BRACKET:
            state.handler = internalSubset;
            return XML_ROLE_DOCTYPE_INTERNAL_SUBSET;
          case XML_TOK_DECL_CLOSE:
            state.handler = prolog2;
            return XML_ROLE_DOCTYPE_CLOSE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_SYSTEM)) {
              state.handler = doctype3;
              return XML_ROLE_DOCTYPE_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_PUBLIC)) {
              state.handler = doctype2;
              return XML_ROLE_DOCTYPE_NONE;
            }
            break;
          }
          return common(state, tok);
        }

        /// <summary>doctype2</summary>
        private static int doctype2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_LITERAL:
            state.handler = doctype3;
            return XML_ROLE_DOCTYPE_PUBLIC_ID;
          }
          return common(state, tok);
        }

        /// <summary>doctype3</summary>
        private static int doctype3(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_LITERAL:
            state.handler = doctype4;
            return XML_ROLE_DOCTYPE_SYSTEM_ID;
          }
          return common(state, tok);
        }

        /// <summary>doctype4</summary>
        private static int doctype4(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_OPEN_BRACKET:
            state.handler = internalSubset;
            return XML_ROLE_DOCTYPE_INTERNAL_SUBSET;
          case XML_TOK_DECL_CLOSE:
            state.handler = prolog2;
            return XML_ROLE_DOCTYPE_CLOSE;
          }
          return common(state, tok);
        }

        /// <summary>doctype5</summary>
        private static int doctype5(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_DECL_CLOSE:
            state.handler = prolog2;
            return XML_ROLE_DOCTYPE_CLOSE;
          }
          return common(state, tok);
        }

        /// <summary>internalSubset</summary>
        private static int internalSubset(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_DECL_OPEN:
            if (enc.NameMatchesAscii(buf, ptr + 2 * enc.minBytesPerChar, end,
                                    KW_ENTITY)) {
              state.handler = entity0;
              return XML_ROLE_ENTITY_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr + 2 * enc.minBytesPerChar, end,
                                    KW_ATTLIST)) {
              state.handler = attlist0;
              return XML_ROLE_ATTLIST_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr + 2 * enc.minBytesPerChar, end,
                                    KW_ELEMENT)) {
              state.handler = element0;
              return XML_ROLE_ELEMENT_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr + 2 * enc.minBytesPerChar, end,
                                    KW_NOTATION)) {
              state.handler = notation0;
              return XML_ROLE_NOTATION_NONE;
            }
            break;
          case XML_TOK_PI:
            return XML_ROLE_PI;
          case XML_TOK_COMMENT:
            return XML_ROLE_COMMENT;
          case XML_TOK_PARAM_ENTITY_REF:
            return XML_ROLE_PARAM_ENTITY_REF;
          case XML_TOK_CLOSE_BRACKET:
            state.handler = doctype5;
            return XML_ROLE_DOCTYPE_NONE;
          case XML_TOK_NONE:
            return XML_ROLE_NONE;
          }
          return common(state, tok);
        }


        /// <summary>externalSubset0</summary>
        private static int externalSubset0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          state.handler = externalSubset1;
          if (tok == XML_TOK_XML_DECL)
            return XML_ROLE_TEXT_DECL;
          return externalSubset1(state, tok, buf, ptr, end, enc);
        }

        /// <summary>externalSubset1</summary>
        private static int externalSubset1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_COND_SECT_OPEN:
            state.handler = condSect0;
            return XML_ROLE_NONE;
          case XML_TOK_COND_SECT_CLOSE:
            if (state.includeLevel == 0)
              break;
            state.includeLevel -= 1;
            return XML_ROLE_NONE;
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_CLOSE_BRACKET:
            break;
          case XML_TOK_NONE:
            if (state.includeLevel != 0)
              break;
            return XML_ROLE_NONE;
          default:
            return internalSubset(state, tok, buf, ptr, end, enc);
          }
          return common(state, tok);
        }


        /// <summary>entity0</summary>
        private static int entity0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_PERCENT:
            state.handler = entity1;
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_NAME:
            state.handler = entity2;
            return XML_ROLE_GENERAL_ENTITY_NAME;
          }
          return common(state, tok);
        }

        /// <summary>entity1</summary>
        private static int entity1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_NAME:
            state.handler = entity7;
            return XML_ROLE_PARAM_ENTITY_NAME;
          }
          return common(state, tok);
        }

        /// <summary>entity2</summary>
        private static int entity2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_SYSTEM)) {
              state.handler = entity4;
              return XML_ROLE_ENTITY_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_PUBLIC)) {
              state.handler = entity3;
              return XML_ROLE_ENTITY_NONE;
            }
            break;
          case XML_TOK_LITERAL:
            state.handler = declClose;
            state.role_none = XML_ROLE_ENTITY_NONE;
            return XML_ROLE_ENTITY_VALUE;
          }
          return common(state, tok);
        }

        /// <summary>entity3</summary>
        private static int entity3(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_LITERAL:
            state.handler = entity4;
            return XML_ROLE_ENTITY_PUBLIC_ID;
          }
          return common(state, tok);
        }

        /// <summary>entity4</summary>
        private static int entity4(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_LITERAL:
            state.handler = entity5;
            return XML_ROLE_ENTITY_SYSTEM_ID;
          }
          return common(state, tok);
        }

        /// <summary>entity5</summary>
        private static int entity5(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_DECL_CLOSE:
            setTopLevel(state);
            return XML_ROLE_ENTITY_COMPLETE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_NDATA)) {
              state.handler = entity6;
              return XML_ROLE_ENTITY_NONE;
            }
            break;
          }
          return common(state, tok);
        }

        /// <summary>entity6</summary>
        private static int entity6(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_NAME:
            state.handler = declClose;
            state.role_none = XML_ROLE_ENTITY_NONE;
            return XML_ROLE_ENTITY_NOTATION_NAME;
          }
          return common(state, tok);
        }

        /// <summary>entity7</summary>
        private static int entity7(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_SYSTEM)) {
              state.handler = entity9;
              return XML_ROLE_ENTITY_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_PUBLIC)) {
              state.handler = entity8;
              return XML_ROLE_ENTITY_NONE;
            }
            break;
          case XML_TOK_LITERAL:
            state.handler = declClose;
            state.role_none = XML_ROLE_ENTITY_NONE;
            return XML_ROLE_ENTITY_VALUE;
          }
          return common(state, tok);
        }

        /// <summary>entity8</summary>
        private static int entity8(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_LITERAL:
            state.handler = entity9;
            return XML_ROLE_ENTITY_PUBLIC_ID;
          }
          return common(state, tok);
        }

        /// <summary>entity9</summary>
        private static int entity9(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_LITERAL:
            state.handler = entity10;
            return XML_ROLE_ENTITY_SYSTEM_ID;
          }
          return common(state, tok);
        }

        /// <summary>entity10</summary>
        private static int entity10(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ENTITY_NONE;
          case XML_TOK_DECL_CLOSE:
            setTopLevel(state);
            return XML_ROLE_ENTITY_COMPLETE;
          }
          return common(state, tok);
        }

        /// <summary>notation0</summary>
        private static int notation0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NOTATION_NONE;
          case XML_TOK_NAME:
            state.handler = notation1;
            return XML_ROLE_NOTATION_NAME;
          }
          return common(state, tok);
        }

        /// <summary>notation1</summary>
        private static int notation1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NOTATION_NONE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_SYSTEM)) {
              state.handler = notation3;
              return XML_ROLE_NOTATION_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_PUBLIC)) {
              state.handler = notation2;
              return XML_ROLE_NOTATION_NONE;
            }
            break;
          }
          return common(state, tok);
        }

        /// <summary>notation2</summary>
        private static int notation2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NOTATION_NONE;
          case XML_TOK_LITERAL:
            state.handler = notation4;
            return XML_ROLE_NOTATION_PUBLIC_ID;
          }
          return common(state, tok);
        }

        /// <summary>notation3</summary>
        private static int notation3(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NOTATION_NONE;
          case XML_TOK_LITERAL:
            state.handler = declClose;
            state.role_none = XML_ROLE_NOTATION_NONE;
            return XML_ROLE_NOTATION_SYSTEM_ID;
          }
          return common(state, tok);
        }

        /// <summary>notation4</summary>
        private static int notation4(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NOTATION_NONE;
          case XML_TOK_LITERAL:
            state.handler = declClose;
            state.role_none = XML_ROLE_NOTATION_NONE;
            return XML_ROLE_NOTATION_SYSTEM_ID;
          case XML_TOK_DECL_CLOSE:
            setTopLevel(state);
            return XML_ROLE_NOTATION_NO_SYSTEM_ID;
          }
          return common(state, tok);
        }

        /// <summary>attlist0</summary>
        private static int attlist0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = attlist1;
            return XML_ROLE_ATTLIST_ELEMENT_NAME;
          }
          return common(state, tok);
        }

        /// <summary>attlist1</summary>
        private static int attlist1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_DECL_CLOSE:
            setTopLevel(state);
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = attlist2;
            return XML_ROLE_ATTRIBUTE_NAME;
          }
          return common(state, tok);
        }

        /// <summary>attlist2</summary>
        private static int attlist2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_NAME: {
            byte[][] types = {
                KW_CDATA,  KW_ID,       KW_IDREF,   KW_IDREFS,
                KW_ENTITY, KW_ENTITIES, KW_NMTOKEN, KW_NMTOKENS,
            };
            int i;
            for (i = 0; i < types.Length; i++)
              if (enc.NameMatchesAscii(buf, ptr, end, types[i])) {
                state.handler = attlist8;
                return XML_ROLE_ATTRIBUTE_TYPE_CDATA + i;
              }
          }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_NOTATION)) {
              state.handler = attlist5;
              return XML_ROLE_ATTLIST_NONE;
            }
            break;
          case XML_TOK_OPEN_PAREN:
            state.handler = attlist3;
            return XML_ROLE_ATTLIST_NONE;
          }
          return common(state, tok);
        }

        /// <summary>attlist3</summary>
        private static int attlist3(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_NMTOKEN:
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = attlist4;
            return XML_ROLE_ATTRIBUTE_ENUM_VALUE;
          }
          return common(state, tok);
        }

        /// <summary>attlist4</summary>
        private static int attlist4(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_CLOSE_PAREN:
            state.handler = attlist8;
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_OR:
            state.handler = attlist3;
            return XML_ROLE_ATTLIST_NONE;
          }
          return common(state, tok);
        }

        /// <summary>attlist5</summary>
        private static int attlist5(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_OPEN_PAREN:
            state.handler = attlist6;
            return XML_ROLE_ATTLIST_NONE;
          }
          return common(state, tok);
        }

        /// <summary>attlist6</summary>
        private static int attlist6(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_NAME:
            state.handler = attlist7;
            return XML_ROLE_ATTRIBUTE_NOTATION_VALUE;
          }
          return common(state, tok);
        }

        /// <summary>attlist7</summary>
        private static int attlist7(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_CLOSE_PAREN:
            state.handler = attlist8;
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_OR:
            state.handler = attlist6;
            return XML_ROLE_ATTLIST_NONE;
          }
          return common(state, tok);
        }

        /* default value */
        /// <summary>attlist8</summary>
        private static int attlist8(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_POUND_NAME:
            if (enc.NameMatchesAscii(buf, ptr + enc.minBytesPerChar, end,
                                    KW_IMPLIED)) {
              state.handler = attlist1;
              return XML_ROLE_IMPLIED_ATTRIBUTE_VALUE;
            }
            if (enc.NameMatchesAscii(buf, ptr + enc.minBytesPerChar, end,
                                    KW_REQUIRED)) {
              state.handler = attlist1;
              return XML_ROLE_REQUIRED_ATTRIBUTE_VALUE;
            }
            if (enc.NameMatchesAscii(buf, ptr + enc.minBytesPerChar, end,
                                    KW_FIXED)) {
              state.handler = attlist9;
              return XML_ROLE_ATTLIST_NONE;
            }
            break;
          case XML_TOK_LITERAL:
            state.handler = attlist1;
            return XML_ROLE_DEFAULT_ATTRIBUTE_VALUE;
          }
          return common(state, tok);
        }

        /// <summary>attlist9</summary>
        private static int attlist9(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ATTLIST_NONE;
          case XML_TOK_LITERAL:
            state.handler = attlist1;
            return XML_ROLE_FIXED_ATTRIBUTE_VALUE;
          }
          return common(state, tok);
        }

        /// <summary>element0</summary>
        private static int element0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = element1;
            return XML_ROLE_ELEMENT_NAME;
          }
          return common(state, tok);
        }

        /// <summary>element1</summary>
        private static int element1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_EMPTY)) {
              state.handler = declClose;
              state.role_none = XML_ROLE_ELEMENT_NONE;
              return XML_ROLE_CONTENT_EMPTY;
            }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_ANY)) {
              state.handler = declClose;
              state.role_none = XML_ROLE_ELEMENT_NONE;
              return XML_ROLE_CONTENT_ANY;
            }
            break;
          case XML_TOK_OPEN_PAREN:
            state.handler = element2;
            state.level = 1;
            return XML_ROLE_GROUP_OPEN;
          }
          return common(state, tok);
        }

        /// <summary>element2</summary>
        private static int element2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_POUND_NAME:
            if (enc.NameMatchesAscii(buf, ptr + enc.minBytesPerChar, end,
                                    KW_PCDATA)) {
              state.handler = element3;
              return XML_ROLE_CONTENT_PCDATA;
            }
            break;
          case XML_TOK_OPEN_PAREN:
            state.level = 2;
            state.handler = element6;
            return XML_ROLE_GROUP_OPEN;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT;
          case XML_TOK_NAME_QUESTION:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT_OPT;
          case XML_TOK_NAME_ASTERISK:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT_REP;
          case XML_TOK_NAME_PLUS:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT_PLUS;
          }
          return common(state, tok);
        }

        /// <summary>element3</summary>
        private static int element3(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_CLOSE_PAREN:
            state.handler = declClose;
            state.role_none = XML_ROLE_ELEMENT_NONE;
            return XML_ROLE_GROUP_CLOSE;
          case XML_TOK_CLOSE_PAREN_ASTERISK:
            state.handler = declClose;
            state.role_none = XML_ROLE_ELEMENT_NONE;
            return XML_ROLE_GROUP_CLOSE_REP;
          case XML_TOK_OR:
            state.handler = element4;
            return XML_ROLE_ELEMENT_NONE;
          }
          return common(state, tok);
        }

        /// <summary>element4</summary>
        private static int element4(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = element5;
            return XML_ROLE_CONTENT_ELEMENT;
          }
          return common(state, tok);
        }

        /// <summary>element5</summary>
        private static int element5(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_CLOSE_PAREN_ASTERISK:
            state.handler = declClose;
            state.role_none = XML_ROLE_ELEMENT_NONE;
            return XML_ROLE_GROUP_CLOSE_REP;
          case XML_TOK_OR:
            state.handler = element4;
            return XML_ROLE_ELEMENT_NONE;
          }
          return common(state, tok);
        }

        /// <summary>element6</summary>
        private static int element6(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_OPEN_PAREN:
            state.level += 1;
            return XML_ROLE_GROUP_OPEN;
          case XML_TOK_NAME:
          case XML_TOK_PREFIXED_NAME:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT;
          case XML_TOK_NAME_QUESTION:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT_OPT;
          case XML_TOK_NAME_ASTERISK:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT_REP;
          case XML_TOK_NAME_PLUS:
            state.handler = element7;
            return XML_ROLE_CONTENT_ELEMENT_PLUS;
          }
          return common(state, tok);
        }

        /// <summary>element7</summary>
        private static int element7(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_ELEMENT_NONE;
          case XML_TOK_CLOSE_PAREN:
            state.level -= 1;
            if (state.level == 0) {
              state.handler = declClose;
              state.role_none = XML_ROLE_ELEMENT_NONE;
            }
            return XML_ROLE_GROUP_CLOSE;
          case XML_TOK_CLOSE_PAREN_ASTERISK:
            state.level -= 1;
            if (state.level == 0) {
              state.handler = declClose;
              state.role_none = XML_ROLE_ELEMENT_NONE;
            }
            return XML_ROLE_GROUP_CLOSE_REP;
          case XML_TOK_CLOSE_PAREN_QUESTION:
            state.level -= 1;
            if (state.level == 0) {
              state.handler = declClose;
              state.role_none = XML_ROLE_ELEMENT_NONE;
            }
            return XML_ROLE_GROUP_CLOSE_OPT;
          case XML_TOK_CLOSE_PAREN_PLUS:
            state.level -= 1;
            if (state.level == 0) {
              state.handler = declClose;
              state.role_none = XML_ROLE_ELEMENT_NONE;
            }
            return XML_ROLE_GROUP_CLOSE_PLUS;
          case XML_TOK_COMMA:
            state.handler = element6;
            return XML_ROLE_GROUP_SEQUENCE;
          case XML_TOK_OR:
            state.handler = element6;
            return XML_ROLE_GROUP_CHOICE;
          }
          return common(state, tok);
        }


        /// <summary>condSect0</summary>
        private static int condSect0(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_NAME:
            if (enc.NameMatchesAscii(buf, ptr, end, KW_INCLUDE)) {
              state.handler = condSect1;
              return XML_ROLE_NONE;
            }
            if (enc.NameMatchesAscii(buf, ptr, end, KW_IGNORE)) {
              state.handler = condSect2;
              return XML_ROLE_NONE;
            }
            break;
          }
          return common(state, tok);
        }

        /// <summary>condSect1</summary>
        private static int condSect1(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_OPEN_BRACKET:
            state.handler = externalSubset1;
            state.includeLevel += 1;
            return XML_ROLE_NONE;
          }
          return common(state, tok);
        }

        /// <summary>condSect2</summary>
        private static int condSect2(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return XML_ROLE_NONE;
          case XML_TOK_OPEN_BRACKET:
            state.handler = externalSubset1;
            return XML_ROLE_IGNORE_SECT;
          }
          return common(state, tok);
        }


        /// <summary>declClose</summary>
        private static int declClose(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          switch (tok) {
          case XML_TOK_PROLOG_S:
            return state.role_none;
          case XML_TOK_DECL_CLOSE:
            setTopLevel(state);
            return state.role_none;
          }
          return common(state, tok);
        }


        /// <summary>error</summary>
        private static int error(PROLOG_STATE state, int tok, byte[] buf, int ptr, int end, ENCODING enc) {
          return XML_ROLE_NONE;
        }


        /// <summary>common</summary>
        private static int common(PROLOG_STATE state, int tok) {
          if (!state.documentEntity && tok == XML_TOK_PARAM_ENTITY_REF)
            return XML_ROLE_INNER_PARAM_ENTITY_REF;
          state.handler = error;
          return XML_ROLE_ERROR;
        }

        /// <summary>XmlPrologStateInit</summary>
        internal static void XmlPrologStateInit(PROLOG_STATE state) {
          state.handler = prolog0;
          state.documentEntity = true;
          state.includeLevel = 0;
          state.inEntityValue = false;
        }


        /// <summary>XmlPrologStateInitExternalEntity</summary>
        internal static void XmlPrologStateInitExternalEntity(PROLOG_STATE state) {
          state.handler = externalSubset0;
          state.documentEntity = false;
          state.includeLevel = 0;
        }
    }
}
