using System;
using System.Text;
using SharpPy.Modules.Expat;
using static SharpPy.Modules.Expat.XmlParse;
using XML_Parser = SharpPy.Modules.Expat.XML_ParserStruct;

namespace SharpPy.Modules
{
    /// <summary>
    /// The error indicator C code reads with PyErr_Occurred() while expat calls it back: a handler
    /// that fails stores its exception here (PyErr_SetObject) and the parser stops; the module
    /// function that called XML_Parse raises it afterwards (get_parse_result).
    /// </summary>
    internal static class PyErrIndicator
    {
        [ThreadStatic] private static PythonException _current;

        internal static bool Occurred() => _current != null;

        internal static void Set(PythonException error) => _current = error;

        internal static void Set(Exception error) => _current = error as PythonException
            ?? new PythonException(PyVM.ConvertToPythonException(error));

        /// <summary>PyErr_Fetch: the pending error, cleared.</summary>
        internal static PythonException Fetch()
        {
            var e = _current;
            _current = null;
            return e;
        }

        /// <summary>Raise the pending error, if any.</summary>
        internal static void RaiseIfOccurred()
        {
            var e = Fetch();
            if (e != null)
                throw e;
        }
    }

    /// <summary>struct PyExpat_CAPI (Include/pyexpat.h): the expat entry points _elementtree uses.</summary>
    internal sealed class PyExpat_CAPI
    {
        internal string magic = "pyexpat.expat_CAPI 1.1";
        internal int MAJOR_VERSION = XmlParse.XML_MAJOR_VERSION;
        internal int MINOR_VERSION = XmlParse.XML_MINOR_VERSION;
        internal int MICRO_VERSION = XmlParse.XML_MICRO_VERSION;
        internal Func<XML_Error, string> ErrorString = XML_ErrorString;
        internal Func<XML_Parser, XML_Error> GetErrorCode = XML_GetErrorCode;
        internal Func<XML_Parser, ulong> GetErrorColumnNumber = XML_GetCurrentColumnNumber;
        internal Func<XML_Parser, ulong> GetErrorLineNumber = XML_GetCurrentLineNumber;
        internal Func<XML_Parser, byte[], int, int, bool, XML_Status> Parse = XML_Parse;
        internal Func<XCHARP, XCHARP, XML_Parser> ParserCreate_MM = XML_ParserCreate_MM;
        internal Action<XML_Parser> ParserFree = XML_ParserFree;
        internal Action<XML_Parser, XML_CharacterDataHandler> SetCharacterDataHandler = XML_SetCharacterDataHandler;
        internal Action<XML_Parser, XML_CommentHandler> SetCommentHandler = XML_SetCommentHandler;
        internal Action<XML_Parser, XML_DefaultHandler> SetDefaultHandlerExpand = XML_SetDefaultHandlerExpand;
        internal Action<XML_Parser, XML_StartElementHandler, XML_EndElementHandler> SetElementHandler = XML_SetElementHandler;
        internal Action<XML_Parser, XML_StartNamespaceDeclHandler, XML_EndNamespaceDeclHandler> SetNamespaceDeclHandler = XML_SetNamespaceDeclHandler;
        internal Action<XML_Parser, XML_ProcessingInstructionHandler> SetProcessingInstructionHandler = XML_SetProcessingInstructionHandler;
        internal Action<XML_Parser, XML_UnknownEncodingHandler, object> SetUnknownEncodingHandler = XML_SetUnknownEncodingHandler;
        internal Action<XML_Parser, object> SetUserData = XML_SetUserData;
        internal Action<XML_Parser, XML_StartDoctypeDeclHandler> SetStartDoctypeDeclHandler = XML_SetStartDoctypeDeclHandler;
        internal Func<XML_Parser, XCHARP, XML_Status> SetEncoding = XML_SetEncoding;
        internal XML_UnknownEncodingHandler DefaultUnknownEncodingHandler = PyexpatModule.PyUnknownEncodingHandler;
        internal Func<XML_Parser, ulong, int> SetHashSalt = XML_SetHashSalt;
        internal Func<XML_Parser, bool, bool> SetReparseDeferralEnabled = XML_SetReparseDeferralEnabled;
        internal Func<XML_Parser, ulong, bool> SetAllocTrackerActivationThreshold = XML_SetAllocTrackerActivationThreshold;
        internal Func<XML_Parser, float, bool> SetAllocTrackerMaximumAmplification = XML_SetAllocTrackerMaximumAmplification;
        internal Func<XML_Parser, ulong, bool> SetBillionLaughsAttackProtectionActivationThreshold = XML_SetBillionLaughsAttackProtectionActivationThreshold;
        internal Func<XML_Parser, float, bool> SetBillionLaughsAttackProtectionMaximumAmplification = XML_SetBillionLaughsAttackProtectionMaximumAmplification;
        internal Func<XML_Parser, byte[], bool> SetHashSalt16Bytes = XML_SetHashSalt16Bytes;
    }

    /// <summary>
    /// Modules/pyexpat.c over expat 2.8.5 in C# (modules/Expat): the xmlparser type, ParserCreate,
    /// ErrorString, the errors and model submodules and the expat_CAPI capsule. One C# method per
    /// C function; the handlers are the my_*Handler functions of the RC_HANDLER macro.
    /// </summary>
    internal static class PyexpatModule
    {
        private const string MODULE_NAME = "pyexpat";

        // enum HandlerTypes
        private const int StartElement = 0, EndElement = 1, ProcessingInstruction = 2, CharacterData = 3,
            UnparsedEntityDecl = 4, NotationDecl = 5, StartNamespaceDecl = 6, EndNamespaceDecl = 7, Comment = 8,
            StartCdataSection = 9, EndCdataSection = 10, Default = 11, DefaultHandlerExpand = 12, NotStandalone = 13,
            ExternalEntityRef = 14, StartDoctypeDecl = 15, EndDoctypeDecl = 16, EntityDecl = 17, XmlDecl = 18,
            ElementDecl = 19, AttlistDecl = 20, SkippedEntity = 21, _DummyDecl = 22;

        // pyexpat_state: one per process, like the other ports' module state
        private static PyType xml_parse_type;
        private static PyType error;
        private static readonly PyString str_read = PyString.InternFrom("read");

        private const int CHARACTER_DATA_BUFFER_SIZE = 8192;

        /// <summary>xmlparseobject</summary>
        internal sealed class xmlparseobject : PyObject
        {
            internal XML_Parser itself;
            internal PyObject parent;
            internal bool ordered_attributes; /* Return attributes as a list. */
            internal bool specified_attributes; /* Report only specified attributes. */
            internal bool in_callback; /* Is a callback active? */
            internal bool ns_prefixes; /* Namespace-triplets mode? */
            internal byte[] buffer; /* Buffer used when accumulating characters */
            /* NULL if not enabled */
            internal int buffer_size; /* Size of buffer, in XML_Char units */
            internal int buffer_used; /* Buffer units in use */
            internal bool reparse_deferral_enabled; /* Whether to defer reparsing of unfinished XML tokens; a de-facto cache of XML_GetReparseDeferralEnabled */
            internal PyDict intern; /* Dictionary to intern strings */
            internal PyObject[] handlers;

            public override PyType GetPyType() => xml_parse_type;
        }

        // struct HandlerInfo: the name, XML_Set*Handler and my_*Handler
        private sealed class HandlerInfo
        {
            internal string name;
            internal Action<XML_Parser, Delegate> setter;
            internal Delegate handler;
        }

        private static readonly HandlerInfo[] handler_info = MakeHandlerInfo();

        internal static PyModule Create()
        {
            var mod = new PyModule(MODULE_NAME);
            pyexpat_exec(mod);
            return mod;
        }

        // ------------------------------------------------------------------ errors

        /// <summary>set_xml_error_attr_code / set_xml_error_attr_location / set_xml_error</summary>
        private static Exception set_xml_error(XML_Error code, ulong lineno, ulong column, string errmsg)
        {
            PyObject arg;
            if (errmsg == null)
                arg = PyString.From($"{XML_ErrorString(code)}: line {lineno}, column {column}");
            else
                arg = PyString.From(errmsg);
            PyObject res = error.Call(new[] { arg }, null);
            res.SetAttribute("code", PyInt.From((long)code));
            res.SetAttribute("lineno", PyInt.From(lineno));
            res.SetAttribute("offset", PyInt.From(column));
            return new PythonException((PyBaseException)res);
        }

