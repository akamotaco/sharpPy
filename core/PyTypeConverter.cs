using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

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
                string str => new PyString(str),
                char c => new PyString(c.ToString()),
                
                // 컬렉션 타입들
                byte[] bytes => new PyList(bytes.Select(b => new PyInt(b)).Cast<PyObject>().ToList()),
                int[] ints => new PyList(ints.Select(i => new PyInt(i)).Cast<PyObject>().ToList()),
                long[] longs => new PyList(longs.Select(l => new PyInt((int)l)).Cast<PyObject>().ToList()),
                float[] floats => new PyList(floats.Select(f => new PyFloat(f)).Cast<PyObject>().ToList()),
                double[] doubles => new PyList(doubles.Select(d => new PyFloat(d)).Cast<PyObject>().ToList()),
                string[] strings => new PyList(strings.Select(s => new PyString(s)).Cast<PyObject>().ToList()),
                
                // 제네릭 컬렉션들
                IList<int> intList => new PyList(intList.Select(i => new PyInt(i)).Cast<PyObject>().ToList()),
                IList<long> longList => new PyList(longList.Select(l => new PyInt((int)l)).Cast<PyObject>().ToList()),
                IList<float> floatList => new PyList(floatList.Select(f => new PyFloat(f)).Cast<PyObject>().ToList()),
                IList<double> doubleList => new PyList(doubleList.Select(d => new PyFloat(d)).Cast<PyObject>().ToList()),
                IList<string> stringList => new PyList(stringList.Select(s => new PyString(s)).Cast<PyObject>().ToList()),
                
                // Dictionary
                IDictionary<string, object> dict => ConvertDictionary(dict),
                IDictionary<string, int> intDict => ConvertIntDictionary(intDict),
                IDictionary<string, string> strDict => ConvertStringDictionary(strDict),
                
                // 이미 PyObject인 경우
                PyObject pyObj => pyObj,
                
                // 일반적인 IEnumerable (마지막 시도)
                IEnumerable enumerable => ConvertEnumerable(enumerable),
                
                // 알 수 없는 타입은 문자열로 변환
                _ => new PyString(obj.ToString() ?? "")
            };
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
                pyDict[kvp.Key] = new PyString(kvp.Value);
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
                if (pyObj is PyString pyString) return pyString.Value;
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
                var genericArgs = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
                return $"{genericTypeName.Split('`')[0]}<{genericArgs}>";
            }
            
            return type.Name;
        }

        #endregion
    }
}