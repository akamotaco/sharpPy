using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// type_ready for the builtin exceptions: what each static type's struct (ExceptionTable.g.cs,
    /// read from the macros of Objects/exceptions.c) puts in its own dict. ValueError has its own
    /// __init__ and __new__ although they are BaseException's functions, and OSError.__str__ is
    /// OSError_str whatever the subtype, as BaseException.__str__ is BaseException_str.
    /// </summary>
    internal static partial class PyExceptionTable
    {
        internal static void Install()
        {
            var byName = new Dictionary<string, PyType>();
            Collect(PyType.BaseExceptionType, byName);
            var baseNew = (PyNewMethodDescriptor)PyType.BaseExceptionType.TypeDict["__new__"];

            var baseType = PyType.BaseExceptionType;
            baseType.TypeDict["__str__"] = new PyWrapperDescriptor("__str__", baseType, StrOf("BaseException"));
            // BaseException_getset: {"__dict__", PyObject_GenericGetDict, PyObject_GenericSetDict}. A
            // subclass instance keeps its own dict, which its generic getattr already answers.
            baseType.TypeDict["__dict__"] = SubclassInstanceSupport.GenericDictGetSet(baseType);

            foreach (var (name, init, @new, str) in Types)
            {
                if (!byName.TryGetValue(name, out var type))
                    continue;
                if (!type.TypeDict.ContainsKey("__init__"))
                    type.TypeDict["__init__"] = new PyWrapperDescriptor("__init__", type, InitOf(byName, init));
                if (@new != null && !type.TypeDict.ContainsKey("__new__"))
                {
                    var source = byName.TryGetValue(@new, out var owner) && owner.TypeDict.TryGetValue("__new__", out var found)
                        && found is PyNewMethodDescriptor declared ? declared : baseNew;
                    type.TypeDict["__new__"] = new PyNewMethodDescriptor("__new__", type, source._implementation)
                    {
                        TpNew = source.TpNew ?? source,
                    };
                }
                if (str != null)
                    type.TypeDict["__str__"] = new PyWrapperDescriptor("__str__", type, StrOf(str));
            }
        }

        // The static exception types, which are not classes.
        private static void Collect(PyType type, Dictionary<string, PyType> byName)
        {
            if (type is PyClass)
                return;
            byName.TryAdd(type.Name, type);
            foreach (var subclass in type.LiveSubclasses())
                Collect(subclass, byName);
        }

        // STORE_init: the struct's own __init__, which the types made from it share.
        private static Func<PyObject, PyObject[], PyDict, PyObject> InitOf(Dictionary<string, PyType> byName, string store)
        {
            if (byName.TryGetValue(store, out var owner) && owner.TypeDict.TryGetValue("__init__", out var found)
                && found is PyWrapperDescriptor wrapper)
                return wrapper._implementation;
            return ((PyWrapperDescriptor)PyType.BaseExceptionType.TypeDict["__init__"])._implementation;
        }

        // The struct behind self: the builtin object, or the layout of a subclass instance.
        private static PyBaseException Struct(PyObject self) =>
            PyExceptionSlots.LayoutOf(self) ?? PyExceptionSlots.ToTransport(self) as PyBaseException
            ?? throw PyTypeError.Create($"'{self.GetTypeName()}' object is not an exception");

        // X_str, called by name: tp_str of the type, not of the object.
        private static Func<PyObject, PyObject[], PyDict, PyObject> StrOf(string function) => function switch
        {
            "BaseException" => (self, args, kwargs) => PyBaseException.BaseExceptionStr(self),
            "KeyError" => (self, args, kwargs) => PyKeyError.KeyErrorStr(self),
            "ImportError" => (self, args, kwargs) => ((PyImportError)Struct(self)).ImportErrorStr(self),
            "OSError" => (self, args, kwargs) => ((PyOSError)Struct(self)).OSErrorStr(self),
            "SyntaxError" => (self, args, kwargs) => ((PySyntaxError)Struct(self)).SyntaxErrorStr(),
            "UnicodeEncodeError" => (self, args, kwargs) => ((PyUnicodeEncodeError)Struct(self)).UnicodeEncodeErrorStr(),
            "UnicodeDecodeError" => (self, args, kwargs) => ((PyUnicodeDecodeError)Struct(self)).UnicodeDecodeErrorStr(),
            "UnicodeTranslateError" => (self, args, kwargs) => ((PyUnicodeTranslateError)Struct(self)).UnicodeTranslateErrorStr(),
            "BaseExceptionGroup" => (self, args, kwargs) => PyBaseExceptionGroup.Of(self).GroupStr(),
            _ => throw new InvalidOperationException($"no {function}_str"),
        };
    }
}