        // SET_XML_ERROR
        private static Exception SET_XML_ERROR(xmlparseobject self, XML_Error code, string errmsg)
        {
            XML_Parser parser = self.itself;
            ulong lineno = XML_GetCurrentLineNumber(parser);
            ulong column = XML_GetCurrentColumnNumber(parser);
            return set_xml_error(code, lineno, column, errmsg);
        }

        /// <summary>set_error</summary>
        private static Exception set_error(xmlparseobject self, XML_Error code) => SET_XML_ERROR(self, code, null);

        /// <summary>set_invalid_arg</summary>
        private static Exception set_invalid_arg(xmlparseobject self, string errmsg) =>
            SET_XML_ERROR(self, XML_Error.XML_ERROR_INVALID_ARGUMENT, errmsg);

        /// <summary>have_handler</summary>
        private static bool have_handler(xmlparseobject self, int type) => self.handlers[type] != null;

        // ------------------------------------------------------------------ strings

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>PyUnicode_DecodeUTF8(str, len, "strict")</summary>
        internal static PyString DecodeUTF8(byte[] a, int off, int len)
        {
            bool ascii = true;
            for (int k = 0; k < len; k++)
                if (a[off + k] >= 0x80)
                {
                    ascii = false;
                    break;
                }
            if (ascii)
                return PyString.From(Encoding.ASCII.GetString(a, off, len));
            try
            {
                return PyString.From(StrictUtf8.GetString(a, off, len));
            }
            catch (DecoderFallbackException)
            {
                var copy = new byte[len];
                Buffer.BlockCopy(a, off, copy, 0, len);
                return UnicodeDecoders.DecodeUtf8(copy, "strict", null);
            }
        }

        /// <summary>conv_string_to_unicode</summary>
        internal static PyObject conv_string_to_unicode(XCHARP str)
        {
            /* XXX currently this code assumes that XML_Char is 8-bit,
               and hence in UTF-8.  */
            /* UTF-8 from Expat, Unicode desired */
            if (str.IsNull)
                return PyNone.Instance;
            return DecodeUTF8(str.a, str.i, str.Length);
        }

        /// <summary>conv_string_len_to_unicode</summary>
        internal static PyObject conv_string_len_to_unicode(XCHARP str, int len)
        {
            /* XXX currently this code assumes that XML_Char is 8-bit,
               and hence in UTF-8.  */
            /* UTF-8 from Expat, Unicode desired */
            if (str.IsNull)
                return PyNone.Instance;
            return DecodeUTF8(str.a, str.i, len);
        }

        /// <summary>
        /// error_external_entity_ref_handler: callback for ExternalEntityRefHandler when there is
        /// already an error: does nothing (returns 0 so expat reports an error).
        /// </summary>
        private static int error_external_entity_ref_handler(XML_Parser parser, XCHARP context, XCHARP @base,
                                                             XCHARP systemId, XCHARP publicId) => 0;

        /// <summary>noop_character_data_handler: dummy character data handler used when an error occurs.</summary>
        private static void noop_character_data_handler(object userData, XCHARP data, int len)
        {
        }

        /// <summary>flag_error</summary>
        private static void flag_error(xmlparseobject self)
        {
            clear_handlers(self, false);
            XML_SetExternalEntityRefHandler(self.itself, error_external_entity_ref_handler);
        }

        /// <summary>
        /// call_with_frame: the call; a failure is stored as the pending error (the PyErr state),
        /// the parser is stopped and null is answered. CPython also adds a frame named after the
        /// handler to the traceback (_PyTraceback_Add), which is not done here.
        /// </summary>
        private static PyObject call_with_frame(string funcname, PyObject func, PyObject[] args, xmlparseobject self)
        {
            PyObject res;
            try
            {
                res = func.Call(args, null);
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
                XML_StopParser(self.itself, false);
                return null;
            }
            return res;
        }

        /// <summary>string_intern</summary>
        private static PyObject string_intern(xmlparseobject self, XCHARP str)
        {
            PyObject result = conv_string_to_unicode(str);
            if (result is PyNone)
                return result;
            if (self.intern == null)
                return result;
            if (!self.intern.InternalDict.TryGetValue(result, out PyObject value))
            {
                self.intern.SetItem(result, result);
                return result;
            }
            return value;
        }

        /// <summary>
        /// call_character_handler: return 0 on success, -1 on exception. flag_error() will be
        /// called before return as needed.
        /// </summary>
        private static int call_character_handler(xmlparseobject self, byte[] buffer, int off, int len)
        {
            PyObject temp;

            if (!have_handler(self, CharacterData))
                return -1;

            try
            {
                temp = conv_string_len_to_unicode(new XCHARP(buffer, off), len);
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
                flag_error(self);
                XML_SetCharacterDataHandler(self.itself, noop_character_data_handler);
                return -1;
            }
            self.in_callback = true;
            temp = call_with_frame("CharacterData", self.handlers[CharacterData], new[] { temp }, self);
            self.in_callback = false;
            if (temp == null)
            {
                flag_error(self);
                XML_SetCharacterDataHandler(self.itself, noop_character_data_handler);
                return -1;
            }
            return 0;
        }

        /// <summary>flush_character_buffer</summary>
        private static int flush_character_buffer(xmlparseobject self)
        {
            int rc;
            if (self.buffer == null || self.buffer_used == 0)
                return 0;
            rc = call_character_handler(self, self.buffer, 0, self.buffer_used);
            self.buffer_used = 0;
            return rc;
        }

        /// <summary>my_CharacterDataHandler</summary>
        private static void my_CharacterDataHandler(object userData, XCHARP data, int len)
        {
            xmlparseobject self = (xmlparseobject)userData;

            if (PyErrIndicator.Occurred())
                return;

            if (self.buffer == null)
                call_character_handler(self, data.a, data.i, len);
            else
            {
                if (self.buffer_used + len > self.buffer_size)
                {
                    if (flush_character_buffer(self) < 0)
                        return;
                    /* handler might have changed; drop the rest on the floor
                     * if there isn't a handler anymore
                     */
                    if (!have_handler(self, CharacterData))
                        return;
                }
                if (len > self.buffer_size)
                {
                    call_character_handler(self, data.a, data.i, len);
                    self.buffer_used = 0;
                }
                else
                {
                    Buffer.BlockCopy(data.a, data.i, self.buffer, self.buffer_used, len);
                    self.buffer_used += len;
                }
            }
        }

        /// <summary>my_StartElementHandler</summary>
        private static void my_StartElementHandler(object userData, XCHARP name, XCHARP[] atts)
        {
            xmlparseobject self = (xmlparseobject)userData;

            if (have_handler(self, StartElement))
            {
                PyObject container, rv;
                int i, max;

                if (PyErrIndicator.Occurred())
                    return;

                if (flush_character_buffer(self) < 0)
                    return;
                /* Set max to the number of slots filled in atts[]; max/2 is
                 * the number of attributes we need to process.
                 */
                if (self.specified_attributes)
                    max = XML_GetSpecifiedAttributeCount(self.itself);
                else
                {
                    max = 0;
                    while (!atts[max].IsNull)
                        max += 2;
                }
                /* Build the container. */
                PyObject[] list = null;
                PyDict dict = null;
                try
                {
                    if (self.ordered_attributes)
                        list = new PyObject[max];
                    else
                        dict = new PyDict();
                    for (i = 0; i < max; i += 2)
                    {
                        PyObject n = string_intern(self, atts[i]);
                        PyObject v = conv_string_to_unicode(atts[i + 1]);
                        if (self.ordered_attributes)
                        {
                            list[i] = n;
                            list[i + 1] = v;
                        }
                        else
                            dict.SetItem(n, v);
                    }
                    container = list != null ? new PyList(list) : dict;
                }
                catch (Exception e)
                {
                    PyErrIndicator.Set(e);
                    flag_error(self);
                    return;
                }
                PyObject nameobj;
                try
                {
                    nameobj = string_intern(self, name);
                }
                catch (Exception e)
                {
                    PyErrIndicator.Set(e);
                    return;
                }
                self.in_callback = true;
                rv = call_with_frame("StartElement", self.handlers[StartElement], new[] { nameobj, container }, self);
                self.in_callback = false;
                if (rv == null)
                {
                    flag_error(self);
                    return;
                }
            }
        }

