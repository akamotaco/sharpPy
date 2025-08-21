// sharppy_copy_module.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// Python copy module implementation
    /// </summary>
    public static class CopyModule
    {
        // 순환 참조 방지를 위한 메모 딕셔너리 - Reference equality 사용
        private class CopyMemo
        {
            // Reference equality를 사용하는 Dictionary
            public Dictionary<object, PythonTypeObject> Memo { get; } = 
                new Dictionary<object, PythonTypeObject>(new ReferenceEqualityComparer());
        }

        // Reference equality comparer
        private class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                // 객체의 실제 메모리 주소 기반 해시코드 사용
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        public static PythonModule CreateCopyModule(List<string> searchPaths = null)
        {
            var module = new PythonModule("copy", searchPaths);

            // copy.copy() - 얕은 복사
            module.SetAttribute("copy", new BuiltinFunction("copy", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "copy() takes exactly one argument");
                
                return ShallowCopy(args[0]);
            }));

            // copy.deepcopy() - 깊은 복사
            module.SetAttribute("deepcopy", new BuiltinFunction("deepcopy", args =>
            {
                if (args.Count < 1 || args.Count > 2)
                    throw new PythonException("TypeError", "deepcopy() takes 1 or 2 arguments");
                
                var memo = new CopyMemo();
                if (args.Count == 2 && args[1] is PythonDict memoDict)
                {
                    // 사용자가 제공한 memo dict 사용 (선택적)
                    // 여기서는 간단히 새 memo 사용
                }
                
                return DeepCopy(args[0], memo);
            }));

            // copy.error 예외 클래스
            module.SetAttribute("Error", new PythonString("copy.Error"));

            return module;
        }

        /// <summary>
        /// 얕은 복사 구현
        /// </summary>
        private static PythonTypeObject ShallowCopy(PythonTypeObject obj)
        {
            switch (obj)
            {
                // Immutable 타입들은 그대로 반환
                case PythonNone _:
                case PythonBool _:
                case PythonInt _:
                case PythonFloat _:
                case PythonString _:
                    return obj;

                // List 얕은 복사
                case PythonList list:
                    var newList = new PythonList(list.Items.Count);
                    newList.Items.AddRange(list.Items);  // 참조만 복사
                    return newList;

                // Tuple은 immutable이지만 내부 요소는 mutable일 수 있음
                case PythonTuple tuple:
                    var newTuple = new PythonTuple(tuple.Items.Count);
                    newTuple.Items.AddRange(tuple.Items);  // 참조만 복사
                    return newTuple;

                // Dict 얕은 복사
                case PythonDict dict:
                    var newDict = new PythonDict(dict.Items.Count);
                    foreach (var kvp in dict.Items)
                    {
                        newDict.Items[kvp.Key] = kvp.Value;  // 키와 값의 참조만 복사
                    }
                    return newDict;

                // Set 얕은 복사
                case PythonSet set:
                    var newSet = new PythonSet();
                    foreach (var item in set.Items)
                    {
                        newSet.Items.Add(item);  // 참조만 복사
                    }
                    return newSet;

                // 클래스 인스턴스 얕은 복사
                case PythonInstance instance:
                    var newInstance = new PythonInstance(instance.Class);
                    
                    // 인스턴스 변수들 복사
                    var instanceVars = instance.InstanceEnv.GetAllVariables();
                    foreach (var kvp in instanceVars)
                    {
                        if (instance.InstanceEnv.HasLocalVariable(kvp.Key))
                        {
                            newInstance.SetAttribute(kvp.Key, kvp.Value);  // 참조만 복사
                        }
                    }
                    
                    return newInstance;

                // Function과 Module은 그대로 반환
                case Function _:
                case PythonModule _:
                case PythonClass _:
                    return obj;

                default:
                    // 알 수 없는 타입은 그대로 반환
                    return obj;
            }
        }

        /// <summary>
        /// 깊은 복사 구현
        /// </summary>
        private static PythonTypeObject DeepCopy(PythonTypeObject obj, CopyMemo memo)
        {
            // 순환 참조 체크 - object reference를 키로 사용
            if (memo.Memo.TryGetValue(obj, out var cached))
            {
                return cached;
            }

            PythonTypeObject result;

            switch (obj)
            {
                // Immutable 타입들은 그대로 반환
                case PythonNone _:
                case PythonBool _:
                case PythonInt _:
                case PythonFloat _:
                case PythonString _:
                    return obj;

                // List 깊은 복사
                case PythonList list:
                    var newList = new PythonList(list.Items.Count);
                    memo.Memo[obj] = newList;  // 순환 참조 방지를 위해 먼저 등록
                    
                    foreach (var item in list.Items)
                    {
                        newList.Items.Add(DeepCopy(item, memo));  // 재귀적으로 복사
                    }
                    result = newList;
                    break;

                // Tuple 깊은 복사
                case PythonTuple tuple:
                    var newTuple = new PythonTuple(tuple.Items.Count);
                    memo.Memo[obj] = newTuple;
                    
                    foreach (var item in tuple.Items)
                    {
                        newTuple.Items.Add(DeepCopy(item, memo));  // 재귀적으로 복사
                    }
                    result = newTuple;
                    break;

                // Dict 깊은 복사
                case PythonDict dict:
                    var newDict = new PythonDict(dict.Items.Count);
                    memo.Memo[obj] = newDict;
                    
                    // Dictionary의 각 항목을 복사
                    var dictItems = new List<KeyValuePair<PythonTypeObject, PythonTypeObject>>(dict.Items);
                    foreach (var kvp in dictItems)
                    {
                        // 키는 일반적으로 immutable이어야 하지만, 값은 깊은 복사
                        var newKey = kvp.Key switch
                        {
                            PythonString _ => kvp.Key,  // String은 immutable
                            PythonInt _ => kvp.Key,     // Int는 immutable
                            PythonFloat _ => kvp.Key,   // Float는 immutable
                            PythonBool _ => kvp.Key,    // Bool은 immutable
                            PythonTuple t => DeepCopy(t, memo),  // Tuple도 복사 (내부에 mutable 있을 수 있음)
                            _ => kvp.Key  // 다른 타입은 그대로 (hashable이어야 함)
                        };
                        
                        var newValue = DeepCopy(kvp.Value, memo);
                        newDict.Items[newKey] = newValue;
                    }
                    result = newDict;
                    break;

                // Set 깊은 복사
                case PythonSet set:
                    var newSet = new PythonSet();
                    memo.Memo[obj] = newSet;
                    
                    // HashSet의 내용을 리스트로 복사한 후 처리
                    var setItems = new List<PythonTypeObject>(set.Items);
                    foreach (var item in setItems)
                    {
                        // Set의 요소는 hashable이어야 하므로 대부분 immutable
                        var newItem = item switch
                        {
                            PythonString _ => item,
                            PythonInt _ => item,
                            PythonFloat _ => item,
                            PythonBool _ => item,
                            PythonTuple t => DeepCopy(t, memo),
                            _ => item
                        };
                        newSet.Items.Add(newItem);
                    }
                    result = newSet;
                    break;

                // 클래스 인스턴스 깊은 복사
                case PythonInstance instance:
                    var newInstance = new PythonInstance(instance.Class);
                    memo.Memo[obj] = newInstance;
                    
                    // 인스턴스 변수들 깊은 복사
                    var instanceVars = instance.InstanceEnv.GetAllVariables();
                    foreach (var kvp in instanceVars)
                    {
                        if (instance.InstanceEnv.HasLocalVariable(kvp.Key))
                        {
                            // __deepcopy__ 메서드가 있는지 확인
                            try
                            {
                                var deepcopyMethod = instance.GetAttribute("__deepcopy__");
                                if (deepcopyMethod is Function func)
                                {
                                    // memo를 Python dict로 변환
                                    var memoDict = new PythonDict();
                                    return func.Call(new List<PythonTypeObject> { memoDict });
                                }
                            }
                            catch (PythonException) { }
                            
                            // 각 속성을 재귀적으로 복사
                            newInstance.SetAttribute(kvp.Key, DeepCopy(kvp.Value, memo));
                        }
                    }
                    
                    result = newInstance;
                    break;

                // Function, Module, Class는 그대로 반환 (복사하지 않음)
                case Function _:
                case PythonModule _:
                case PythonClass _:
                case CallableType _:  // CallableType도 복사하지 않음
                    return obj;

                default:
                    // 알 수 없는 타입은 그대로 반환
                    return obj;
            }

            return result;
        }
    }
}