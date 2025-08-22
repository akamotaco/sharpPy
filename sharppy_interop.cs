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
            var func = env.GetVariable(functionName) as Function;
            if (func == null)
                throw new Exception($"Function '{functionName}' not found");

            var pythonArgs = args.Select(ConvertToPython).ToList();
            return func.Call(pythonArgs);
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
    }
}