using System;
using System.Collections.Generic;
using System.Numerics;

namespace SharpPy.Modules
{
    /// <summary>
    /// _sre - CPython 3.12: Modules/_sre/sre.c. Lib/re parses and compiles patterns to opcodes; compile here keeps
    /// them, and SreEngine.cs runs them.
    /// </summary>
    public static class _SreModule
    {
        public static PyType PyPatternType { get; private set; }
        public static PyType PyMatchType { get; private set; }

        public static PyModule CreateSreModule()
        {
            if (PyPatternType == null)
            {
                PyPatternType = new PyType("Pattern", new[] { PyType.ObjectType }, "re");
                PyMatchType = new PyType("Match", new[] { PyType.ObjectType }, "re");
                foreach (var type in new[] { PyPatternType, PyMatchType })
                    type.TypeDict["__class_getitem__"] = new PyBuiltinClassMethod("__class_getitem__",
                        (cls, key) => new SharpPy.PyGenericAlias((PyType)cls, key));
            }

            var module = new PyModule("_sre", "<_sre C module>");
            module.ModuleDict["MAGIC"] = new PyInt(20221023);
            module.ModuleDict["CODESIZE"] = new PyInt(4);
            module.ModuleDict["MAXREPEAT"] = new PyInt(4294967295);
            module.ModuleDict["MAXGROUPS"] = new PyInt(1073741823);
            module.ModuleDict["compile"] = new PyBuiltinFunction("compile", Compile);
            module.ModuleDict["template"] = new PyBuiltinFunction("template", Template);
            module.ModuleDict["ascii_iscased"] = new PyBuiltinFunction("ascii_iscased", AsciiIscased);
            module.ModuleDict["unicode_iscased"] = new PyBuiltinFunction("unicode_iscased", UnicodeIscased);
            module.ModuleDict["ascii_tolower"] = new PyBuiltinFunction("ascii_tolower", AsciiTolower);
            module.ModuleDict["unicode_tolower"] = new PyBuiltinFunction("unicode_tolower", UnicodeTolower);
            return module;
        }

        /// <summary>_sre_compile_impl: pattern, flags, code, groups, groupindex, indexgroup.</summary>
        public static PyObject Compile(PyObject[] args)
        {
            if (args.Length != 6)
                throw PyTypeError.Create($"compile() takes exactly 6 arguments ({args.Length} given)");
            var items = ((PyList)args[2]).Items;
            var code = new uint[items.Length];
            for (int i = 0; i < code.Length; i++)
                code[i] = (uint)((items[i] is PyIntSubclass named ? named.GetIntValue().Value : ((PyInt)items[i]).Value) & uint.MaxValue);
            var indexGroup = args[5] is PyTuple tuple ? tuple.Items : ((PyList)args[5]).Items;
            return new PySrePattern(args[0], args[1].ToInt(), code, args[3].ToInt(), (PyDict)args[4], indexGroup);
        }

        /// <summary>_sre_template_impl: literal, then group and literal in turn.</summary>
        public static PyObject Template(PyObject[] args)
        {
            var items = args[1] is PyList list ? list.Items : ((PyTuple)args[1]).Items;
            if (items.Length == 0 || (items.Length - 1) % 2 != 0)
                throw PyTypeError.Create("invalid template");
            return new PySreTemplate((PySrePattern)args[0], items);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:377-385 (_sre_ascii_iscased_impl)
        /// Check if ASCII character has case (is alphabetic)
        /// </summary>
        public static PyObject AsciiIscased(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("ascii_iscased() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();

            // CPython: ch < 128 && Py_ISALPHA(ch)
            // Py_ISALPHA checks if character is alphabetic (A-Z, a-z)
            bool result = ch < 128 && char.IsLetter((char)ch);
            return PyBool.FromBool(result);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:393-401 (_sre_unicode_iscased_impl)
        /// Check if Unicode character has case
        /// </summary>
        public static PyObject UnicodeIscased(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("unicode_iscased() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();
            char c = (char)ch;

            // CPython: ch != sre_lower_unicode(ch) || ch != sre_upper_unicode(ch)
            // A character is cased if converting to lower/upper changes it
            bool result = c != char.ToLowerInvariant(c) || c != char.ToUpperInvariant(c);
            return PyBool.FromBool(result);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:409-416 (_sre_ascii_tolower_impl)
        /// Convert ASCII character to lowercase
        /// </summary>
        public static PyObject AsciiTolower(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("ascii_tolower() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();

            // CPython: sre_lower_ascii(character)
            // ASCII lowercase: if ch is 'A'-'Z', return 'a'-'z', otherwise return ch
            if (ch >= 'A' && ch <= 'Z')
                return new PyInt(ch + ('a' - 'A'));
            return new PyInt(ch);
        }

        /// <summary>
        /// CPython 3.12: Modules/_sre/sre.c:424-431 (_sre_unicode_tolower_impl)
        /// Convert Unicode character to lowercase
        /// </summary>
        public static PyObject UnicodeTolower(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("unicode_tolower() missing 1 required positional argument: 'character'");

            int ch = args[0].ToInt();

            // CPython: sre_lower_unicode(character)
            // Use C# char.ToLowerInvariant for Unicode case mapping
            char c = (char)ch;
            char lower = char.ToLowerInvariant(c);
            return new PyInt((int)lower);
        }
    }
}
