using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace SharpPy.Modules.Cjk
{
    /// <summary>MultibyteCodec_State: eight bytes a codec keeps between calls.</summary>
    internal sealed class MbState
    {
        internal byte[] C = new byte[8];
        internal MbState Clone() => new() { C = (byte[])C.Clone() };
    }

    /// <summary>The output of an encoder: outbuf, the array and the position written next.</summary>
    internal sealed class MbOut
    {
        internal byte[] Buf;
        internal int Pos;
    }

    /// <summary>The input of a decoder: *inbuf, the array and the position read next.</summary>
    internal sealed class MbIn
    {
        internal byte[] Buf;
        internal int Pos;
    }

    /// <summary>_PyUnicodeWriter, for the code points a decoder writes.</summary>
    internal sealed class MbWriter
    {
        internal readonly StringBuilder Text = new();
        internal int Pos;

        internal void Write(int c)
        {
            if (c >= 0x10000)
                Text.Append(char.ConvertFromUtf32(c));
            else
                Text.Append((char)c);
            Pos++;
        }

        internal void WriteStr(string s)
        {
            Text.Append(s);
            Pos += PyString.CodePointCount(s);
        }
    }

    /// <summary>
    /// MultibyteCodec (multibytecodec.h): one codec of a cjkcodecs module - encode, decode, and the
    /// optional codecinit, encinit, encreset, decinit and decreset, as the codec's C functions.
    /// The return values are the C ones: 0, the length of an illegal sequence, or an MBERR_.
    /// </summary>
    internal abstract class MbCodec
    {
        internal const int MBERR_TOOSMALL = -1, MBERR_TOOFEW = -2, MBERR_INTERNAL = -3, MBERR_EXCEPTION = -4;
        internal const int MBENC_FLUSH = 0x0001, MBENC_MAX = MBENC_FLUSH, MBENC_RESET = MBENC_MAX << 1;

        internal string Encoding;
        internal CjkModule ModState;

        internal virtual int CodecInit() => 0;
        internal abstract long Encode(MbState state, int[] data, ref long inpos, long inlen, MbOut outbuf, long outleft, int flags);
        internal virtual bool HasEncInit => false;
        internal virtual int EncInit(MbState state) => 0;
        internal virtual bool HasEncReset => false;
        internal virtual long EncReset(MbState state, MbOut outbuf, long outleft) => 0;
        internal abstract long Decode(MbState state, MbIn inbuf, long inleft, MbWriter writer);
        internal virtual bool HasDecInit => false;
        internal virtual int DecInit(MbState state) => 0;
        internal virtual bool HasDecReset => false;
        internal virtual long DecReset(MbState state) => 0;
    }

    /// <summary>A cjkcodecs module's state (cjkcodecs_module_state): its maps and its codecs.</summary>
    internal sealed class CjkModule
    {
        internal readonly string Locale;
        internal readonly List<DbcsMap> Mappings = new();
        internal readonly List<MbCodec> Codecs = new();
        internal PyModule Module;

        internal CjkModule(string locale) => Locale = locale;

        // getcodec: _multibytecodec.__create_codec over a capsule of the codec, or LookupError.
        internal PyModule Create()
        {
            var module = new PyModule("_codecs_" + Locale);
            Module = module;
            foreach (var codec in Codecs)
                codec.ModState = this;
            module.ModuleDict["getcodec"] = PyArgs.MethO(module.Name, "getcodec", args =>
            {
                if (args[0] is not PyString encoding)
                    throw PyTypeError.Create("encoding name must be a string.");
                string enc = encoding.Value;
                foreach (var codec in Codecs)
                    if (codec.Encoding == enc)
                    {
                        var create = PyImportSystem.Import("_multibytecodec").GetAttribute("__create_codec");
                        return create.Call(new PyObject[] { new PyCapsule("multibytecodec.codec", (codec, module)) }, null);
                    }
                throw PyLookupError.Create("no such codec is supported.");
            });
            foreach (var map in Mappings)
                module.ModuleDict["__map_" + map.Charset] = new PyCapsule("multibytecodec.map", map);
            return module;
        }

        // importmap: another module's __map_<charset>, a capsule of its maps.
        internal static DbcsMap ImportMap(string modname, string charset)
        {
            var o = PyImportSystem.Import(modname).GetAttribute("__map_" + charset);
            if (o is not PyCapsule { Name: "multibytecodec.map", Pointer: DbcsMap map })
                throw PyValueError.Create("map data must be a Capsule.");
            return map;
        }
    }

    /// <summary>Modules/cjkcodecs/multibytecodec.c: the codec object, the incremental encoder and decoder, the stream reader and writer.</summary>
    internal static class MultibyteCodecModule
    {
        private const int MAXENCPENDING = 2, MAXDECPENDING = 8;

        internal static readonly PyType CodecType = MakeCodecType();
        internal static readonly PyType EncoderType = MakeEncoderType();
        internal static readonly PyType DecoderType = MakeDecoderType();
        internal static readonly PyType ReaderType = MakeReaderType();
        internal static readonly PyType WriterType = MakeWriterType();

        internal static PyModule Create()
        {
            var module = new PyModule("_multibytecodec");
            module.ModuleDict["__create_codec"] = PyArgs.MethO("_multibytecodec", "__create_codec", args =>
            {
                if (args[0] is not PyCapsule { Name: "multibytecodec.codec", Pointer: ValueTuple<MbCodec, PyModule> data })
                    throw PyValueError.Create("argument type invalid");
                var codec = data.Item1;
                if (codec.CodecInit() != 0)
                    throw PySystemError.Create("error return without exception set");
                return new CodecObject(codec, data.Item2);
            });
            module.ModuleDict["MultibyteIncrementalEncoder"] = EncoderType;
            module.ModuleDict["MultibyteIncrementalDecoder"] = DecoderType;
            module.ModuleDict["MultibyteStreamReader"] = ReaderType;
            module.ModuleDict["MultibyteStreamWriter"] = WriterType;
            return module;
        }

        // str as code points, a lone surrogate standing for itself.
        internal static int[] CodePoints(string text)
        {
            var points = new int[PyString.CodePointCount(text)];
            for (int unit = 0, i = 0; unit < text.Length; i++)
            {
                points[i] = PyString.CodePointAt(text, unit, out int width);
                unit += width;
            }
            return points;
        }

        private static string FromCodePoints(int[] points, int start, int end)
        {
            var sb = new StringBuilder();
            for (int i = start; i < end; i++)
                sb.Append(PyString.CodePointText(points[i]));
            return sb.ToString();
        }

        // ---- encoding

        private sealed class EncodeBuffer
        {
            internal PyString InObj;
            internal int[] Data;
            internal long InPos, InLen;
            internal MbOut Out = new();
            internal PyObject ExcObj;
        }

        // expand_encodebuffer
        private static void ExpandEncodeBuffer(EncodeBuffer buf, long esize)
        {
            long orgsize = buf.Out.Buf.Length;
            long incsize = esize < (orgsize >> 1) ? (orgsize >> 1) | 1 : esize;
            if (orgsize + incsize > Array.MaxLength)
                throw PyMemoryError.Create();
            Array.Resize(ref buf.Out.Buf, (int)(orgsize + incsize));
        }

        // REQUIRE_ENCODEBUFFER
        private static void RequireEncodeBuffer(EncodeBuffer buf, long s)
        {
            if (s < 0 || s > buf.Out.Buf.Length - buf.Out.Pos)
                ExpandEncodeBuffer(buf, s);
        }

        private static long OutLeft(EncodeBuffer buf) => buf.Out.Buf.Length - buf.Out.Pos;

        // PyCodec_StrictErrors: the exception, raised.
        private static Exception Strict(PyObject exc) => new PythonException((PyBaseException)exc);

        // multibytecodec_encerror
        private static void EncError(MbCodec codec, MbState state, EncodeBuffer buf, string errors, long e)
        {
            string reason;
            long esize;
            if (e > 0)
            {
                reason = "illegal multibyte sequence";
                esize = e;
            }
            else
            {
                switch (e)
                {
                    case MbCodec.MBERR_TOOSMALL:
                        RequireEncodeBuffer(buf, -1);
                        return;
                    case MbCodec.MBERR_TOOFEW:
                        reason = "incomplete multibyte sequence";
                        esize = buf.InPos;
                        break;
                    case MbCodec.MBERR_INTERNAL:
                        throw PyRuntimeError.Create("internal codec error");
                    default:
                        throw PyRuntimeError.Create("unknown runtime error");
                }
            }
            if (errors == "replace")
            {
                long r;
                long inpos = 0;
                var replchar = new[] { (int)'?' };
                for (;;)
                {
                    r = codec.Encode(state, replchar, ref inpos, 1, buf.Out, OutLeft(buf), 0);
                    if (r == MbCodec.MBERR_TOOSMALL)
                    {
                        RequireEncodeBuffer(buf, -1);
                        continue;
                    }
                    break;
                }
                if (r != 0)
                {
                    RequireEncodeBuffer(buf, 1);
                    buf.Out.Buf[buf.Out.Pos++] = (byte)'?';
                }
            }
            if (errors == "ignore" || errors == "replace")
            {
                buf.InPos += esize;
                return;
            }
            long start = buf.InPos, end = start + esize;
            if (buf.ExcObj == null)
                buf.ExcObj = PyType.UnicodeEncodeErrorType.Call(new PyObject[]
                    { PyString.From(codec.Encoding), buf.InObj, PyInt.From(start), PyInt.From(end), PyString.From(reason) }, null);
            else
            {
                buf.ExcObj.SetAttribute("start", PyInt.From(start));
                buf.ExcObj.SetAttribute("end", PyInt.From(end));
                buf.ExcObj.SetAttribute("reason", PyString.From(reason));
            }
            if (errors == "strict")
                throw Strict(buf.ExcObj);
            var retobj = PyCodecErrors.LookupError(errors).Call(new[] { buf.ExcObj }, null);
            if (retobj is not PyTuple { Items.Length: 2 } pair || pair.Items[0] is not (PyString or PyBytes) || pair.Items[1] is not PyInt)
                throw PyTypeError.Create("encoding error handler must return (str, int) tuple");
            byte[] retstr;
            if (pair.Items[0] is PyString text)
            {
                long ignored = 0;
                retstr = Encode(codec, state, text, ref ignored, "strict", MbCodec.MBENC_FLUSH);
            }
            else
                retstr = ((PyBytes)pair.Items[0]).Value;
            if (retstr.Length > 0)
            {
                RequireEncodeBuffer(buf, retstr.Length);
                Array.Copy(retstr, 0, buf.Out.Buf, buf.Out.Pos, retstr.Length);
                buf.Out.Pos += retstr.Length;
            }
            var position = ((PyInt)pair.Items[1]).Value;
            long newpos;
            if (position < long.MinValue || position > long.MaxValue)
                newpos = -1;
            else
            {
                newpos = (long)position;
                if (newpos < 0)
                    newpos += buf.InLen;
            }
            if (newpos < 0 || newpos > buf.InLen)
                throw PyIndexError.Create($"position {newpos} from error handler out of bounds");
            buf.InPos = newpos;
        }

        // multibytecodec_encode
        internal static byte[] Encode(MbCodec codec, MbState state, PyString text, ref long inposT, string errors, int flags)
        {
            var data = CodePoints(text.Value);
            long datalen = data.Length;
            if (datalen == 0 && (flags & MbCodec.MBENC_RESET) == 0)
                return Array.Empty<byte>();
            var buf = new EncodeBuffer { InObj = text, Data = data, InPos = 0, InLen = datalen };
            if (datalen > (Array.MaxLength - 16) / 2)
                throw PyMemoryError.Create();
            buf.Out.Buf = new byte[datalen * 2 + 16];
            while (buf.InPos < buf.InLen)
            {
                long r = codec.Encode(state, buf.Data, ref buf.InPos, buf.InLen, buf.Out, OutLeft(buf), flags);
                if (r == 0 || (r == MbCodec.MBERR_TOOFEW && (flags & MbCodec.MBENC_FLUSH) == 0))
                    break;
                EncError(codec, state, buf, errors, r);
                if (r == MbCodec.MBERR_TOOFEW)
                    break;
            }
            if (codec.HasEncReset && (flags & MbCodec.MBENC_RESET) != 0)
                for (;;)
                {
                    long r = codec.EncReset(state, buf.Out, OutLeft(buf));
                    if (r == 0)
                        break;
                    EncError(codec, state, buf, errors, r);
                }
            inposT = buf.InPos;
            return buf.Out.Buf.AsSpan(0, buf.Out.Pos).ToArray();
        }

        // ---- decoding

        private sealed class DecodeBuffer
        {
            internal MbIn In = new();
            internal int Top, End;
            internal PyObject ExcObj;
            internal MbWriter Writer = new();
        }

        // multibytecodec_decerror
        private static void DecError(MbCodec codec, MbState state, DecodeBuffer buf, string errors, long e)
        {
            string reason;
            long esize;
            if (e > 0)
            {
                reason = "illegal multibyte sequence";
                esize = e;
            }
            else
            {
                switch (e)
                {
                    case MbCodec.MBERR_TOOSMALL:
                        return;
                    case MbCodec.MBERR_TOOFEW:
                        reason = "incomplete multibyte sequence";
                        esize = buf.End - buf.In.Pos;
                        break;
                    case MbCodec.MBERR_INTERNAL:
                        throw PyRuntimeError.Create("internal codec error");
                    case MbCodec.MBERR_EXCEPTION:
                        throw PySystemError.Create("error return without exception set");
                    default:
                        throw PyRuntimeError.Create("unknown runtime error");
                }
            }
            if (errors == "replace")
                buf.Writer.Write(0xFFFD);
            if (errors == "ignore" || errors == "replace")
            {
                buf.In.Pos += (int)esize;
                return;
            }
            long start = buf.In.Pos - buf.Top, end = start + esize;
            if (buf.ExcObj == null)
                buf.ExcObj = PyType.UnicodeDecodeErrorType.Call(new PyObject[]
                {
                    PyString.From(codec.Encoding), PyBytes.From(buf.In.Buf.AsSpan(buf.Top, buf.End - buf.Top).ToArray()),
                    PyInt.From(start), PyInt.From(end), PyString.From(reason),
                }, null);
            else
            {
                buf.ExcObj.SetAttribute("start", PyInt.From(start));
                buf.ExcObj.SetAttribute("end", PyInt.From(end));
                buf.ExcObj.SetAttribute("reason", PyString.From(reason));
            }
            if (errors == "strict")
                throw Strict(buf.ExcObj);
            var retobj = PyCodecErrors.LookupError(errors).Call(new[] { buf.ExcObj }, null);
            if (retobj is not PyTuple { Items.Length: 2 } pair || pair.Items[0] is not PyString retuni || pair.Items[1] is not PyInt)
                throw PyTypeError.Create("decoding error handler must return (str, int) tuple");
            buf.Writer.WriteStr(retuni.Value);
            var position = ((PyInt)pair.Items[1]).Value;
            long newpos;
            if (position < long.MinValue || position > long.MaxValue)
                newpos = -1;
            else
            {
                newpos = (long)position;
                if (newpos < 0)
                    newpos += buf.End - buf.Top;
            }
            if (newpos < 0 || buf.Top + newpos > buf.End)
                throw PyIndexError.Create($"position {newpos} from error handler out of bounds");
            buf.In.Pos = (int)(buf.Top + newpos);
        }

        // ---- the codec object

        internal sealed class CodecObject : PyObject
        {
            internal readonly MbCodec Codec;
            internal readonly PyModule CjkModule;
            internal CodecObject(MbCodec codec, PyModule cjkModule)
            {
                Codec = codec;
                CjkModule = cjkModule;
            }
            public override PyType GetPyType() => CodecType;
        }

        // errors: str(accept={str, NoneType}) = None
        private static string ErrorsArg(string fname, PyObject value) =>
            value == null || value is PyNone ? null : PyArgs.ClinicStr(fname, "argument 'errors'", "str or None", value);

        // internal_error_callback: strict, ignore and replace are the codec's own; any other name a handler's.
        private static string ErrorCallback(string errors) => errors ?? "strict";

        private static PyType MakeCodecType()
        {
            var type = new PyType("MultibyteCodec", new[] { PyType.ObjectType }, "_multibytecodec");
            type.MarkSpecHeapType();
            // _multibytecodec_MultibyteCodec_encode_impl: (input, errors=None)
            type.TypeDict["encode"] = new PyMethodDescriptor("encode", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("encode", args, kwargs, new[] { "input", "errors" }, 0, 1, 2);
                var codec = ((CodecObject)self).Codec;
                var input = parsed[0];
                string errors = ErrorsArg("encode", parsed[1]);
                if (input is not PyString text)
                {
                    var converted = input.AsString();
                    text = converted as PyString ?? throw PyTypeError.Create("couldn't convert the object to unicode.");
                }
                long datalen = PyString.CodePointCount(text.Value);
                string errorcb = ErrorCallback(errors);
                var state = new MbState();
                if (codec.HasEncInit && codec.EncInit(state) != 0)
                    throw PySystemError.Create("error return without exception set");
                long ignored = 0;
                var r = Encode(codec, state, text, ref ignored, errorcb, MbCodec.MBENC_FLUSH | MbCodec.MBENC_RESET);
                return PyTuple.From(new PyObject[] { PyBytes.From(r), PyInt.From(datalen) });
            }, acceptsKwargs: true);
            // _multibytecodec_MultibyteCodec_decode_impl: (input, errors=None)
            type.TypeDict["decode"] = new PyMethodDescriptor("decode", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decode", args, kwargs, new[] { "input", "errors" }, 0, 1, 2);
                var codec = ((CodecObject)self).Codec;
                var data = BytesMethods.RequireBuffer(parsed[0]);
                string errorcb = ErrorCallback(ErrorsArg("decode", parsed[1]));
                if (data.Length == 0)
                    return PyTuple.From(new PyObject[] { PyString.From(""), PyInt.From(0) });
                var buf = new DecodeBuffer();
                buf.In.Buf = data;
                buf.In.Pos = buf.Top = 0;
                buf.End = data.Length;
                var state = new MbState();
                if (codec.HasDecInit && codec.DecInit(state) != 0)
                    throw PySystemError.Create("error return without exception set");
                while (buf.In.Pos < buf.End)
                {
                    long inleft = buf.End - buf.In.Pos;
                    long r = codec.Decode(state, buf.In, inleft, buf.Writer);
                    if (r == 0)
                        break;
                    DecError(codec, state, buf, errorcb, r);
                }
                return PyTuple.From(new PyObject[] { PyString.From(buf.Writer.Text.ToString()), PyInt.From(data.Length) });
            }, acceptsKwargs: true);
            return type;
        }

        // ---- stateful contexts

        internal abstract class StatefulContext : PyObject
        {
            internal MbCodec Codec;
            internal MbState State = new();
            internal string Errors = "strict";
            protected StatefulContext(PyType type, PyType baseType)
            {
                if (!ReferenceEquals(type, baseType))
                    SetCustomType(type);
            }
        }

        internal abstract class EncoderContext : StatefulContext
        {
            internal PyString Pending;
            protected EncoderContext(PyType type, PyType baseType) : base(type, baseType) { }
        }

        internal abstract class DecoderContext : StatefulContext
        {
            internal byte[] Pending = new byte[MAXDECPENDING];
            internal int PendingSize;
            protected DecoderContext(PyType type, PyType baseType) : base(type, baseType) { }
        }

        private sealed class IncrementalEncoder : EncoderContext
        {
            internal IncrementalEncoder(PyType type) : base(type, EncoderType) { }
            public override PyType GetPyType() => _customType ?? EncoderType;
        }

        private sealed class IncrementalDecoder : DecoderContext
        {
            internal IncrementalDecoder(PyType type) : base(type, DecoderType) { }
            public override PyType GetPyType() => _customType ?? DecoderType;
        }

        private sealed class StreamReader : DecoderContext
        {
            internal PyObject Stream;
            internal StreamReader(PyType type) : base(type, ReaderType) { }
            public override PyType GetPyType() => _customType ?? ReaderType;
        }

        private sealed class StreamWriter : EncoderContext
        {
            internal PyObject Stream;
            internal StreamWriter(PyType type) : base(type, WriterType) { }
            public override PyType GetPyType() => _customType ?? WriterType;
        }

        // codecctx_getsets: errors
        private static void InstallErrors(PyType type)
        {
            type.TypeDict["errors"] = new PyGetSetDescriptor("errors", type,
                self => PyString.From(((StatefulContext)self).Errors),
                (self, value) =>
                {
                    if (value == null)
                        throw PyAttributeError.Create("cannot delete attribute");
                    if (value is not PyString text)
                        throw PyTypeError.Create("errors must be a string");
                    PyArgs.AsUtf8(text);
                    if (text.Value.IndexOf('\0') >= 0)
                        throw PyValueError.Create("embedded null character");
                    ((StatefulContext)self).Errors = text.Value;
                });
            // the type's tp_init: nothing.
            type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, (self, args, kwargs) => PyNone.Instance);
        }

        // The codec of a stateful type's class: its codec attribute, which must be a MultibyteCodec.
        private static MbCodec CodecOf(PyObject cls)
        {
            var codec = cls.GetAttribute("codec");
            if (codec is not CodecObject codecObject)
                throw PyTypeError.Create("codec is unexpected type");
            return codecObject.Codec;
        }

        // "|s": errors, a str without NUL, or None for the default.
        private static string ErrorsKeyword(string fname, PyObject value)
        {
            if (value == null)
                return null;
            return PyArgs.ClinicStr(fname, "argument 'errors'", "str", value);
        }

        // encoder_encode_stateful
        private static byte[] EncodeStateful(EncoderContext ctx, PyObject unistr, bool final)
        {
            if (unistr is not PyString text)
            {
                var converted = unistr.AsString();
                text = converted as PyString ?? throw PyTypeError.Create("couldn't convert the object to str.");
            }
            PyString origpending = null, inbuf;
            if (ctx.Pending != null)
            {
                origpending = ctx.Pending;
                inbuf = PyString.From(ctx.Pending.Value + text.Value);
                ctx.Pending = null;
            }
            else
                inbuf = text;
            long inpos = 0;
            long datalen = PyString.CodePointCount(inbuf.Value);
            byte[] r;
            try
            {
                r = Encode(ctx.Codec, ctx.State, inbuf, ref inpos, ctx.Errors,
                    final ? MbCodec.MBENC_FLUSH | MbCodec.MBENC_RESET : 0);
            }
            catch
            {
                ctx.Pending = origpending;
                throw;
            }
            if (inpos < datalen)
            {
                if (datalen - inpos > MAXENCPENDING)
                    throw PyExceptionSlots.Raise(PyType.UnicodeErrorType, "pending buffer overflow");
                var points = CodePoints(inbuf.Value);
                ctx.Pending = PyString.From(FromCodePoints(points, (int)inpos, (int)datalen));
            }
            return r;
        }

        // decoder_append_pending
        private static void AppendPending(DecoderContext ctx, DecodeBuffer buf)
        {
            int npendings = buf.End - buf.In.Pos;
            if (npendings + ctx.PendingSize > MAXDECPENDING)
                throw PyExceptionSlots.Raise(PyType.UnicodeErrorType, "pending buffer overflow");
            Array.Copy(buf.In.Buf, buf.In.Pos, ctx.Pending, ctx.PendingSize, npendings);
            ctx.PendingSize += npendings;
        }

        // decoder_prepare_buffer
        private static void PrepareBuffer(DecodeBuffer buf, byte[] data)
        {
            buf.In.Buf = data;
            buf.In.Pos = buf.Top = 0;
            buf.End = data.Length;
        }

        // decoder_feed_buffer
        private static void FeedBuffer(DecoderContext ctx, DecodeBuffer buf)
        {
            while (buf.In.Pos < buf.End)
            {
                long inleft = buf.End - buf.In.Pos;
                long r = ctx.Codec.Decode(ctx.State, buf.In, inleft, buf.Writer);
                if (r == 0 || r == MbCodec.MBERR_TOOFEW)
                    break;
                DecError(ctx.Codec, ctx.State, buf, ctx.Errors, r);
            }
        }

        private static void Init(StatefulContext self, PyObject cls, string errors, bool encoder)
        {
            self.Codec = CodecOf(cls);
            self.Errors = ErrorCallback(errors);
            if (encoder ? self.Codec.HasEncInit && self.Codec.EncInit(self.State) != 0
                        : self.Codec.HasDecInit && self.Codec.DecInit(self.State) != 0)
                throw PySystemError.Create("error return without exception set");
        }

        private static PyType MakeEncoderType()
        {
            var type = new PyType("MultibyteIncrementalEncoder", new[] { PyType.ObjectType }, "_multibytecodec");
            type.MarkSpecHeapType();
            // mbiencoder_new: "|s:IncrementalEncoder"
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("IncrementalEncoder", args, kwargs, new[] { "errors" }, 0, 0, 1);
                var self = new IncrementalEncoder((PyType)cls);
                Init(self, cls, ErrorsKeyword("IncrementalEncoder", parsed[0]), true);
                return self;
            }, acceptsKwargs: true);
            InstallErrors(type);
            // _multibytecodec_MultibyteIncrementalEncoder_encode_impl: (input, final=False)
            type.TypeDict["encode"] = new PyMethodDescriptor("encode", type, (self, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("encode", args, kwargs, new[] { "input", "final" }, 0, 1, 2);
                bool final = parsed[1] != null && parsed[1].IsTrue();
                return PyBytes.From(EncodeStateful((EncoderContext)self, parsed[0], final));
            }, acceptsKwargs: true);
            PyArgs.Define(type, "getstate", PyCall.NoArgs, (o, args) =>
            {
                var self = (EncoderContext)o;
                var statebytes = new List<byte>();
                if (self.Pending != null)
                {
                    var pending = System.Text.Encoding.UTF8.GetBytes(self.Pending.Value);
                    if (pending.Length > MAXENCPENDING * 4)
                        throw PyExceptionSlots.Raise(PyType.UnicodeErrorType, "pending buffer too large");
                    statebytes.Add((byte)pending.Length);
                    statebytes.AddRange(pending);
                }
                else
                    statebytes.Add(0);
                statebytes.AddRange(self.State.C);
                return PyInt.From(new BigInteger(statebytes.ToArray(), isUnsigned: true, isBigEndian: false));
            });
            PyArgs.Define(type, "setstate", PyCall.One, (o, args) =>
            {
                var self = (EncoderContext)o;
                if (args[0] is not PyInt statelong)
                    throw PyArgs.BadArgument("setstate", "argument", "int", args[0]);
                var statebytes = AsByteArray(statelong.Value, 1 + MAXENCPENDING * 4 + 8);
                if (statebytes[0] > MAXENCPENDING * 4)
                    throw PyExceptionSlots.Raise(PyType.UnicodeErrorType, "pending buffer too large");
                self.Pending = UnicodeDecoders.DecodeUtf8(statebytes.AsSpan(1, statebytes[0]).ToArray(), null, null);
                Array.Copy(statebytes, 1 + statebytes[0], self.State.C, 0, 8);
                return PyNone.Instance;
            });
            PyArgs.Define(type, "reset", PyCall.NoArgs, (o, args) =>
            {
                var self = (EncoderContext)o;
                if (self.Codec.HasEncReset)
                {
                    var outbuf = new MbOut { Buf = new byte[4] };
                    if (self.Codec.EncReset(self.State, outbuf, 4) != 0)
                        throw PySystemError.Create("error return without exception set");
                }
                self.Pending = null;
                return PyNone.Instance;
            });
            return type;
        }

        // _PyLong_AsByteArray(v, n, little_endian=1, is_signed=0)
        private static byte[] AsByteArray(BigInteger value, int n)
        {
            if (value.Sign < 0)
                throw PyOverflowError.Create("can't convert negative int to unsigned");
            var bytes = value.ToByteArray(isUnsigned: true, isBigEndian: false);
            if (bytes.Length > n && !(bytes.Length == 1 && bytes[0] == 0))
                throw PyOverflowError.Create("int too big to convert");
            var result = new byte[n];
            Array.Copy(bytes, result, Math.Min(bytes.Length, n));
            return result;
        }

        private static PyType MakeDecoderType()
        {
            var type = new PyType("MultibyteIncrementalDecoder", new[] { PyType.ObjectType }, "_multibytecodec");
            type.MarkSpecHeapType();
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("IncrementalDecoder", args, kwargs, new[] { "errors" }, 0, 0, 1);
                var self = new IncrementalDecoder((PyType)cls);
                Init(self, cls, ErrorsKeyword("IncrementalDecoder", parsed[0]), false);
                return self;
            }, acceptsKwargs: true);
            InstallErrors(type);
            // _multibytecodec_MultibyteIncrementalDecoder_decode_impl: (input, final=False)
            type.TypeDict["decode"] = new PyMethodDescriptor("decode", type, (o, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("decode", args, kwargs, new[] { "input", "final" }, 0, 1, 2);
                var self = (DecoderContext)o;
                var data = BytesMethods.RequireBuffer(parsed[0]);
                bool final = parsed[1] != null && parsed[1].IsTrue();
                var buf = new DecodeBuffer();
                int origpending = self.PendingSize;
                byte[] wdata;
                if (self.PendingSize == 0)
                    wdata = data;
                else
                {
                    wdata = new byte[data.Length + self.PendingSize];
                    Array.Copy(self.Pending, wdata, self.PendingSize);
                    data.CopyTo(wdata, self.PendingSize);
                    self.PendingSize = 0;
                }
                PrepareBuffer(buf, wdata);
                FeedBuffer(self, buf);
                if (final && buf.In.Pos < buf.End)
                {
                    try
                    {
                        DecError(self.Codec, self.State, buf, self.Errors, MbCodec.MBERR_TOOFEW);
                    }
                    catch
                    {
                        Array.Copy(wdata, self.Pending, origpending);
                        self.PendingSize = origpending;
                        throw;
                    }
                }
                if (buf.In.Pos < buf.End)
                    AppendPending(self, buf);
                return PyString.From(buf.Writer.Text.ToString());
            }, acceptsKwargs: true);
            PyArgs.Define(type, "getstate", PyCall.NoArgs, (o, args) =>
            {
                var self = (DecoderContext)o;
                return PyTuple.From(new PyObject[]
                {
                    PyBytes.From(self.Pending.AsSpan(0, self.PendingSize).ToArray()),
                    PyInt.From(new BigInteger(self.State.C, isUnsigned: true, isBigEndian: false)),
                });
            });
            PyArgs.Define(type, "setstate", PyCall.One, (o, args) =>
            {
                var self = (DecoderContext)o;
                if (args[0] is not PyTuple state)
                    throw PyArgs.BadArgument("setstate", "argument", "tuple", args[0]);
                if (state.Items.Length != 2 || state.Items[0] is not PyBytes buffer || state.Items[1] is not PyInt statelong)
                    throw PyTypeError.Create("setstate(): illegal state argument");
                var statebytes = AsByteArray(statelong.Value, 8);
                if (buffer.Value.Length > MAXDECPENDING)
                    throw PyExceptionSlots.Raise(PyType.UnicodeErrorType, "pending buffer too large");
                self.PendingSize = buffer.Value.Length;
                Array.Copy(buffer.Value, self.Pending, self.PendingSize);
                Array.Copy(statebytes, self.State.C, 8);
                return PyNone.Instance;
            });
            PyArgs.Define(type, "reset", PyCall.NoArgs, (o, args) =>
            {
                var self = (DecoderContext)o;
                if (self.Codec.HasDecReset && self.Codec.DecReset(self.State) != 0)
                    throw PySystemError.Create("error return without exception set");
                self.PendingSize = 0;
                return PyNone.Instance;
            });
            return type;
        }

        // mbstreamreader_iread
        private static PyObject StreamRead(StreamReader self, string method, long sizehint)
        {
            if (sizehint == 0)
                return PyString.From("");
            var buf = new DecodeBuffer();
            for (;;)
            {
                var cres = sizehint < 0
                    ? self.Stream.GetAttribute(method).Call(Array.Empty<PyObject>(), null)
                    : self.Stream.GetAttribute(method).Call(new PyObject[] { PyInt.From(sizehint) }, null);
                if (cres is not PyBytes bytes)
                    throw PyTypeError.Create($"stream function returned a non-bytes object ({cres.GetPyType().FullName})");
                bool endoffile = bytes.Value.Length == 0;
                var data = bytes.Value;
                if (self.PendingSize > 0)
                {
                    var ctr = new byte[data.Length + self.PendingSize];
                    Array.Copy(self.Pending, ctr, self.PendingSize);
                    data.CopyTo(ctr, self.PendingSize);
                    data = ctr;
                    self.PendingSize = 0;
                }
                int rsize = data.Length;
                PrepareBuffer(buf, data);
                if (rsize > 0)
                    FeedBuffer(self, buf);
                if (endoffile || sizehint < 0)
                {
                    if (buf.In.Pos < buf.End)
                        DecError(self.Codec, self.State, buf, self.Errors, MbCodec.MBERR_TOOFEW);
                }
                if (buf.In.Pos < buf.End)
                    AppendPending(self, buf);
                if (sizehint < 0 || buf.Writer.Pos != 0 || rsize == 0)
                    break;
                sizehint = 1;
            }
            return PyString.From(buf.Writer.Text.ToString());
        }

        // read/readline/readlines' size: None, or an int.
        private static long SizeArg(PyObject[] args)
        {
            var sizeobj = args.Length > 0 ? args[0] : PyNone.Instance;
            if (sizeobj is PyNone)
                return -1;
            if (sizeobj is not PyInt)
                throw PyTypeError.Create("arg 1 must be an integer");
            return PyArgs.Ssize(sizeobj);
        }

        private static PyType MakeReaderType()
        {
            var type = new PyType("MultibyteStreamReader", new[] { PyType.ObjectType }, "_multibytecodec");
            type.MarkSpecHeapType();
            // mbstreamreader_new: "O|s:StreamReader", stream and errors
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("StreamReader", args, kwargs, new[] { "stream", "errors" }, 0, 1, 2);
                var self = new StreamReader((PyType)cls) { Stream = parsed[0] };
                Init(self, cls, ErrorsKeyword("StreamReader", parsed[1]), false);
                return self;
            }, acceptsKwargs: true);
            InstallErrors(type);
            PyArgs.Define(type, "read", PyCall.Fast, (o, args) => StreamRead((StreamReader)o, "read", SizeArg(args)), 0, 1);
            PyArgs.Define(type, "readline", PyCall.Fast, (o, args) => StreamRead((StreamReader)o, "readline", SizeArg(args)), 0, 1);
            PyArgs.Define(type, "readlines", PyCall.Fast, (o, args) =>
            {
                var r = StreamRead((StreamReader)o, "read", SizeArg(args));
                return r.GetAttribute("splitlines").Call(new PyObject[] { PyBool.True }, null);
            }, 0, 1);
            PyArgs.Define(type, "reset", PyCall.NoArgs, (o, args) =>
            {
                var self = (StreamReader)o;
                if (self.Codec.HasDecReset && self.Codec.DecReset(self.State) != 0)
                    throw PySystemError.Create("error return without exception set");
                self.PendingSize = 0;
                return PyNone.Instance;
            });
            type.TypeDict["stream"] = new PyMemberDescriptor("stream", type, o => ((StreamReader)o).Stream, readOnly: true);
            return type;
        }

        // mbstreamwriter_iwrite
        private static void StreamWrite(StreamWriter self, PyObject unistr)
        {
            var str = EncodeStateful(self, unistr, false);
            self.Stream.GetAttribute("write").Call(new PyObject[] { PyBytes.From(str) }, null);
        }

        private static PyType MakeWriterType()
        {
            var type = new PyType("MultibyteStreamWriter", new[] { PyType.ObjectType }, "_multibytecodec");
            type.MarkSpecHeapType();
            type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, (cls, args, kwargs) =>
            {
                var parsed = PyArgs.Unpack("StreamWriter", args, kwargs, new[] { "stream", "errors" }, 0, 1, 2);
                var self = new StreamWriter((PyType)cls) { Stream = parsed[0] };
                Init(self, cls, ErrorsKeyword("StreamWriter", parsed[1]), true);
                return self;
            }, acceptsKwargs: true);
            InstallErrors(type);
            PyArgs.Define(type, "write", PyCall.Defining, (o, args) =>
            {
                StreamWrite((StreamWriter)o, args[0]);
                return PyNone.Instance;
            }, 1, 1);
            PyArgs.Define(type, "writelines", PyCall.Defining, (o, args) =>
            {
                var lines = args[0];
                if (!PyVM.IsSequence(lines))
                    throw PyTypeError.Create("arg must be a sequence object");
                for (long i = 0; i < lines.Size(); i++)
                    StreamWrite((StreamWriter)o, lines.GetItem(PyInt.From(i)));
                return PyNone.Instance;
            }, 1, 1);
            PyArgs.Define(type, "reset", PyCall.Defining, (o, args) =>
            {
                var self = (StreamWriter)o;
                if (self.Pending == null)
                    return PyNone.Instance;
                long ignored = 0;
                var pending = self.Pending;
                byte[] pwrt;
                try
                {
                    pwrt = Encode(self.Codec, self.State, pending, ref ignored, self.Errors, MbCodec.MBENC_FLUSH | MbCodec.MBENC_RESET);
                }
                finally
                {
                    self.Pending = null;
                }
                if (pwrt.Length > 0)
                    self.Stream.GetAttribute("write").Call(new PyObject[] { PyBytes.From(pwrt) }, null);
                return PyNone.Instance;
            }, 0, 0);
            type.TypeDict["stream"] = new PyMemberDescriptor("stream", type, o => ((StreamWriter)o).Stream, readOnly: true);
            return type;
        }
    }
}
