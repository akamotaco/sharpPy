using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace SharpPy.Core
{
    /// <summary>
    /// IronPython 스타일의 자동 타입 변환을 제공하는 클래스
    /// C# 타입과 PyObject 간의 양방향 변환을 담당
    /// </summary>
    public static class PyTypeConverter
    {
        #region C# to PyObject 변환 (ToPyObject)

        /// <summary>
        /// C# 객체를 PyObject로 변환
        /// </summary>
        public static PyObject ToPyObject(object obj)
        {
            if (obj == null)
                return PyNone.Instance;

            return obj switch
            {
                // 기본 타입들
                bool b => PyBool.FromBool(b),
                byte b => new PyInt(b),
                sbyte sb => new PyInt(sb),
                short s => new PyInt(s),
                ushort us => new PyInt(us),
                int i => new PyInt(i),
                uint ui => new PyInt((int)ui),
                long l => new PyInt((int)l),
                ulong ul => new PyInt((int)ul),
                float f => new PyFloat(f),
                double d => new PyFloat(d),
                decimal dec => new PyFloat((double)dec),
                string str => new PyStr(str),
                char c => new PyStr(c.ToString()),

                // Performance: Eliminated LINQ - Manual array/list conversion
                // 컬렉션 타입들
                byte[] bytes => ConvertByteArray(bytes),
                int[] ints => ConvertIntArray(ints),
                long[] longs => ConvertLongArray(longs),
                float[] floats => ConvertFloatArray(floats),
                double[] doubles => ConvertDoubleArray(doubles),
                string[] strings => ConvertStringArray(strings),

                // 제네릭 컬렉션들
                IList<int> intList => ConvertIntList(intList),
                IList<long> longList => ConvertLongList(longList),
                IList<float> floatList => ConvertFloatList(floatList),
                IList<double> doubleList => ConvertDoubleList(doubleList),
                IList<string> stringList => ConvertStringList(stringList),
                
                // Dictionary
                IDictionary<string, object> dict => ConvertDictionary(dict),
                IDictionary<string, int> intDict => ConvertIntDictionary(intDict),
                IDictionary<string, string> strDict => ConvertStringDictionary(strDict),
                
                // 이미 PyObject인 경우
                PyObject pyObj => pyObj,
                
                // 일반적인 IEnumerable (마지막 시도)
                IEnumerable enumerable => ConvertEnumerable(enumerable),
                
                // 알 수 없는 타입은 문자열로 변환
                _ => new PyStr(obj.ToString() ?? "")
            };
        }

        // Performance: Eliminated LINQ - Helper methods for array/list conversion
        private static PyList ConvertByteArray(byte[] bytes)
        {
            var items = new List<PyObject>(bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                items.Add(new PyInt(bytes[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertIntArray(int[] ints)
        {
            var items = new List<PyObject>(ints.Length);
            for (int i = 0; i < ints.Length; i++)
            {
                items.Add(new PyInt(ints[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertLongArray(long[] longs)
        {
            var items = new List<PyObject>(longs.Length);
            for (int i = 0; i < longs.Length; i++)
            {
                items.Add(new PyInt((int)longs[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertFloatArray(float[] floats)
        {
            var items = new List<PyObject>(floats.Length);
            for (int i = 0; i < floats.Length; i++)
            {
                items.Add(new PyFloat(floats[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertDoubleArray(double[] doubles)
        {
            var items = new List<PyObject>(doubles.Length);
            for (int i = 0; i < doubles.Length; i++)
            {
                items.Add(new PyFloat(doubles[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertStringArray(string[] strings)
        {
            var items = new List<PyObject>(strings.Length);
            for (int i = 0; i < strings.Length; i++)
            {
                items.Add(new PyStr(strings[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertIntList(IList<int> intList)
        {
            var items = new List<PyObject>(intList.Count);
            for (int i = 0; i < intList.Count; i++)
            {
                items.Add(new PyInt(intList[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertLongList(IList<long> longList)
        {
            var items = new List<PyObject>(longList.Count);
            for (int i = 0; i < longList.Count; i++)
            {
                items.Add(new PyInt((int)longList[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertFloatList(IList<float> floatList)
        {
            var items = new List<PyObject>(floatList.Count);
            for (int i = 0; i < floatList.Count; i++)
            {
                items.Add(new PyFloat(floatList[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertDoubleList(IList<double> doubleList)
        {
            var items = new List<PyObject>(doubleList.Count);
            for (int i = 0; i < doubleList.Count; i++)
            {
                items.Add(new PyFloat(doubleList[i]));
            }
            return new PyList(items);
        }

        private static PyList ConvertStringList(IList<string> stringList)
        {
            var items = new List<PyObject>(stringList.Count);
            for (int i = 0; i < stringList.Count; i++)
            {
                items.Add(new PyStr(stringList[i]));
            }
            return new PyList(items);
        }

        private static PyDict ConvertDictionary(IDictionary<string, object> dict)
        {
            var pyDict = new Dictionary<string, PyObject>();
            foreach (var kvp in dict)
            {
                pyDict[kvp.Key] = ToPyObject(kvp.Value);
            }
            return new PyDict(pyDict);
        }

        private static PyDict ConvertIntDictionary(IDictionary<string, int> dict)
        {
            var pyDict = new Dictionary<string, PyObject>();
            foreach (var kvp in dict)
            {
                pyDict[kvp.Key] = new PyInt(kvp.Value);
            }
            return new PyDict(pyDict);
        }

        private static PyDict ConvertStringDictionary(IDictionary<string, string> dict)
        {
            var pyDict = new Dictionary<string, PyObject>();
            foreach (var kvp in dict)
            {
                pyDict[kvp.Key] = new PyStr(kvp.Value);
            }
            return new PyDict(pyDict);
        }

        private static PyList ConvertEnumerable(IEnumerable enumerable)
        {
            var items = new List<PyObject>();
            foreach (var item in enumerable)
            {
                items.Add(ToPyObject(item));
            }
            return new PyList(items);
        }

        #endregion

        #region PyObject to C# 변환 (FromPyObject)

        /// <summary>
        /// PyObject를 지정된 C# 타입으로 변환
        /// </summary>
        public static T FromPyObject<T>(PyObject pyObj)
        {
            if (pyObj == null || pyObj == PyNone.Instance)
            {
                return default(T)!;
            }

            var targetType = typeof(T);
            
            // Nullable 타입 처리
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                var underlyingType = Nullable.GetUnderlyingType(targetType)!;
                var value = ConvertToType(pyObj, underlyingType);
                return (T)value!;
            }

            var result = ConvertToType(pyObj, targetType);
            return (T)result!;
        }

        private static object? ConvertToType(PyObject pyObj, Type targetType)
        {
            if (pyObj == null || pyObj == PyNone.Instance)
                return GetDefaultValue(targetType);

            // 기본 타입 변환
            if (targetType == typeof(bool))
                return pyObj.PyBoolValue();
            
            if (targetType == typeof(int))
            {
                if (pyObj is PyInt pyInt) return pyInt.Value;
                if (pyObj is PyFloat pyFloat) return (int)pyFloat.Value;
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to int");
            }
            
            if (targetType == typeof(long))
            {
                if (pyObj is PyInt pyInt) return (long)pyInt.Value;
                if (pyObj is PyFloat pyFloat) return (long)pyFloat.Value;
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to long");
            }
            
            if (targetType == typeof(float))
            {
                if (pyObj is PyFloat pyFloat) return (float)pyFloat.Value;
                if (pyObj is PyInt pyInt) return (float)pyInt.Value;
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to float");
            }
            
            if (targetType == typeof(double))
            {
                if (pyObj is PyFloat pyFloat) return pyFloat.Value;
                if (pyObj is PyInt pyInt) return (double)pyInt.Value;
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to double");
            }
            
            if (targetType == typeof(string))
            {
                if (pyObj is PyStr pyString) return pyString.Value;
                return pyObj.AsString();
            }

            // 배열 타입 변환
            if (targetType.IsArray)
            {
                return ConvertToArray(pyObj, targetType);
            }

            // List<T> 타입 변환
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
            {
                return ConvertToList(pyObj, targetType);
            }

            // Dictionary<string, T> 타입 변환
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var genericArgs = targetType.GetGenericArguments();
                if (genericArgs[0] == typeof(string))
                {
                    return ConvertToDictionary(pyObj, genericArgs[1]);
                }
            }

            // 마지막 시도: ToString()
            if (targetType == typeof(object))
                return pyObj;

            throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to {targetType.Name}");
        }

        private static object ConvertToArray(PyObject pyObj, Type arrayType)
        {
            if (pyObj is not PyList pyList)
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to array");

            var elementType = arrayType.GetElementType()!;
            var array = Array.CreateInstance(elementType, pyList.Length());
            
            for (int i = 0; i < pyList.Length(); i++)
            {
                var item = pyList.GetItem(new PyInt(i));
                var convertedItem = ConvertToType(item, elementType);
                array.SetValue(convertedItem, i);
            }
            
            return array;
        }

        private static object ConvertToList(PyObject pyObj, Type listType)
        {
            if (pyObj is not PyList pyList)
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to List");

            var elementType = listType.GetGenericArguments()[0];
            var listInstance = Activator.CreateInstance(listType)!;
            var addMethod = listType.GetMethod("Add")!;
            
            for (int i = 0; i < pyList.Length(); i++)
            {
                var item = pyList.GetItem(new PyInt(i));
                var convertedItem = ConvertToType(item, elementType);
                addMethod.Invoke(listInstance, new[] { convertedItem });
            }
            
            return listInstance;
        }

        private static object ConvertToDictionary(PyObject pyObj, Type valueType)
        {
            if (pyObj is not PyDict pyDict)
                throw PyTypeError.Create($"Cannot convert {pyObj.GetTypeName()} to Dictionary");

            var dictType = typeof(Dictionary<,>).MakeGenericType(typeof(string), valueType);
            var dictInstance = Activator.CreateInstance(dictType)!;
            var setMethod = dictType.GetMethod("set_Item")!;

            // PyDict의 키-값 쌍을 순회하면서 변환
            var dictData = (Dictionary<string, PyObject>)pyDict.GetType()
                .GetField("_dict", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(pyDict)!;

            foreach (var kvp in dictData)
            {
                var convertedValue = ConvertToType(kvp.Value, valueType);
                setMethod.Invoke(dictInstance, new object[] { kvp.Key, convertedValue! });
            }
            
            return dictInstance;
        }

        private static object? GetDefaultValue(Type type)
        {
            if (type.IsValueType)
                return Activator.CreateInstance(type);
            return null;
        }

        #endregion

        #region 유틸리티 메서드들

        /// <summary>
        /// 타입이 변환 가능한지 확인
        /// </summary>
        public static bool CanConvert<T>()
        {
            return CanConvert(typeof(T));
        }

        public static bool CanConvert(Type type)
        {
            // Nullable 타입 처리
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
            {
                type = Nullable.GetUnderlyingType(type)!;
            }

            // 지원되는 기본 타입들
            if (type.IsPrimitive || type == typeof(string) || type == typeof(decimal))
                return true;

            // 배열 타입
            if (type.IsArray && CanConvert(type.GetElementType()!))
                return true;

            // List<T> 타입
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                return CanConvert(type.GetGenericArguments()[0]);

            // Dictionary<string, T> 타입
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = type.GetGenericArguments();
                return args[0] == typeof(string) && CanConvert(args[1]);
            }

            return false;
        }

        /// <summary>
        /// 타입 이름을 사용자 친화적으로 반환
        /// </summary>
        public static string GetFriendlyTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                var genericTypeName = type.GetGenericTypeDefinition().Name;
                var genericArgs = type.GetGenericArguments();

                // Performance: Eliminated LINQ - Manual StringBuilder for type names
                var sb = new StringBuilder();
                for (int i = 0; i < genericArgs.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(GetFriendlyTypeName(genericArgs[i]));
                }

                return $"{genericTypeName.Split('`')[0]}<{sb}>";
            }

            return type.Name;
        }

        #endregion
    }
}