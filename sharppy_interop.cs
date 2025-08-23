using System;
using System.Linq;
using System.Collections.Generic;

namespace SharpPy
{
    public class PythonInterop
    {
        private Environment env;

        public PythonInterop(Environment environment)
        {
            env = environment;
        }

        // 간단한 함수 호출
        public PythonTypeObject Call(string functionName, params object[] args)
        {
            try
            {
                var func = env.GetVariable(functionName) as Function;
                if (func == null)
                    throw new Exception($"Function '{functionName}' not found");

                var pythonArgs = args.Select(ConvertToPython).ToList();
                return func.Call(pythonArgs);
            }
            catch (PythonException ex)
            {
                throw new PythonException("Python Error", $"{ex.Type}: {ex.Message} \n Line {ex.Line}, Column {ex.Column} \n {ex}");
            }
            catch (Exception ex)
            {
                throw new PythonException("RuntimeError", $"Internal error calling '{functionName}': {ex.Message}");
            }
        }

        // 키워드 인자와 함께 호출
        public PythonTypeObject CallWithKwargs(string functionName,
                                              object[] args = null,
                                              Dictionary<string, object> kwargs = null)
        {
            var func = env.GetVariable(functionName) as UserFunction;
            if (func == null)
                throw new Exception($"Function '{functionName}' not found");

            var pythonArgs = (args ?? new object[0]).Select(ConvertToPython).ToList();
            var pythonKwargs = new Dictionary<string, PythonTypeObject>();

            if (kwargs != null)
            {
                foreach (var kvp in kwargs)
                {
                    pythonKwargs[kvp.Key] = ConvertToPython(kvp.Value);
                }
            }

            return func.CallWithKeywords(pythonArgs, pythonKwargs);
        }

        // C# 타입을 Python 타입으로 변환
        private PythonTypeObject ConvertToPython(object value)
        {
            return value switch
            {
                null => PythonNone.Instance,
                int i => PythonInt.Create(i),
                double d => new PythonFloat(d),
                float f => new PythonFloat(f),
                string s => new PythonString(s),
                bool b => PythonBool.Create(b),
                PythonTypeObject p => p,
                _ => throw new ArgumentException($"Cannot convert {value.GetType()} to Python type")
            };
        }

        // Python 타입을 C# 타입으로 변환
        public T ConvertFromPython<T>(PythonTypeObject value)
        {
            if (value == null || value is PythonNone)
                return default(T);

            var targetType = typeof(T);

            if (targetType == typeof(int) && value is PythonInt pi)
                return (T)(object)pi.Value;

            if (targetType == typeof(double) && value is PythonFloat pf)
                return (T)(object)pf.Value;

            if (targetType == typeof(string) && value is PythonString ps)
                return (T)(object)ps.Value;

            if (targetType == typeof(bool) && value is PythonBool pb)
                return (T)(object)pb.Value;

            throw new InvalidCastException($"Cannot convert {value.Type} to {targetType}");
        }

        public List<PythonTypeObject> ArgsToPython(params object[] args)
        {
            return args.Select(ConvertToPython).ToList();
        }

        static public object[] ArgsFromPython(List<PythonTypeObject> args)
        {
            if (args == null)
                return new object[0];

            var result = new object[args.Count];

            for (int i = 0; i < args.Count; i++)
            {
                result[i] = ConvertFromPythonToObject(args[i]);
            }

            return result;
        }

        // Python 타입을 적절한 C# object로 변환하는 헬퍼 메서드
        static private object ConvertFromPythonToObject(PythonTypeObject value)
        {
            return value switch
            {
                PythonNone or null => null,
                PythonInt pi => pi.AsInt(),
                PythonFloat pf => pf.AsDouble(),
                PythonBool pb => pb.AsBool(),
                PythonString ps => ps.AsString(),
                PythonList pl => ConvertList(pl),
                PythonDict pd => ConvertDict(pd),
                PythonTuple pt => ConvertTuple(pt),
                _ => value  // 변환할 수 없는 타입은 그대로 반환
            };
        }

        static public List<object> ConvertList(PythonList list)
        {
            var result = new List<object>(list.Items.Count);
            foreach (var item in list.Items)
            {
                result.Add(ConvertFromPythonToObject(item));
            }
            return result;
        }

        static public object[] ConvertTuple(PythonTuple tuple)
        {
            var result = new object[tuple.Items.Count];
            for (int i = 0; i < tuple.Items.Count; i++)
            {
                result[i] = ConvertFromPythonToObject(tuple.Items[i]);
            }
            return result;
        }

        static public Dictionary<object, object> ConvertDict(PythonDict dict)
        {
            var result = new Dictionary<object, object>();
            foreach (var kvp in dict.Items)
            {
                var key = ConvertFromPythonToObject(kvp.Key);
                var value = ConvertFromPythonToObject(kvp.Value);
                result[key] = value;
            }
            return result;
        }

#if GODOT
        public static Godot.Variant ConvertToVariant(PythonTypeObject pyObj)
        {
            return pyObj switch
            {
                null or PythonNone => Godot.Variant.CreateFrom<string>(null),
                PythonInt pi => Godot.Variant.From(pi.AsInt()),
                PythonFloat pf => Godot.Variant.From(pf.AsDouble()),
                PythonBool pb => Godot.Variant.From(pb.AsBool()),
                PythonString ps => Godot.Variant.From(ps.AsString()),
                // PythonList pl => Godot.Variant.From(ConvertListToGodotArray(pl)),
                // PythonDict pd => Godot.Variant.From(ConvertDictToGodotDict(pd)),
                // PythonTuple pt => Godot.Variant.From(ConvertTupleToGodotArray(pt)),
                _ => Godot.Variant.From(pyObj.ToPythonString())
            };
        }

        public static Godot.Variant[] ConvertToVariantArray(List<PythonTypeObject> args)
        {
            if (args == null || args.Count == 0)
                return new Godot.Variant[0];

            return args.Select(arg => ConvertToVariant(arg)).ToArray();
        }

#endif
    }
}