        /// <summary>
        /// RC_HANDLER: if there is a handler and no pending error, flush the character buffer,
        /// build the arguments, call; a failure flags the error. Returns the call's result or null.
        /// </summary>
        private static PyObject RC_HANDLER(xmlparseobject self, int NAME, string name, Func<PyObject[]> PARAM_FORMAT)
        {
            if (have_handler(self, NAME))
            {
                if (PyErrIndicator.Occurred())
                    return null;
                if (flush_character_buffer(self) < 0)
                    return null;
                PyObject[] args;
                try
                {
                    args = PARAM_FORMAT();
                }
                catch (Exception e)
                {
                    PyErrIndicator.Set(e);
                    flag_error(self);
                    return null;
                }
                self.in_callback = true;
                PyObject rv = call_with_frame(name, self.handlers[NAME], args, self);
                self.in_callback = false;
                if (rv == null)
                {
                    flag_error(self);
                    return null;
                }
                return rv;
            }
            return null;
        }

        // INT_HANDLER's CONVERSION: rc = PyLong_AsLong(rv)
        private static int AsLong(xmlparseobject self, PyObject rv)
        {
            if (rv == null)
                return 0;
            try
            {
                return (int)PyArgs.CLong(rv);
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
                return -1;
            }
        }

        private static void my_EndElementHandler(object userData, XCHARP name)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, EndElement, "EndElement", () => new[] { string_intern(self, name) });
        }

        private static void my_ProcessingInstructionHandler(object userData, XCHARP target, XCHARP data)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, ProcessingInstruction, "ProcessingInstruction",
                       () => new[] { string_intern(self, target), conv_string_to_unicode(data) });
        }

        private static void my_UnparsedEntityDeclHandler(object userData, XCHARP entityName, XCHARP @base, XCHARP systemId,
                                                         XCHARP publicId, XCHARP notationName)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, UnparsedEntityDecl, "UnparsedEntityDecl", () => new[]
            {
                string_intern(self, entityName), string_intern(self, @base), string_intern(self, systemId),
                string_intern(self, publicId), string_intern(self, notationName)
            });
        }

        private static void my_EntityDeclHandler(object userData, XCHARP entityName, int is_parameter_entity, XCHARP value,
                                                 int value_length, XCHARP @base, XCHARP systemId, XCHARP publicId,
                                                 XCHARP notationName)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, EntityDecl, "EntityDecl", () => new[]
            {
                string_intern(self, entityName), PyInt.From(is_parameter_entity),
                conv_string_len_to_unicode(value, value_length), string_intern(self, @base),
                string_intern(self, systemId), string_intern(self, publicId), string_intern(self, notationName)
            });
        }

        private static void my_XmlDeclHandler(object userData, XCHARP version, XCHARP encoding, int standalone)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, XmlDecl, "XmlDecl", () => new[]
            {
                conv_string_to_unicode(version), conv_string_to_unicode(encoding), PyInt.From(standalone)
            });
        }

        /// <summary>conv_content_model</summary>
        private static PyObject conv_content_model(XML_Content model, Func<XCHARP, PyObject> conv_string)
        {
            PyCRecursion.Enter(" in conv_content_model");
            try
            {
                var children = new PyObject[model.numchildren];
                for (int i = 0; i < (int)model.numchildren; ++i)
                    children[i] = conv_content_model(model.children[i], conv_string);
                return PyTuple.From(new PyObject[]
                {
                    PyInt.From((int)model.type), PyInt.From((int)model.quant), conv_string(model.name), PyTuple.From(children)
                });
            }
            finally
            {
                PyCRecursion.Leave();
            }
        }

        /// <summary>my_ElementDeclHandler</summary>
        private static void my_ElementDeclHandler(object userData, XCHARP name, XML_Content model)
        {
            xmlparseobject self = (xmlparseobject)userData;

            if (have_handler(self, ElementDecl))
            {
                PyObject rv;
                PyObject modelobj, nameobj;

                if (PyErrIndicator.Occurred())
                    return;

                if (flush_character_buffer(self) < 0)
                    return;
                try
                {
                    modelobj = conv_content_model(model, conv_string_to_unicode);
                    nameobj = string_intern(self, name);
                }
                catch (Exception e)
                {
                    PyErrIndicator.Set(e);
                    flag_error(self);
                    return;
                }
                self.in_callback = true;
                rv = call_with_frame("ElementDecl", self.handlers[ElementDecl], new[] { nameobj, modelobj }, self);
                self.in_callback = false;
                if (rv == null)
                    flag_error(self);
            }
        }

        private static void my_AttlistDeclHandler(object userData, XCHARP elname, XCHARP attname, XCHARP att_type, XCHARP dflt,
                                                  int isrequired)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, AttlistDecl, "AttlistDecl", () => new[]
            {
                string_intern(self, elname), string_intern(self, attname), conv_string_to_unicode(att_type),
                conv_string_to_unicode(dflt), PyInt.From(isrequired)
            });
        }

        private static void my_SkippedEntityHandler(object userData, XCHARP entityName, int is_parameter_entity)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, SkippedEntity, "SkippedEntity", () => new[]
            {
                string_intern(self, entityName), PyInt.From(is_parameter_entity)
            });
        }

        private static void my_NotationDeclHandler(object userData, XCHARP notationName, XCHARP @base, XCHARP systemId,
                                                   XCHARP publicId)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, NotationDecl, "NotationDecl", () => new[]
            {
                string_intern(self, notationName), string_intern(self, @base), string_intern(self, systemId),
                string_intern(self, publicId)
            });
        }

        private static void my_StartNamespaceDeclHandler(object userData, XCHARP prefix, XCHARP uri)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, StartNamespaceDecl, "StartNamespaceDecl", () => new[]
            {
                string_intern(self, prefix), string_intern(self, uri)
            });
        }

        private static void my_EndNamespaceDeclHandler(object userData, XCHARP prefix)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, EndNamespaceDecl, "EndNamespaceDecl", () => new[] { string_intern(self, prefix) });
        }

        private static void my_CommentHandler(object userData, XCHARP data)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, Comment, "Comment", () => new[] { conv_string_to_unicode(data) });
        }

        private static void my_StartCdataSectionHandler(object userData)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, StartCdataSection, "StartCdataSection", () => Array.Empty<PyObject>());
        }

        private static void my_EndCdataSectionHandler(object userData)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, EndCdataSection, "EndCdataSection", () => Array.Empty<PyObject>());
        }

        private static void my_DefaultHandler(object userData, XCHARP s, int len)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, Default, "Default", () => new[] { conv_string_len_to_unicode(s, len) });
        }

        private static void my_DefaultHandlerExpandHandler(object userData, XCHARP s, int len)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, DefaultHandlerExpand, "DefaultHandlerExpand", () => new[] { conv_string_len_to_unicode(s, len) });
        }

        private static int my_NotStandaloneHandler(object userData)
        {
            var self = (xmlparseobject)userData;
            return AsLong(self, RC_HANDLER(self, NotStandalone, "NotStandalone", () => Array.Empty<PyObject>()));
        }

        private static int my_ExternalEntityRefHandler(XML_Parser parser, XCHARP context, XCHARP @base, XCHARP systemId,
                                                       XCHARP publicId)
        {
            var self = (xmlparseobject)XML_GetUserData(parser);
            return AsLong(self, RC_HANDLER(self, ExternalEntityRef, "ExternalEntityRef", () => new[]
            {
                conv_string_to_unicode(context), string_intern(self, @base), string_intern(self, systemId),
                string_intern(self, publicId)
            }));
        }

        private static void my_StartDoctypeDeclHandler(object userData, XCHARP doctypeName, XCHARP sysid, XCHARP pubid,
                                                       int has_internal_subset)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, StartDoctypeDecl, "StartDoctypeDecl", () => new[]
            {
                string_intern(self, doctypeName), string_intern(self, sysid), string_intern(self, pubid),
                PyInt.From(has_internal_subset)
            });
        }

        private static void my_EndDoctypeDeclHandler(object userData)
        {
            var self = (xmlparseobject)userData;
            RC_HANDLER(self, EndDoctypeDecl, "EndDoctypeDecl", () => Array.Empty<PyObject>());
        }

        // ------------------------------------------------------------------ methods

        /// <summary>get_parse_result</summary>
        private static PyObject get_parse_result(xmlparseobject self, int rv)
        {
            PyErrIndicator.RaiseIfOccurred();
            if (rv == 0)
                throw set_error(self, XML_GetErrorCode(self.itself));
            if (flush_character_buffer(self) < 0)
            {
                PyErrIndicator.RaiseIfOccurred();
                return null;
            }
            return PyInt.From(rv);
        }

        private const int MAX_CHUNK_SIZE = 1 << 20;

        /// <summary>pyexpat_xmlparser_SetReparseDeferralEnabled_impl</summary>
        private static PyObject SetReparseDeferralEnabled(xmlparseobject self, bool enabled)
        {
            XML_SetReparseDeferralEnabled(self.itself, enabled);
            self.reparse_deferral_enabled = enabled;
            return PyNone.Instance;
        }

        /// <summary>pyexpat_xmlparser_GetReparseDeferralEnabled_impl</summary>
        private static PyObject GetReparseDeferralEnabled(xmlparseobject self) => PyBool.FromBool(self.reparse_deferral_enabled);

        /// <summary>pyexpat_xmlparser_Parse_impl</summary>
        private static PyObject Parse(xmlparseobject self, PyObject data, bool isfinal)
        {
            byte[] s;
            int off = 0;
            long slen;
            int rc;

            if (data is PyString text)
            {
                s = UnicodeEncoders.EncodeUtf8(text, null);
                slen = s.Length;
                XML_SetEncoding(self.itself, XCHARP.Literal("utf-8"));
            }
            else
            {
                s = BytesMethods.RequireBuffer(data);
                slen = s.Length;
            }

            while (slen > MAX_CHUNK_SIZE)
            {
                rc = (int)XML_Parse(self.itself, s, off, MAX_CHUNK_SIZE, false);
                if (rc == 0)
                    return get_parse_result(self, rc);
                off += MAX_CHUNK_SIZE;
                slen -= MAX_CHUNK_SIZE;
            }

            rc = (int)XML_Parse(self.itself, s, off, (int)slen, isfinal);
            return get_parse_result(self, rc);
        }

        private const int BUF_SIZE = 2048;

        /// <summary>readinst: the bytes read() returned, copied into the parser's buffer.</summary>
        private static int readinst(byte[] buf, int bufOff, int buf_size, PyObject meth)
        {
            PyObject str = meth.Call(new PyObject[] { PyInt.From(buf_size) }, null);
            byte[] ptr;
            if (str is PyBytes bytes)
                ptr = bytes.Value;
            else if (str is PyByteArray bytearray)
                ptr = bytearray.ToByteArrayCopy();
            else
                throw PyTypeError.Create($"read() did not return a bytes object (type={str.GetPyType().FullName})");
            int len = ptr.Length;
            if (len > buf_size)
                throw PyValueError.Create($"read() returned too much data: {buf_size} bytes requested, {len} returned");
            Buffer.BlockCopy(ptr, 0, buf, bufOff, len);
            return len;
        }

        /// <summary>pyexpat_xmlparser_ParseFile_impl</summary>
        private static PyObject ParseFile(xmlparseobject self, PyObject file)
        {
            int rv = 1;
            PyObject readmethod = file.LookupAttribute(str_read);
            if (readmethod == null)
                throw PyTypeError.Create("argument must have 'read' attribute");
            for (; ; )
            {
                int bytes_read;
                int buf = XML_GetBuffer(self.itself, BUF_SIZE);
                if (buf < 0)
                    return get_parse_result(self, 0);

                bytes_read = readinst(self.itself.m_buffer, buf, BUF_SIZE, readmethod);

                rv = (int)XML_ParseBuffer(self.itself, bytes_read, bytes_read == 0);
                PyErrIndicator.RaiseIfOccurred();

                if (rv == 0 || bytes_read == 0)
                    break;
            }
            return get_parse_result(self, rv);
        }

        /// <summary>pyexpat_xmlparser_SetBase_impl</summary>
        private static PyObject SetBase(xmlparseobject self, string @base)
        {
            if (XML_SetBase(self.itself, XCHARP.FromString(@base)) == XML_Status.XML_STATUS_ERROR)
                throw PyMemoryError.Create();
            return PyNone.Instance;
        }

        /// <summary>pyexpat_xmlparser_GetBase_impl</summary>
        private static PyObject GetBase(xmlparseobject self)
        {
            XCHARP b = XML_GetBase(self.itself);
            return b.IsNull ? PyNone.Instance : conv_string_to_unicode(b);
        }

        /// <summary>pyexpat_xmlparser_GetInputContext_impl</summary>
        private static PyObject GetInputContext(xmlparseobject self)
        {
            if (self.in_callback)
            {
                byte[] buffer = XML_GetInputContext(self.itself, out int offset, out int size);
                if (buffer != null)
                {
                    var r = new byte[size - offset];
                    Buffer.BlockCopy(buffer, offset, r, 0, size - offset);
                    return PyBytes.From(r);
                }
                else
                    return PyNone.Instance;
            }
            else
                return PyNone.Instance;
        }

        /// <summary>pyexpat_xmlparser_ExternalEntityParserCreate_impl</summary>
        private static PyObject ExternalEntityParserCreate(xmlparseobject self, string context, string encoding)
        {
            xmlparseobject new_parser = new xmlparseobject();
            // The new subparser will make use of the parent XML_Parser inside of Expat.
            // So we need to take subparsers into account with the reference counting
            // of their parent parser.
            new_parser.buffer_size = self.buffer_size;
            new_parser.buffer_used = 0;
            new_parser.buffer = null;
            new_parser.ordered_attributes = self.ordered_attributes;
            new_parser.specified_attributes = self.specified_attributes;
            new_parser.in_callback = false;
            new_parser.ns_prefixes = self.ns_prefixes;
            new_parser.itself = XML_ExternalEntityParserCreate(self.itself, XCHARP.FromString(context), XCHARP.FromString(encoding));
            new_parser.parent = self;
            new_parser.handlers = null;
            new_parser.intern = self.intern;
            new_parser.reparse_deferral_enabled = self.reparse_deferral_enabled;

            if (self.buffer != null)
                new_parser.buffer = new byte[new_parser.buffer_size];
            if (new_parser.itself == null)
                throw PyMemoryError.Create();

            XML_SetUserData(new_parser.itself, new_parser);

            new_parser.handlers = new PyObject[handler_info.Length];
            clear_handlers(new_parser, true);

            /* then copy handlers from self */
            for (int i = 0; i < handler_info.Length; i++)
            {
                PyObject handler = self.handlers[i];
                if (handler != null)
                {
                    new_parser.handlers[i] = handler;
                    handler_info[i].setter(new_parser.itself, handler_info[i].handler);
                }
            }

            return new_parser;
        }

        /// <summary>pyexpat_xmlparser_SetParamEntityParsing_impl</summary>
        private static PyObject SetParamEntityParsing(xmlparseobject self, int flag)
        {
            flag = XML_SetParamEntityParsing(self.itself, (XML_ParamEntityParsing)flag);
            return PyInt.From(flag);
        }

        /// <summary>pyexpat_xmlparser_UseForeignDTD_impl</summary>
        private static PyObject UseForeignDTD(xmlparseobject self, bool flag)
        {
            XML_Error rc = XML_UseForeignDTD(self.itself, flag);
            if (rc != XML_Error.XML_ERROR_NONE)
                throw set_error(self, rc);
            return PyNone.Instance;
        }

        /// <summary>set_activation_threshold</summary>
        private static PyObject set_activation_threshold(xmlparseobject self, ulong threshold, Func<XML_Parser, ulong, bool> setter)
        {
            if (setter(self.itself, threshold))
                return PyNone.Instance;
            // The setter fails if self->itself is NULL (which is not possible here)
            // or is a non-root parser, which currently only happens for parsers
            // created by ExternalEntityParserCreate().
            throw set_invalid_arg(self, "parser must be a root parser");
        }

        /// <summary>set_maximum_amplification</summary>
        private static PyObject set_maximum_amplification(xmlparseobject self, float max_factor, Func<XML_Parser, float, bool> setter)
        {
            if (setter(self.itself, max_factor))
                return PyNone.Instance;
            // Note: Expat has no API to determine whether a parser is a root parser,
            // and since the Expat functions for defining the various maximum allowed
            // amplifcation factors fail when a bad parser or an out-of-range factor
            // is given without specifying which check failed, we check whether the
            // factor is out-of-range to improve the error message. See also gh-90949.
            string message = float.IsNaN(max_factor) || max_factor < 1.0f
                ? "'max_factor' must be at least 1.0"
                : "parser must be a root parser";
            throw set_invalid_arg(self, message);
        }

        // ------------------------------------------------------------------ unknown encodings

        /// <summary>
        /// PyUnknownEncodingHandler: decode the 256 single bytes with the named codec; only
        /// single-byte encodings are supported.
        /// </summary>
        internal static int PyUnknownEncodingHandler(object encodingHandlerData, XCHARP name, XML_Encoding info)
        {
            if (PyErrIndicator.Occurred())
                return (int)XML_Status.XML_STATUS_ERROR;

            PyString u;
            try
            {
                var template_buffer = new byte[256];
                for (int k = 0; k < 256; k++)
                    template_buffer[k] = (byte)k;
                string encName = Encoding.UTF8.GetString(name.ToArray());
                u = PyStringNewArguments.DecodeBytes(template_buffer, encName, "replace");
            }
            catch (Exception e)
            {
                PyErrIndicator.Set(e);
                return (int)XML_Status.XML_STATUS_ERROR;
            }

            int[] cps = u.ToCodePoints();
            if (cps.Length != 256)
            {
                PyErrIndicator.Set(PyValueError.Create("multi-byte encodings are not supported"));
                return (int)XML_Status.XML_STATUS_ERROR;
            }

            for (int i = 0; i < 256; i++)
            {
                int ch = cps[i];
                if (ch != 0xFFFD /* Py_UNICODE_REPLACEMENT_CHARACTER */)
                    info.map[i] = ch;
                else
                    info.map[i] = -1;
            }

            info.data = null;
            info.convert = null;
            info.release = null;
            return (int)XML_Status.XML_STATUS_OK;
        }

        /// <summary>newxmlparseobject</summary>
        private static PyObject newxmlparseobject(string encoding, string namespace_separator, PyDict intern)
        {
            xmlparseobject self = new xmlparseobject();
            self.buffer = null;
            self.buffer_size = CHARACTER_DATA_BUFFER_SIZE;
            self.buffer_used = 0;
            self.ordered_attributes = false;
            self.specified_attributes = false;
            self.in_callback = false;
            self.ns_prefixes = false;
            self.handlers = null;
            self.intern = intern;
            self.reparse_deferral_enabled = true;

            /* namespace_separator is either NULL or contains one char + \0 */
            self.itself = XML_ParserCreate_MM(XCHARP.FromString(encoding), XCHARP.FromString(namespace_separator));
            self.parent = null;
            if (self.itself == null)
                throw PyRuntimeError.Create("XML_ParserCreate failed");
            XML_SetHashSalt16Bytes(self.itself, HashSalt16);
            XML_SetUserData(self.itself, self);
            XML_SetUnknownEncodingHandler(self.itself, PyUnknownEncodingHandler, null);

            self.handlers = new PyObject[handler_info.Length];
            clear_handlers(self, true);
            return self;
        }

        // _Py_HashSecret.expat.hashsalt16: random per process
        internal static readonly byte[] HashSalt16 = MakeSalt();

        private static byte[] MakeSalt()
        {
            var b = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(b);
            return b;
        }

        /// <summary>xmlparse_handler_getter</summary>
        private static PyObject xmlparse_handler_getter(xmlparseobject self, int handlernum)
        {
            PyObject result = self.handlers[handlernum];
            if (result == null)
                result = PyNone.Instance;
            return result;
        }

        /// <summary>xmlparse_handler_setter</summary>
        private static void xmlparse_handler_setter(xmlparseobject self, PyObject v, int handlernum)
        {
            if (v == null)
                throw PyRuntimeError.Create("Cannot delete attribute");
            if (handlernum == CharacterData)
            {
                /* If we're changing the character data handler, flush all
                 * cached data with the old handler.  Not sure there's a
                 * "right" thing to do, though, but this probably won't
                 * happen.
                 */
                if (flush_character_buffer(self) < 0)
                {
                    PyErrIndicator.RaiseIfOccurred();
                    return;
                }
            }

            Delegate c_handler = null;
            if (v is PyNone)
            {
                /* If this is the character data handler, and a character
                   data handler is already active, we need to be more
                   careful.  What we can safely do is replace the existing
                   character data handler callback function with a no-op
                   function that will refuse to call Python.  The downside
                   is that this doesn't completely remove the character
                   data handler from the C layer if there's any callback
                   active, so Expat does a little more work than it
                   otherwise would, but that's really an odd case.  A more
                   elaborate system of handlers and state could remove the
                   C handler more effectively. */
                if (handlernum == CharacterData && self.in_callback)
                    c_handler = (XML_CharacterDataHandler)noop_character_data_handler;
                v = null;
            }
            else
                c_handler = handler_info[handlernum].handler;
            self.handlers[handlernum] = v;
            handler_info[handlernum].setter(self.itself, c_handler);
        }

        /// <summary>xmlparse_buffer_text_setter</summary>
        private static void xmlparse_buffer_text_setter(xmlparseobject self, PyObject v)
        {
            if (v == null)
                throw PyRuntimeError.Create("Cannot delete attribute");
            bool b = v.IsTrue();
            if (b)
            {
                if (self.buffer == null)
                {
                    self.buffer = new byte[self.buffer_size];
                    self.buffer_used = 0;
                }
            }
            else if (self.buffer != null)
            {
                if (flush_character_buffer(self) < 0)
                {
                    PyErrIndicator.RaiseIfOccurred();
                    return;
                }
                self.buffer = null;
            }
        }

        /// <summary>xmlparse_buffer_size_setter</summary>
        private static void xmlparse_buffer_size_setter(xmlparseobject self, PyObject v)
        {
            if (v == null)
                throw PyRuntimeError.Create("Cannot delete attribute");
            long new_buffer_size;
            if (v is not PyInt)
                throw PyTypeError.Create("buffer_size must be an integer");
            new_buffer_size = PyArgs.CLong(v);
            if (new_buffer_size <= 0)
                throw PyValueError.Create("buffer_size must be greater than zero");

            /* trivial case -- no change */
            if (new_buffer_size == self.buffer_size)
                return;

            /* check maximum */
            if (new_buffer_size > int.MaxValue)
                throw PyValueError.Create($"buffer_size must not be greater than {int.MaxValue}");

            if (self.buffer != null)
            {
                /* there is already a buffer */
                if (self.buffer_used != 0)
                {
                    if (flush_character_buffer(self) < 0)
                    {
                        PyErrIndicator.RaiseIfOccurred();
                        return;
                    }
                }
                /* free existing buffer */
            }
            try
            {
                self.buffer = new byte[new_buffer_size];
            }
            catch (OutOfMemoryException)
            {
                throw PyMemoryError.Create();
            }
            self.buffer_size = (int)new_buffer_size;
        }

        /// <summary>xmlparse_namespace_prefixes_setter</summary>
        private static void xmlparse_namespace_prefixes_setter(xmlparseobject self, PyObject v)
        {
            if (v == null)
                throw PyRuntimeError.Create("Cannot delete attribute");
            bool b = v.IsTrue();
            self.ns_prefixes = b;
            XML_SetReturnNSTriplet(self.itself, self.ns_prefixes ? 1 : 0);
        }

        /// <summary>xmlparse_ordered_attributes_setter</summary>
        private static void xmlparse_ordered_attributes_setter(xmlparseobject self, PyObject v)
        {
            if (v == null)
                throw PyRuntimeError.Create("Cannot delete attribute");
            self.ordered_attributes = v.IsTrue();
        }

        /// <summary>xmlparse_specified_attributes_setter</summary>
        private static void xmlparse_specified_attributes_setter(xmlparseobject self, PyObject v)
        {
            if (v == null)
                throw PyRuntimeError.Create("Cannot delete attribute");
            self.specified_attributes = v.IsTrue();
        }

        // ------------------------------------------------------------------ the type

        private static xmlparseobject Self(PyObject o) => (xmlparseobject)o;

        private static PyType MakeXmlParseType()
        {
            var type = new PyType("xmlparser", new[] { PyType.ObjectType }, MODULE_NAME);
            type.MarkSpecHeapType();
            type.MarkImmutableType();
            type.TypeDict["__doc__"] = PyString.From("XML parser");

            // xmlparse_methods
            PyArgs.Define(type, "Parse", PyCall.Defining, (o, args) =>
                Parse(Self(o), args[0], args.Length >= 2 && args[1].IsTrue()), 1, 2);
            PyArgs.Define(type, "ParseFile", PyCall.Defining, (o, args) => ParseFile(Self(o), args[0]), 1, 1);
            PyArgs.Define(type, "SetBase", PyCall.One, (o, args) =>
                SetBase(Self(o), PyArgs.ClinicStr("SetBase", "argument", "str", args[0])));
            PyArgs.Define(type, "GetBase", PyCall.NoArgs, (o, args) => GetBase(Self(o)));
            PyArgs.Define(type, "GetInputContext", PyCall.NoArgs, (o, args) => GetInputContext(Self(o)));
            PyArgs.Define(type, "ExternalEntityParserCreate", PyCall.Defining, (o, args) =>
            {
                string context;
                if (args[0] is PyNone)
                    context = null;
                else if (args[0] is PyString)
                    context = PyArgs.ClinicStr("ExternalEntityParserCreate", "argument 1", "str or None", args[0]);
                else
                    throw PyArgs.BadArgument("ExternalEntityParserCreate", "argument 1", "str or None", args[0]);
                string encoding = args.Length < 2 ? null
                    : PyArgs.ClinicStr("ExternalEntityParserCreate", "argument 2", "str", args[1]);
                return ExternalEntityParserCreate(Self(o), context, encoding);
            }, 1, 2);
            PyArgs.Define(type, "SetParamEntityParsing", PyCall.One, (o, args) => SetParamEntityParsing(Self(o), PyArgs.Int(args[0])));
            PyArgs.Define(type, "UseForeignDTD", PyCall.Defining, (o, args) =>
                UseForeignDTD(Self(o), args.Length < 1 || args[0].IsTrue()), 0, 1);
            PyArgs.Define(type, "SetBillionLaughsAttackProtectionActivationThreshold", PyCall.Defining, (o, args) =>
                set_activation_threshold(Self(o), UnsignedLongLongConverter(args[0]),
                                         XML_SetBillionLaughsAttackProtectionActivationThreshold), 1, 1);
            PyArgs.Define(type, "SetBillionLaughsAttackProtectionMaximumAmplification", PyCall.Defining, (o, args) =>
                set_maximum_amplification(Self(o), FloatConverter(args[0]),
                                          XML_SetBillionLaughsAttackProtectionMaximumAmplification), 1, 1);
            PyArgs.Define(type, "SetAllocTrackerActivationThreshold", PyCall.Defining, (o, args) =>
                set_activation_threshold(Self(o), UnsignedLongLongConverter(args[0]),
                                         XML_SetAllocTrackerActivationThreshold), 1, 1);
            PyArgs.Define(type, "SetAllocTrackerMaximumAmplification", PyCall.Defining, (o, args) =>
                set_maximum_amplification(Self(o), FloatConverter(args[0]),
                                          XML_SetAllocTrackerMaximumAmplification), 1, 1);
            PyArgs.Define(type, "SetReparseDeferralEnabled", PyCall.One, (o, args) =>
                SetReparseDeferralEnabled(Self(o), args[0].IsTrue()));
            PyArgs.Define(type, "GetReparseDeferralEnabled", PyCall.NoArgs, (o, args) => GetReparseDeferralEnabled(Self(o)));

            // xmlparse_members
            type.TypeDict["intern"] = new PyMemberDescriptor("intern", type,
                o => (PyObject)Self(o).intern ?? PyNone.Instance, readOnly: true);

            // xmlparse_getsetlist
            void Getter(string name, Func<xmlparseobject, PyObject> get) =>
                type.TypeDict[name] = new PyGetSetDescriptor(name, type, o => get(Self(o)));
            void GetterSetter(string name, Func<xmlparseobject, PyObject> get, Action<xmlparseobject, PyObject> set) =>
                type.TypeDict[name] = new PyGetSetDescriptor(name, type, o => get(Self(o)), (o, v) => set(Self(o), v),
                                                             deleter: o => set(Self(o), null));
            Getter("ErrorCode", s => PyInt.From((long)XML_GetErrorCode(s.itself)));
            Getter("ErrorLineNumber", s => PyInt.From((long)(int)XML_GetCurrentLineNumber(s.itself)));
            Getter("ErrorColumnNumber", s => PyInt.From((long)(int)XML_GetCurrentColumnNumber(s.itself)));
            Getter("ErrorByteIndex", s => PyInt.From((long)(int)XML_GetCurrentByteIndex(s.itself)));
            Getter("CurrentLineNumber", s => PyInt.From((long)(int)XML_GetCurrentLineNumber(s.itself)));
            Getter("CurrentColumnNumber", s => PyInt.From((long)(int)XML_GetCurrentColumnNumber(s.itself)));
            Getter("CurrentByteIndex", s => PyInt.From((long)(int)XML_GetCurrentByteIndex(s.itself)));
            GetterSetter("buffer_size", s => PyInt.From(s.buffer_size), xmlparse_buffer_size_setter);
            GetterSetter("buffer_text", s => PyBool.FromBool(s.buffer != null), xmlparse_buffer_text_setter);
            Getter("buffer_used", s => PyInt.From(s.buffer_used));
            GetterSetter("namespace_prefixes", s => PyBool.FromBool(s.ns_prefixes), xmlparse_namespace_prefixes_setter);
            GetterSetter("ordered_attributes", s => PyBool.FromBool(s.ordered_attributes), xmlparse_ordered_attributes_setter);
            GetterSetter("specified_attributes", s => PyBool.FromBool(s.specified_attributes), xmlparse_specified_attributes_setter);
            return type;
        }

        // _PyLong_UnsignedLongLong_Converter
        private static ulong UnsignedLongLongConverter(PyObject obj)
        {
            if (obj is PyInt i && i.Value < 0)
                throw PyValueError.Create("value must be positive");
            if (obj is not PyInt)
                obj = PyObject.IndexOrThrow(obj);
            var v = ((PyInt)obj).Value;
            if (v < 0)
                throw PyValueError.Create("value must be positive");
            if (v > ulong.MaxValue)
                throw PyOverflowError.Create("int too big to convert");
            return (ulong)v;
        }

        // the clinic's float converter: PyFloat_AsDouble, then (float)
        private static float FloatConverter(PyObject obj)
        {
            if (obj is PyFloat f)
                return (float)f.Value;
            return (float)obj.ToFloat();
        }

        /// <summary>init_handler_descrs: one getset per handler, closure = its handler_info entry.</summary>
        private static void init_handler_descrs()
        {
            for (int i = 0; i < handler_info.Length; i++)
            {
                int handlernum = i;
                string name = handler_info[i].name;
                if (xml_parse_type.TypeDict.ContainsKey(name))
                    continue;
                xml_parse_type.TypeDict[name] = new PyGetSetDescriptor(name, xml_parse_type,
                    o => xmlparse_handler_getter(Self(o), handlernum),
                    (o, v) => xmlparse_handler_setter(Self(o), v, handlernum),
                    deleter: o => xmlparse_handler_setter(Self(o), null, handlernum));
            }
        }

        /// <summary>clear_handlers</summary>
        private static void clear_handlers(xmlparseobject self, bool initial)
        {
            for (int i = 0; i < handler_info.Length; i++)
            {
                if (initial)
                    self.handlers[i] = null;
                else
                {
                    self.handlers[i] = null;
                    handler_info[i].setter(self.itself, null);
                }
            }
        }

        // handler_info[]
        private static HandlerInfo[] MakeHandlerInfo()
        {
            HandlerInfo H(string name, Action<XML_Parser, Delegate> setter, Delegate handler) =>
                new HandlerInfo { name = name, setter = setter, handler = handler };
            return new[]
            {
                H("StartElementHandler", (p, h) => XML_SetStartElementHandler(p, (XML_StartElementHandler)h),
                  (XML_StartElementHandler)my_StartElementHandler),
                H("EndElementHandler", (p, h) => XML_SetEndElementHandler(p, (XML_EndElementHandler)h),
                  (XML_EndElementHandler)my_EndElementHandler),
                H("ProcessingInstructionHandler", (p, h) => XML_SetProcessingInstructionHandler(p, (XML_ProcessingInstructionHandler)h),
                  (XML_ProcessingInstructionHandler)my_ProcessingInstructionHandler),
                H("CharacterDataHandler", (p, h) => XML_SetCharacterDataHandler(p, (XML_CharacterDataHandler)h),
                  (XML_CharacterDataHandler)my_CharacterDataHandler),
                H("UnparsedEntityDeclHandler", (p, h) => XML_SetUnparsedEntityDeclHandler(p, (XML_UnparsedEntityDeclHandler)h),
                  (XML_UnparsedEntityDeclHandler)my_UnparsedEntityDeclHandler),
                H("NotationDeclHandler", (p, h) => XML_SetNotationDeclHandler(p, (XML_NotationDeclHandler)h),
                  (XML_NotationDeclHandler)my_NotationDeclHandler),
                H("StartNamespaceDeclHandler", (p, h) => XML_SetStartNamespaceDeclHandler(p, (XML_StartNamespaceDeclHandler)h),
                  (XML_StartNamespaceDeclHandler)my_StartNamespaceDeclHandler),
                H("EndNamespaceDeclHandler", (p, h) => XML_SetEndNamespaceDeclHandler(p, (XML_EndNamespaceDeclHandler)h),
                  (XML_EndNamespaceDeclHandler)my_EndNamespaceDeclHandler),
                H("CommentHandler", (p, h) => XML_SetCommentHandler(p, (XML_CommentHandler)h),
                  (XML_CommentHandler)my_CommentHandler),
                H("StartCdataSectionHandler", (p, h) => XML_SetStartCdataSectionHandler(p, (XML_StartCdataSectionHandler)h),
                  (XML_StartCdataSectionHandler)my_StartCdataSectionHandler),
                H("EndCdataSectionHandler", (p, h) => XML_SetEndCdataSectionHandler(p, (XML_EndCdataSectionHandler)h),
                  (XML_EndCdataSectionHandler)my_EndCdataSectionHandler),
                H("DefaultHandler", (p, h) => XML_SetDefaultHandler(p, (XML_DefaultHandler)h),
                  (XML_DefaultHandler)my_DefaultHandler),
                H("DefaultHandlerExpand", (p, h) => XML_SetDefaultHandlerExpand(p, (XML_DefaultHandler)h),
                  (XML_DefaultHandler)my_DefaultHandlerExpandHandler),
                H("NotStandaloneHandler", (p, h) => XML_SetNotStandaloneHandler(p, (XML_NotStandaloneHandler)h),
                  (XML_NotStandaloneHandler)my_NotStandaloneHandler),
                H("ExternalEntityRefHandler", (p, h) => XML_SetExternalEntityRefHandler(p, (XML_ExternalEntityRefHandler)h),
                  (XML_ExternalEntityRefHandler)my_ExternalEntityRefHandler),
                H("StartDoctypeDeclHandler", (p, h) => XML_SetStartDoctypeDeclHandler(p, (XML_StartDoctypeDeclHandler)h),
                  (XML_StartDoctypeDeclHandler)my_StartDoctypeDeclHandler),
                H("EndDoctypeDeclHandler", (p, h) => XML_SetEndDoctypeDeclHandler(p, (XML_EndDoctypeDeclHandler)h),
                  (XML_EndDoctypeDeclHandler)my_EndDoctypeDeclHandler),
                H("EntityDeclHandler", (p, h) => XML_SetEntityDeclHandler(p, (XML_EntityDeclHandler)h),
                  (XML_EntityDeclHandler)my_EntityDeclHandler),
                H("XmlDeclHandler", (p, h) => XML_SetXmlDeclHandler(p, (XML_XmlDeclHandler)h),
                  (XML_XmlDeclHandler)my_XmlDeclHandler),
                H("ElementDeclHandler", (p, h) => XML_SetElementDeclHandler(p, (XML_ElementDeclHandler)h),
                  (XML_ElementDeclHandler)my_ElementDeclHandler),
                H("AttlistDeclHandler", (p, h) => XML_SetAttlistDeclHandler(p, (XML_AttlistDeclHandler)h),
                  (XML_AttlistDeclHandler)my_AttlistDeclHandler),
                H("SkippedEntityHandler", (p, h) => XML_SetSkippedEntityHandler(p, (XML_SkippedEntityHandler)h),
                  (XML_SkippedEntityHandler)my_SkippedEntityHandler),
            };
        }

        // ------------------------------------------------------------------ module

        /// <summary>pyexpat_ParserCreate_impl</summary>
        private static PyObject ParserCreate(string encoding, string namespace_separator, PyObject intern)
        {
            if (namespace_separator != null && Encoding.UTF8.GetByteCount(namespace_separator) > 1)
                throw PyValueError.Create("namespace_separator must be at most one character, omitted, or None");
            /* Explicitly passing None means no interning is desired.
               Not passing anything means that a new dictionary is used. */
            PyDict internDict;
            if (intern is PyNone)
                internDict = null;
            else if (intern == null)
                internDict = new PyDict();
            else if (intern is not PyDict d)
                throw PyTypeError.Create("intern must be a dictionary");
            else
                internDict = d;
            return newxmlparseobject(encoding, namespace_separator, internDict);
        }

        /// <summary>pyexpat_ErrorString_impl</summary>
        private static PyObject ErrorString(long code)
        {
            string s = XML_ErrorString((XML_Error)(int)code);
            return s == null ? PyNone.Instance : PyString.From(s);
        }

        /// <summary>add_submodule: the module, also in sys.modules under its full name.</summary>
        private static PyModule add_submodule(PyModule mod, string fullname)
        {
            string name = fullname.Substring(fullname.LastIndexOf('.') + 1);
            var submodule = new PyModule(fullname);
            PyImportSystem.SetModule(fullname, submodule);
            mod.ModuleDict[name] = submodule;
            return submodule;
        }

        // struct ErrorInfo error_info_of[]
        private static readonly (string name, string description)[] error_info_of =
        {
            (null, null),
            ("XML_ERROR_NO_MEMORY", "out of memory"),
            ("XML_ERROR_SYNTAX", "syntax error"),
            ("XML_ERROR_NO_ELEMENTS", "no element found"),
            ("XML_ERROR_INVALID_TOKEN", "not well-formed (invalid token)"),
            ("XML_ERROR_UNCLOSED_TOKEN", "unclosed token"),
            ("XML_ERROR_PARTIAL_CHAR", "partial character"),
            ("XML_ERROR_TAG_MISMATCH", "mismatched tag"),
            ("XML_ERROR_DUPLICATE_ATTRIBUTE", "duplicate attribute"),
            ("XML_ERROR_JUNK_AFTER_DOC_ELEMENT", "junk after document element"),
            ("XML_ERROR_PARAM_ENTITY_REF", "illegal parameter entity reference"),
            ("XML_ERROR_UNDEFINED_ENTITY", "undefined entity"),
            ("XML_ERROR_RECURSIVE_ENTITY_REF", "recursive entity reference"),
            ("XML_ERROR_ASYNC_ENTITY", "asynchronous entity"),
            ("XML_ERROR_BAD_CHAR_REF", "reference to invalid character number"),
            ("XML_ERROR_BINARY_ENTITY_REF", "reference to binary entity"),
            ("XML_ERROR_ATTRIBUTE_EXTERNAL_ENTITY_REF", "reference to external entity in attribute"),
            ("XML_ERROR_MISPLACED_XML_PI", "XML or text declaration not at start of entity"),
            ("XML_ERROR_UNKNOWN_ENCODING", "unknown encoding"),
            ("XML_ERROR_INCORRECT_ENCODING", "encoding specified in XML declaration is incorrect"),
            ("XML_ERROR_UNCLOSED_CDATA_SECTION", "unclosed CDATA section"),
            ("XML_ERROR_EXTERNAL_ENTITY_HANDLING", "error in processing external entity reference"),
            ("XML_ERROR_NOT_STANDALONE", "document is not standalone"),
            ("XML_ERROR_UNEXPECTED_STATE", "unexpected parser state - please send a bug report"),
            ("XML_ERROR_ENTITY_DECLARED_IN_PE", "entity declared in parameter entity"),
            ("XML_ERROR_FEATURE_REQUIRES_XML_DTD", "requested feature requires XML_DTD support in Expat"),
            ("XML_ERROR_CANT_CHANGE_FEATURE_ONCE_PARSING", "cannot change setting once parsing has begun"),
            /* Added in Expat 1.95.7. */
            ("XML_ERROR_UNBOUND_PREFIX", "unbound prefix"),
            /* Added in Expat 1.95.8. */
            ("XML_ERROR_UNDECLARING_PREFIX", "must not undeclare prefix"),
            ("XML_ERROR_INCOMPLETE_PE", "incomplete markup in parameter entity"),
            ("XML_ERROR_XML_DECL", "XML declaration not well-formed"),
            ("XML_ERROR_TEXT_DECL", "text declaration not well-formed"),
            ("XML_ERROR_PUBLICID", "illegal character(s) in public id"),
            ("XML_ERROR_SUSPENDED", "parser suspended"),
            ("XML_ERROR_NOT_SUSPENDED", "parser not suspended"),
            ("XML_ERROR_ABORTED", "parsing aborted"),
            ("XML_ERROR_FINISHED", "parsing finished"),
            ("XML_ERROR_SUSPEND_PE", "cannot suspend in external parameter entity"),
            /* Added in 2.0.0. */
            ("XML_ERROR_RESERVED_PREFIX_XML", "reserved prefix (xml) must not be undeclared or bound to another namespace name"),
            ("XML_ERROR_RESERVED_PREFIX_XMLNS", "reserved prefix (xmlns) must not be declared or undeclared"),
            ("XML_ERROR_RESERVED_NAMESPACE_URI", "prefix must not be bound to one of the reserved namespace names"),
            /* Added in 2.2.1. */
            ("XML_ERROR_INVALID_ARGUMENT", "invalid argument"),
            /* Added in 2.3.0. */
            ("XML_ERROR_NO_BUFFER", "a successful prior call to function XML_GetBuffer is required"),
            /* Added in 2.4.0. */
            ("XML_ERROR_AMPLIFICATION_LIMIT_BREACH", "limit on input amplification factor (from DTD and entities) breached"),
        };

        /// <summary>add_error</summary>
        private static void add_error(PyModule errors_module, PyDict codes_dict, PyDict rev_codes_dict, int error_index)
        {
            string name = error_info_of[error_index].name;
            int error_code = error_index;

            /* NOTE: This keeps the source of truth regarding error
             *       messages with libexpat and (by definition) in bulletproof sync
             *       with the other uses of the XML_ErrorString function
             *       elsewhere within this file.  pyexpat's copy of the messages
             *       only acts as a fallback in case of outdated runtime libexpat,
             *       where it returns NULL. */
            string error_string = XML_ErrorString((XML_Error)error_code);
            if (error_string == null)
                error_string = error_info_of[error_index].description;

            errors_module.ModuleDict[name] = PyString.From(error_string);
            PyObject num = PyInt.From(error_code);
            codes_dict.SetItem(PyString.From(error_string), num);
            rev_codes_dict.SetItem(num, PyString.From(error_string));
        }

        /// <summary>add_errors_module</summary>
        private static void add_errors_module(PyModule mod)
        {
            PyModule errors_module = add_submodule(mod, MODULE_NAME + ".errors");
            var codes_dict = new PyDict();
            var rev_codes_dict = new PyDict();
            for (int error_index = 0; error_index < error_info_of.Length; error_index++)
            {
                if (error_info_of[error_index].name == null)
                    continue;
                add_error(errors_module, codes_dict, rev_codes_dict, error_index);
            }
            errors_module.ModuleDict["__doc__"] = PyString.From("Constants used to describe error conditions.");
            errors_module.ModuleDict["codes"] = codes_dict;
            errors_module.ModuleDict["messages"] = rev_codes_dict;
        }

        /// <summary>add_model_module</summary>
        private static void add_model_module(PyModule mod)
        {
            PyModule model_module = add_submodule(mod, MODULE_NAME + ".model");
            model_module.ModuleDict["__doc__"] = PyString.From("Constants used to interpret content model information.");
            model_module.ModuleDict["XML_CTYPE_EMPTY"] = PyInt.From((int)XML_Content_Type.XML_CTYPE_EMPTY);
            model_module.ModuleDict["XML_CTYPE_ANY"] = PyInt.From((int)XML_Content_Type.XML_CTYPE_ANY);
            model_module.ModuleDict["XML_CTYPE_MIXED"] = PyInt.From((int)XML_Content_Type.XML_CTYPE_MIXED);
            model_module.ModuleDict["XML_CTYPE_NAME"] = PyInt.From((int)XML_Content_Type.XML_CTYPE_NAME);
            model_module.ModuleDict["XML_CTYPE_CHOICE"] = PyInt.From((int)XML_Content_Type.XML_CTYPE_CHOICE);
            model_module.ModuleDict["XML_CTYPE_SEQ"] = PyInt.From((int)XML_Content_Type.XML_CTYPE_SEQ);
            model_module.ModuleDict["XML_CQUANT_NONE"] = PyInt.From((int)XML_Content_Quant.XML_CQUANT_NONE);
            model_module.ModuleDict["XML_CQUANT_OPT"] = PyInt.From((int)XML_Content_Quant.XML_CQUANT_OPT);
            model_module.ModuleDict["XML_CQUANT_REP"] = PyInt.From((int)XML_Content_Quant.XML_CQUANT_REP);
            model_module.ModuleDict["XML_CQUANT_PLUS"] = PyInt.From((int)XML_Content_Quant.XML_CQUANT_PLUS);
        }

        /// <summary>add_features</summary>
        private static void add_features(PyModule mod)
        {
            var list = new PyList();
            foreach (var feature in XML_GetFeatureList())
                list.Append(PyTuple.From(new PyObject[] { PyString.From(feature.name), PyInt.From(feature.value) }));
            mod.ModuleDict["features"] = list;
        }

        /// <summary>pyexpat_exec</summary>
        private static void pyexpat_exec(PyModule mod)
        {
            mod.ModuleDict["__doc__"] = PyString.From("Python wrapper for Expat parser.");
            if (xml_parse_type == null)
            {
                xml_parse_type = MakeXmlParseType();
                init_handler_descrs();
                error = PyExceptionSlots.NewException("xml.parsers.expat", "ExpatError", PyType.ExceptionType);
            }

            mod.ModuleDict["ParserCreate"] = new PyBuiltinFunction("ParserCreate", (args, kwargs) =>
            {
                var a = PyArgs.Unpack("ParserCreate", args, kwargs, new[] { "encoding", "namespace_separator", "intern" }, 0, 0, 3);
                string encoding = a[0] == null || a[0] is PyNone ? null
                    : a[0] is PyString ? PyArgs.ClinicStr("ParserCreate", "argument 'encoding'", "str or None", a[0])
                    : throw PyArgs.BadArgument("ParserCreate", "argument 'encoding'", "str or None", a[0]);
                string sep = a[1] == null || a[1] is PyNone ? null
                    : a[1] is PyString ? PyArgs.ClinicStr("ParserCreate", "argument 'namespace_separator'", "str or None", a[1])
                    : throw PyArgs.BadArgument("ParserCreate", "argument 'namespace_separator'", "str or None", a[1]);
                return ParserCreate(encoding, sep, a[2]);
            }) { OwnerModule = MODULE_NAME };
            mod.ModuleDict["ErrorString"] = PyArgs.MethO(MODULE_NAME, "ErrorString", args => ErrorString(PyArgs.CLong(args[0])));

            mod.ModuleDict["error"] = error;
            mod.ModuleDict["ExpatError"] = error;
            mod.ModuleDict["XMLParserType"] = xml_parse_type;
            mod.ModuleDict["EXPAT_VERSION"] = PyString.From(XML_ExpatVersion());
            mod.ModuleDict["version_info"] = PyTuple.From(new PyObject[]
            {
                PyInt.From(XML_MAJOR_VERSION), PyInt.From(XML_MINOR_VERSION), PyInt.From(XML_MICRO_VERSION)
            });
            /* XXX When Expat supports some way of figuring out how it was
               compiled, this should check and set native_encoding
               appropriately.
            */
            mod.ModuleDict["native_encoding"] = PyString.From("UTF-8");

            add_errors_module(mod);
            add_model_module(mod);
            add_features(mod);

            mod.ModuleDict["XML_PARAM_ENTITY_PARSING_NEVER"] = PyInt.From(0);
            mod.ModuleDict["XML_PARAM_ENTITY_PARSING_UNLESS_STANDALONE"] = PyInt.From(1);
            mod.ModuleDict["XML_PARAM_ENTITY_PARSING_ALWAYS"] = PyInt.From(2);

            mod.ModuleDict["expat_CAPI"] = new PyCapsule("pyexpat.expat_CAPI", new PyExpat_CAPI());
        }
    }
}
