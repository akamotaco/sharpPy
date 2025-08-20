// enhanced_python_types.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace SharpPy
{
    // Optimized Python List
    public class PythonList : PythonTypeObject
    {
        public List<PythonTypeObject> Items { get; }
        private static Dictionary<string, Func<PythonList, List<PythonTypeObject>, PythonTypeObject>> methodRegistry;

        static PythonList()
        {
            InitializeMethodRegistry();
        }

        public PythonList()
        {
            Items = new List<PythonTypeObject>();
        }

        public PythonList(int capacity)
        {
            Items = new List<PythonTypeObject>(capacity);
        }

        public override PythonType Type => PythonType.List;
        public override bool IsTrue() => Items.Count > 0;
        public override bool IsSequence() => true;
        public override object GetRawValue() => this;

        public override string ToPythonString()
        {
            if (Items.Count == 0) return "[]";
            return "[" + string.Join(", ", Items.Select(FormatItem)) + "]";
        }

        public override bool Equals(PythonTypeObject other)
        {
            if (!(other is PythonList pl) || Items.Count != pl.Items.Count)
                return false;

            for (int i = 0; i < Items.Count; i++)
            {
                if (!Items[i].Equals(pl.Items[i]))
                    return false;
            }
            return true;
        }

        public override int GetHashCode() =>
            throw new PythonException("TypeError", "unhashable type: 'list'");

        private static void InitializeMethodRegistry()
        {
            methodRegistry = new Dictionary<string, Func<PythonList, List<PythonTypeObject>, PythonTypeObject>>
            {
                ["append"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "append() takes exactly one argument");
                    self.Items.Add(args[0]);
                    return PythonNone.Instance;
                },
                ["extend"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "extend() takes exactly one argument");
                    switch (args[0])
                    {
                        case PythonList other:
                            self.Items.AddRange(other.Items);
                            break;
                        case PythonTuple tuple:
                            self.Items.AddRange(tuple.Items);
                            break;
                        case PythonString str:
                            foreach (char c in str.Value)
                                self.Items.Add(new PythonString(c.ToString()));
                            break;
                        default:
                            throw new PythonException("TypeError", "extend() argument must be iterable");
                    }
                    return PythonNone.Instance;
                },
                ["insert"] = (self, args) =>
                {
                    if (args.Count != 2) throw new PythonException("TypeError", "insert() takes exactly two arguments");
                    if (!NumberHelper.IsNumber(args[0]))
                        throw new PythonException("TypeError", "insert() first argument must be an integer");

                    int i = NumberHelper.ToInt(args[0]);
                    if (i < 0) i = Math.Max(0, self.Items.Count + i);
                    if (i > self.Items.Count) i = self.Items.Count;
                    self.Items.Insert(i, args[1]);
                    return PythonNone.Instance;
                },
                ["remove"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "remove() takes exactly one argument");
                    for (int i = 0; i < self.Items.Count; i++)
                    {
                        if (self.Items[i].Equals(args[0]))
                        {
                            self.Items.RemoveAt(i);
                            return PythonNone.Instance;
                        }
                    }
                    throw new PythonException("ValueError", "list.remove(x): x not in list");
                },
                ["pop"] = (self, args) =>
                {
                    if (args.Count > 1) throw new PythonException("TypeError", "pop() takes at most 1 argument");
                    if (self.Items.Count == 0) throw new PythonException("IndexError", "pop from empty list");

                    int index = args.Count == 0 ? self.Items.Count - 1 : NumberHelper.ToInt(args[0]);
                    if (index < 0) index += self.Items.Count;
                    if (index < 0 || index >= self.Items.Count)
                        throw new PythonException("IndexError", "pop index out of range");

                    var item = self.Items[index];
                    self.Items.RemoveAt(index);
                    return item;
                },
                ["clear"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "clear() takes no arguments");
                    self.Items.Clear();
                    return PythonNone.Instance;
                },
                ["index"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "index() takes exactly one argument");
                    for (int i = 0; i < self.Items.Count; i++)
                    {
                        if (self.Items[i].Equals(args[0]))
                            return PythonInt.Create(i);
                    }
                    throw new PythonException("ValueError", $"{args[0]} is not in list");
                },
                ["count"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    return PythonInt.Create(self.Items.Count(item => item.Equals(args[0])));
                },
                ["sort"] = (self, args) =>
                {
                    if (args.Count > 1) throw new PythonException("TypeError", "sort() takes at most 1 argument");
                    self.Items.Sort((a, b) =>
                    {
                        if (NumberHelper.IsNumber(a) && NumberHelper.IsNumber(b))
                            return NumberHelper.ToDouble(a).CompareTo(NumberHelper.ToDouble(b));
                        if (a is PythonString sa && b is PythonString sb)
                            return string.Compare(sa.Value, sb.Value);
                        return 0;
                    });
                    return PythonNone.Instance;
                },
                ["reverse"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "reverse() takes no arguments");
                    self.Items.Reverse();
                    return PythonNone.Instance;
                },
                ["copy"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "copy() takes no arguments");
                    var newList = new PythonList(self.Items.Count);
                    newList.Items.AddRange(self.Items);
                    return newList;
                }
            };
        }

        public override BuiltinFunction GetMethod(string name)
        {
            if (methodRegistry.TryGetValue(name, out var method))
            {
                return new BuiltinFunction(name, args => method(this, args));
            }
            throw new PythonException("AttributeError", $"'list' object has no attribute '{name}'");
        }

        public override List<string> GetMethodNames()
        {
            return methodRegistry.Keys.OrderBy(k => k).ToList();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string FormatItem(PythonTypeObject item) => item switch
        {
            PythonString s => $"'{s.Value}'",
            PythonNone => "None",
            _ => item.ToPythonString()
        };

        public PythonList Repeat(int times)
        {
            if (times <= 0) return new PythonList();
            var result = new PythonList(Items.Count * times);
            for (int i = 0; i < times; i++)
                result.Items.AddRange(Items);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PythonTypeObject GetItem(int index)
        {
            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "list index out of range");
            return Items[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetItem(int index, PythonTypeObject value)
        {
            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "list assignment index out of range");
            Items[index] = value;
        }
    }

    // Optimized Python Tuple
    public sealed class PythonTuple : PythonTypeObject
    {
        private static readonly PythonTuple EmptyTuple = new PythonTuple();
        private static Dictionary<string, Func<PythonTuple, List<PythonTypeObject>, PythonTypeObject>> methodRegistry;

        public List<PythonTypeObject> Items { get; }

        static PythonTuple()
        {
            InitializeMethodRegistry();
        }

        private static void InitializeMethodRegistry()
        {
            methodRegistry = new Dictionary<string, Func<PythonTuple, List<PythonTypeObject>, PythonTypeObject>>
            {
                ["count"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "count() takes exactly one argument");
                    return PythonInt.Create(self.Items.Count(item => item.Equals(args[0])));
                },
                ["index"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "index() takes exactly one argument");
                    for (int i = 0; i < self.Items.Count; i++)
                    {
                        if (self.Items[i].Equals(args[0]))
                            return PythonInt.Create(i);
                    }
                    throw new PythonException("ValueError", $"{args[0]} is not in tuple");
                }
            };
        }

        public PythonTuple()
        {
            Items = new List<PythonTypeObject>();
        }

        public PythonTuple(int capacity)
        {
            Items = new List<PythonTypeObject>(capacity);
        }

        public static PythonTuple Empty => EmptyTuple;

        public override PythonType Type => PythonType.Tuple;
        public override bool IsTrue() => Items.Count > 0;
        public override bool IsSequence() => true;
        public override object GetRawValue() => this;

        public override string ToPythonString()
        {
            if (Items.Count == 0) return "()";
            if (Items.Count == 1) return $"({FormatItem(Items[0])},)";
            return "(" + string.Join(", ", Items.Select(FormatItem)) + ")";
        }

        public override bool Equals(PythonTypeObject other)
        {
            if (!(other is PythonTuple pt) || Items.Count != pt.Items.Count)
                return false;

            for (int i = 0; i < Items.Count; i++)
            {
                if (!Items[i].Equals(pt.Items[i]))
                    return false;
            }
            return true;
        }

        public override int GetHashCode()
        {
            int hash = 17;
            foreach (var item in Items)
                hash = hash * 31 + item.GetHashCode();
            return hash;
        }

        public override BuiltinFunction GetMethod(string name)
        {
            if (methodRegistry.TryGetValue(name, out var method))
            {
                return new BuiltinFunction(name, args => method(this, args));
            }
            throw new PythonException("AttributeError", $"'tuple' object has no attribute '{name}'");
        }

        public override List<string> GetMethodNames()
        {
            return methodRegistry.Keys.OrderBy(k => k).ToList();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string FormatItem(PythonTypeObject item) => item switch
        {
            PythonString s => $"'{s.Value}'",
            PythonNone => "None",
            _ => item.ToPythonString()
        };

        public PythonTuple Repeat(int times)
        {
            if (times <= 0) return EmptyTuple;
            var result = new PythonTuple(Items.Count * times);
            for (int i = 0; i < times; i++)
                result.Items.AddRange(Items);
            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PythonTypeObject GetItem(int index)
        {
            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "tuple index out of range");
            return Items[index];
        }
    }

    // Optimized Python Dictionary
    public sealed class PythonDict : PythonTypeObject
    {
        public Dictionary<PythonTypeObject, PythonTypeObject> Items { get; }
        private static Dictionary<string, Func<PythonDict, List<PythonTypeObject>, PythonTypeObject>> methodRegistry;

        static PythonDict()
        {
            InitializeMethodRegistry();
        }

        public PythonDict()
        {
            Items = new Dictionary<PythonTypeObject, PythonTypeObject>();
        }

        public PythonDict(int capacity)
        {
            Items = new Dictionary<PythonTypeObject, PythonTypeObject>(capacity);
        }

        public override PythonType Type => PythonType.Dict;
        public override bool IsTrue() => Items.Count > 0;
        public override object GetRawValue() => this;

        public override string ToPythonString()
        {
            if (Items.Count == 0) return "{}";
            var pairs = Items.Select(kvp => $"{FormatItem(kvp.Key)}: {FormatItem(kvp.Value)}");
            return "{" + string.Join(", ", pairs) + "}";
        }

        public override bool Equals(PythonTypeObject other)
        {
            if (!(other is PythonDict pd) || Items.Count != pd.Items.Count)
                return false;

            foreach (var kvp in Items)
            {
                bool found = false;
                foreach (var pdKvp in pd.Items)
                {
                    if (pdKvp.Key.Equals(kvp.Key))
                    {
                        if (!pdKvp.Value.Equals(kvp.Value))
                            return false;
                        found = true;
                        break;
                    }
                }
                if (!found) return false;
            }
            return true;
        }

        public override int GetHashCode() =>
            throw new PythonException("TypeError", "unhashable type: 'dict'");


        private static void InitializeMethodRegistry()
        {
            methodRegistry = new Dictionary<string, Func<PythonDict, List<PythonTypeObject>, PythonTypeObject>>
            {
                ["get"] = (self, args) =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new PythonException("TypeError", "get() takes 1 or 2 arguments");
                    var key = args[0];
                    var defaultValue = args.Count == 2 ? args[1] : PythonNone.Instance;

                    foreach (var kvp in self.Items)
                    {
                        if (kvp.Key.Equals(key))
                            return kvp.Value;
                    }
                    return defaultValue;
                },
                ["keys"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "keys() takes no arguments");
                    var list = new PythonList(self.Items.Count);
                    list.Items.AddRange(self.Items.Keys);
                    return list;
                },
                ["values"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "values() takes no arguments");
                    var list = new PythonList(self.Items.Count);
                    list.Items.AddRange(self.Items.Values);
                    return list;
                },
                ["items"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "items() takes no arguments");
                    var list = new PythonList(self.Items.Count);
                    foreach (var kvp in self.Items)
                    {
                        var tuple = new PythonTuple(2);
                        tuple.Items.Add(kvp.Key);
                        tuple.Items.Add(kvp.Value);
                        list.Items.Add(tuple);
                    }
                    return list;
                },
                ["pop"] = (self, args) =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new PythonException("TypeError", "pop() takes 1 or 2 arguments");
                    var key = args[0];
                    foreach (var kvp in self.Items)
                    {
                        if (kvp.Key.Equals(key))
                        {
                            self.Items.Remove(kvp.Key);
                            return kvp.Value;
                        }
                    }
                    if (args.Count == 2) return args[1];
                    throw new PythonException("KeyError", $"KeyError: {key}");
                },
                ["clear"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "clear() takes no arguments");
                    self.Items.Clear();
                    return PythonNone.Instance;
                },
                ["update"] = (self, args) =>
                {
                    if (args.Count != 1) throw new PythonException("TypeError", "update() takes exactly one argument");
                    if (!(args[0] is PythonDict other))
                        throw new PythonException("TypeError", "update() argument must be a dict");
                    foreach (var kvp in other.Items)
                        self.Items[kvp.Key] = kvp.Value;
                    return PythonNone.Instance;
                },
                ["copy"] = (self, args) =>
                {
                    if (args.Count != 0) throw new PythonException("TypeError", "copy() takes no arguments");
                    var newDict = new PythonDict(self.Items.Count);
                    foreach (var kvp in self.Items)
                        newDict.Items[kvp.Key] = kvp.Value;
                    return newDict;
                },
                ["setdefault"] = (self, args) =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new PythonException("TypeError", "setdefault() takes 1 or 2 arguments");
                    var key = args[0];
                    var defaultValue = args.Count == 2 ? args[1] : PythonNone.Instance;

                    foreach (var kvp in self.Items)
                    {
                        if (kvp.Key.Equals(key))
                            return kvp.Value;
                    }
                    self.Items[key] = defaultValue;
                    return defaultValue;
                }
            };
        }

        public override BuiltinFunction GetMethod(string name)
        {
            if (methodRegistry.TryGetValue(name, out var method))
            {
                return new BuiltinFunction(name, args => method(this, args));
            }
            throw new PythonException("AttributeError", $"'dict' object has no attribute '{name}'");
        }

        public override List<string> GetMethodNames()
        {
            return methodRegistry.Keys.OrderBy(k => k).ToList();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string FormatItem(PythonTypeObject item) => item switch
        {
            PythonString s => $"'{s.Value}'",
            PythonNone => "None",
            _ => item.ToPythonString()
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PythonTypeObject GetItem(PythonTypeObject key)
        {
            foreach (var kvp in Items)
            {
                if (kvp.Key.Equals(key))
                    return kvp.Value;
            }
            throw new PythonException("KeyError", $"KeyError: {key}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetItem(PythonTypeObject key, PythonTypeObject value)
        {
            Items[key] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ContainsKey(PythonTypeObject key) => Items.ContainsKey(key);
    }

    // Optimized Function Classes
    public abstract class Function : PythonTypeObject
    {
        public string Name { get; }
        protected Function(string name) => Name = name;

        public abstract PythonTypeObject Call(List<PythonTypeObject> arguments);

        public override PythonType Type => PythonType.Function;
        public override bool IsTrue() => true;
        public override bool IsCallable() => true;
        public override string ToPythonString() => $"<function {Name}>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        // GetAttribute 추가
        public override PythonTypeObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__name__":
                    return new PythonString(Name);
                case "__doc__":
                    return PythonNone.Instance;  // 향후 docstring 지원
                case "__module__":
                    return new PythonString("__main__");  // 기본값
                default:
                    throw new PythonException("AttributeError",
                        $"'function' object has no attribute '{name}'");
            }
        }
    }

    // sharppy_python_types.cs의 UserFunction 클래스 수정
    public sealed class UserFunction : Function
    {
        public List<Parameter> Parameters { get; }
        public List<ASTNode> Body { get; }
        public Environment ClosureEnv { get; }
        public TypeHint ReturnTypeHint { get; }

        private List<PythonTypeObject> evaluatedDefaults;

        public UserFunction(string name, List<Parameter> parameters, List<ASTNode> body,
                        Environment closureEnv, TypeHint returnTypeHint = null)
            : base(name)
        {
            Parameters = parameters;
            Body = body;
            ClosureEnv = closureEnv;
            ReturnTypeHint = returnTypeHint;

            evaluatedDefaults = new List<PythonTypeObject>();
            foreach (var param in parameters)
            {
                if (param.DefaultValue != null)
                {
                    evaluatedDefaults.Add(param.DefaultValue.Evaluate(closureEnv));
                }
                else
                {
                    evaluatedDefaults.Add(null);
                }
            }
        }

        public PythonTypeObject CallWithKeywords(List<PythonTypeObject> positionalArgs,
                                            Dictionary<string, PythonTypeObject> keywordArgs)
        {
            var funcEnv = new Environment(ClosureEnv, ClosureEnv.globalEnv, EnvironmentType.Enclosing);

            // 함수가 정의된 환경의 파일 정보 상속
            funcEnv.CurrentFileName = ClosureEnv.CurrentFileName;

            // 파라미터 분류
            var normalParams = Parameters.Where(p => p.Kind == ParameterKind.Normal).ToList();
            var varArgsParam = Parameters.FirstOrDefault(p => p.Kind == ParameterKind.VarArgs);
            var kwArgsParam = Parameters.FirstOrDefault(p => p.Kind == ParameterKind.KwArgs);

            // 사용된 키워드 인자 추적
            var usedKeywords = new HashSet<string>();

            // 1. 위치 인자를 일반 매개변수에 할당
            int posArgIndex = 0;
            for (int i = 0; i < normalParams.Count && posArgIndex < positionalArgs.Count; i++)
            {
                var param = normalParams[i];

                // 키워드로 이미 제공된 경우 건너뛰기
                if (keywordArgs.ContainsKey(param.Name))
                    continue;

                funcEnv.SetVariable(param.Name, positionalArgs[posArgIndex]);
                posArgIndex++;
            }

            // 2. 키워드 인자 처리
            foreach (var kvp in keywordArgs)
            {
                var param = normalParams.FirstOrDefault(p => p.Name == kvp.Key);

                if (param != null)
                {
                    // 이미 위치 인자로 할당된 경우 에러
                    if (funcEnv.HasLocalVariable(param.Name))
                    {
                        throw new PythonException("TypeError",
                            $"{Name}() got multiple values for argument '{param.Name}'");
                    }

                    funcEnv.SetVariable(param.Name, kvp.Value);
                    usedKeywords.Add(kvp.Key);
                }
                else if (kwArgsParam == null)
                {
                    throw new PythonException("TypeError",
                        $"{Name}() got an unexpected keyword argument '{kvp.Key}'");
                }
            }

            // 3. 기본값 처리
            for (int i = 0; i < normalParams.Count; i++)
            {
                var param = normalParams[i];

                if (!funcEnv.HasLocalVariable(param.Name))
                {
                    if (param.HasDefault && i < evaluatedDefaults.Count && evaluatedDefaults[i] != null)
                    {
                        funcEnv.SetVariable(param.Name, evaluatedDefaults[i]);
                    }
                    else
                    {
                        throw new PythonException("TypeError",
                            $"{Name}() missing required positional argument: '{param.Name}'");
                    }
                }
            }

            // 4. *args 처리
            if (varArgsParam != null)
            {
                var extraArgs = new PythonTuple();
                for (int i = posArgIndex; i < positionalArgs.Count; i++)
                {
                    extraArgs.Items.Add(positionalArgs[i]);
                }
                funcEnv.SetVariable(varArgsParam.Name, extraArgs);
            }
            else if (posArgIndex < positionalArgs.Count)
            {
                throw new PythonException("TypeError",
                    $"{Name}() takes {normalParams.Count} positional arguments but {positionalArgs.Count} were given");
            }

            // 5. **kwargs 처리
            if (kwArgsParam != null)
            {
                var extraKwargs = new PythonDict();
                foreach (var kvp in keywordArgs)
                {
                    if (!usedKeywords.Contains(kvp.Key))
                    {
                        extraKwargs.Items[new PythonString(kvp.Key)] = kvp.Value;
                    }
                }
                funcEnv.SetVariable(kwArgsParam.Name, extraKwargs);
            }

            // 함수 본문 실행
            try
            {
                PythonTypeObject result = PythonNone.Instance;
                foreach (var stmt in Body)
                    result = stmt.Evaluate(funcEnv);

                return result;
            }
            catch (ReturnException ex)
            {
                return ex.Value;
            }
        }

        // 기존 Call 메서드는 키워드 인자 없이 호출용
        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            return CallWithKeywords(arguments, new Dictionary<string, PythonTypeObject>());
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetValueType(PythonTypeObject value)
        {
            return value switch
            {
                PythonNone => "None",
                PythonInstance instance => instance.Class.Name,
                PythonClass cls => $"Type[{cls.Name}]",
                _ => value.Type.ToString().ToLower()
            };
        }


        public override PythonTypeObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__name__":
                    return new PythonString(Name);
                case "__doc__":
                    // 향후 docstring 지원 시 구현
                    return PythonNone.Instance;
                default:
                    throw new PythonException("AttributeError",
                        $"'function' object has no attribute '{name}'");
            }
        }
    }

    public sealed class LambdaFunction : Function
    {
        public List<Parameter> Parameters { get; }
        public ASTNode Body { get; }
        public Environment ClosureEnv { get; }
        private List<PythonTypeObject> evaluatedDefaults;

        public LambdaFunction(List<Parameter> parameters, ASTNode body, Environment closureEnv)
            : base("<lambda>")
        {
            Parameters = parameters;
            Body = body;
            ClosureEnv = closureEnv;

            // 기본값 평가
            evaluatedDefaults = new List<PythonTypeObject>();
            foreach (var param in parameters)
            {
                if (param.DefaultValue != null)
                {
                    evaluatedDefaults.Add(param.DefaultValue.Evaluate(closureEnv));
                }
                else
                {
                    evaluatedDefaults.Add(null);
                }
            }
        }

        public PythonTypeObject CallWithKeywords(List<PythonTypeObject> positionalArgs,
                                            Dictionary<string, PythonTypeObject> keywordArgs)
        {
            // UserFunction과 유사한 로직으로 구현
            // 단, 람다는 보통 *args, **kwargs를 지원하지 않으므로 간단하게 구현
            var funcEnv = new Environment(ClosureEnv, ClosureEnv.globalEnv, EnvironmentType.Enclosing);

            int posArgIndex = 0;
            for (int i = 0; i < Parameters.Count && posArgIndex < positionalArgs.Count; i++)
            {
                var param = Parameters[i];
                if (keywordArgs.ContainsKey(param.Name))
                    continue;
                funcEnv.SetVariable(param.Name, positionalArgs[posArgIndex]);
                posArgIndex++;
            }

            foreach (var kvp in keywordArgs)
            {
                var param = Parameters.FirstOrDefault(p => p.Name == kvp.Key);
                if (param != null)
                {
                    if (funcEnv.HasLocalVariable(param.Name))
                        throw new PythonException("TypeError",
                            $"Lambda got multiple values for argument '{param.Name}'");
                    funcEnv.SetVariable(param.Name, kvp.Value);
                }
                else
                {
                    throw new PythonException("TypeError",
                        $"Lambda got an unexpected keyword argument '{kvp.Key}'");
                }
            }

            // 기본값 처리
            for (int i = 0; i < Parameters.Count; i++)
            {
                var param = Parameters[i];
                if (!funcEnv.HasLocalVariable(param.Name))
                {
                    if (param.HasDefault && i < evaluatedDefaults.Count && evaluatedDefaults[i] != null)
                    {
                        funcEnv.SetVariable(param.Name, evaluatedDefaults[i]);
                    }
                    else
                    {
                        throw new PythonException("TypeError",
                            $"Lambda missing required argument: '{param.Name}'");
                    }
                }
            }

            return Body.Evaluate(funcEnv);
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            return CallWithKeywords(arguments, new Dictionary<string, PythonTypeObject>());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetValueType(PythonTypeObject value) =>
            value == null || value is PythonNone ? "None" : value.Type.ToString().ToLower();
    }

    public sealed class BuiltinFunction : Function
    {
        private readonly Func<List<PythonTypeObject>, PythonTypeObject> implementation;
        private readonly Func<Environment, List<PythonTypeObject>, PythonTypeObject> envImplementation;

        public bool NeedsEnvironment { get; }

        public BuiltinFunction(string name, Func<List<PythonTypeObject>, PythonTypeObject> impl) : base(name)
        {
            implementation = impl;
            NeedsEnvironment = false;
        }

        // 환경이 필요한 내장 함수용 생성자 추가
        public BuiltinFunction(string name, Func<Environment, List<PythonTypeObject>, PythonTypeObject> impl) : base(name)
        {
            envImplementation = impl;
            NeedsEnvironment = true;
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            if (NeedsEnvironment)
                throw new PythonException("RuntimeError", $"Function {Name} requires environment context");
            return implementation(arguments);
        }

        // 환경과 함께 호출하는 메서드
        public PythonTypeObject CallWithEnv(Environment env, List<PythonTypeObject> arguments)
        {
            if (NeedsEnvironment)
                return envImplementation(env, arguments);
            return implementation(arguments);
        }
    }

    // Optimized Bound Method
    public sealed class BoundMethod : Function
    {
        private readonly UserFunction method;
        private readonly PythonTypeObject instance;

        public BoundMethod(string name, UserFunction method, PythonTypeObject instance) : base(name)
        {
            this.method = method;
            this.instance = instance;
        }

        public PythonTypeObject CallWithKeywords(List<PythonTypeObject> positionalArgs,
                                                Dictionary<string, PythonTypeObject> keywordArgs)
        {
            var newArgs = new List<PythonTypeObject>(positionalArgs.Count + 1) { instance };
            newArgs.AddRange(positionalArgs);
            return method.CallWithKeywords(newArgs, keywordArgs);
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            return CallWithKeywords(arguments, new Dictionary<string, PythonTypeObject>());
        }
    }

    // Optimized Class System
    public sealed class PythonClass : PythonTypeObject
    {
        public string Name { get; }
        public Environment ClassEnv { get; }
        public PythonClass ParentClass { get; }

        public PythonClass(string name, Environment classEnv, PythonClass parentClass = null)
        {
            Name = name;
            ClassEnv = classEnv;
            ParentClass = parentClass;
        }

        public override PythonType Type => PythonType.Class;
        public override bool IsTrue() => true;
        public override bool IsCallable() => true;
        public override string ToPythonString() => $"<class '{Name}'>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        public PythonTypeObject GetClassAttribute(string name)
        {
            // 클래스 자체의 속성만 확인 (전역 환경 제외)
            PythonClass currentClass = this;
            while (currentClass != null)
            {
                if (currentClass.ClassEnv.HasLocalVariable(name))
                {
                    return currentClass.ClassEnv.GetLocalVariable(name);
                }
                currentClass = currentClass.ParentClass;
            }

            throw new PythonException("AttributeError", $"type object '{Name}' has no attribute '{name}'");
        }

        public PythonInstance CreateInstance(List<PythonTypeObject> args)
        {
            return CreateInstanceWithKeywords(args, new Dictionary<string, PythonTypeObject>());
        }

        public PythonInstance CreateInstanceWithKeywords(List<PythonTypeObject> positionalArgs,
                                                     Dictionary<string, PythonTypeObject> keywordArgs)
        {
            var instance = new PythonInstance(this);

            // __init__ 메서드 찾기
            if (ClassEnv.HasVariable("__init__"))
            {
                var initMethod = ClassEnv.GetVariable("__init__");

                // self를 첫 번째 인자로 추가
                var argsWithSelf = new List<PythonTypeObject> { instance };
                argsWithSelf.AddRange(positionalArgs);

                // __init__ 호출 시 keyword arguments도 전달
                switch (initMethod)
                {
                    case UserFunction userFunc:
                        userFunc.CallWithKeywords(argsWithSelf, keywordArgs);
                        break;

                    case BytecodeFunctionWithDefaults bytecodeFunc:
                        bytecodeFunc.CallWithKeywords(argsWithSelf, keywordArgs);
                        break;

                    case Function func:
                        // 기본 Function 타입은 keyword를 지원하지 않으면 positional만 사용
                        if (keywordArgs.Count > 0)
                        {
                            throw new PythonException("TypeError",
                                $"__init__() got unexpected keyword arguments");
                        }
                        func.Call(argsWithSelf);
                        break;

                    default:
                        throw new PythonException("TypeError",
                            "__init__ must be a callable");
                }
            }
            else if (positionalArgs.Count > 0 || keywordArgs.Count > 0)
            {
                // __init__이 없는데 인자가 전달된 경우
                throw new PythonException("TypeError",
                    $"{Name}() takes no arguments");
            }

            return instance;
        }

        public override PythonTypeObject GetAttribute(string name)
        {
            switch (name)
            {
                case "__name__":
                    return new PythonString(Name);
                case "__bases__":
                    // 부모 클래스 튜플 반환
                    var bases = new PythonTuple();
                    if (ParentClass != null)
                        bases.Items.Add(ParentClass);
                    return bases;
                case "__dict__":
                    var dict = new PythonDict();
                    foreach (var kvp in ClassEnv.variables)
                    {
                        dict.Items[new PythonString(kvp.Key)] = kvp.Value;
                    }
                    return dict;
                default:
                    return GetClassAttribute(name);
            }
        }
    }

    // PythonInstance with GetMethodNames
    public sealed class PythonInstance : PythonTypeObject
    {
        public PythonClass Class { get; }
        public Environment InstanceEnv { get; }

        public PythonInstance(PythonClass pythonClass)
        {
            Class = pythonClass;
            InstanceEnv = new Environment(pythonClass.ClassEnv);
        }

        public override PythonType Type => PythonType.Instance;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<{Class.Name} object>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        public override List<string> GetMethodNames()
        {
            var methods = new HashSet<string>();

            // 인스턴스 변수들
            foreach (var key in InstanceEnv.variables.Keys)
            {
                methods.Add(key);
            }

            // 클래스와 부모 클래스들의 메서드
            PythonClass currentClass = Class;
            while (currentClass != null)
            {
                foreach (var kvp in currentClass.ClassEnv.variables)
                {
                    if (!methods.Contains(kvp.Key))
                        methods.Add(kvp.Key);
                }
                currentClass = currentClass.ParentClass;
            }

            return methods.OrderBy(m => m).ToList();
        }

        public override PythonTypeObject GetAttribute(string name)
        {
            // 1. 먼저 인스턴스 변수 확인 (부모 환경 탐색 없이)
            if (InstanceEnv.HasLocalVariable(name))
            {
                var value = InstanceEnv.GetLocalVariable(name);
                return value is UserFunction userFunction
                    ? new BoundMethod(name, userFunction, this)
                    : value;
            }

            // 2. 클래스와 부모 클래스들의 메서드/속성 확인
            PythonClass currentClass = Class;
            while (currentClass != null)
            {
                if (currentClass.ClassEnv.HasLocalVariable(name))
                {
                    var value = currentClass.ClassEnv.GetLocalVariable(name);
                    return value is UserFunction userFunction
                        ? new BoundMethod(name, userFunction, this)
                        : value;
                }
                currentClass = currentClass.ParentClass;
            }

            // 3. 특수 메서드들 확인 (__str__, __repr__ 등)
            if (name == "__class__")
                return Class;
            if (name == "__dict__")
            {
                var dict = new PythonDict();
                var vars = InstanceEnv.GetAllVariables();
                foreach (var kvp in vars)
                {
                    if (InstanceEnv.HasLocalVariable(kvp.Key))  // 인스턴스 변수만
                        dict.Items[new PythonString(kvp.Key)] = kvp.Value;
                }
                return dict;
            }

            throw new PythonException("AttributeError", $"'{Class.Name}' object has no attribute '{name}'");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAttribute(string name, PythonTypeObject value) => InstanceEnv.SetVariable(name, value);
    }

    public class PythonModule : PythonTypeObject
    {
        public string Name { get; }
        public Environment ModuleEnv { get; }

        public PythonModule(string name, List<string> searchPaths = null)
        {
            Name = name;
            ModuleEnv = new Environment(null);

            // __name__ 속성 설정
            ModuleEnv.SetVariable("__name__", new PythonString(name));
            ModuleEnv.SetVariable("__file__", new PythonString("<module>"));

            if (searchPaths != null)
            {
                ModuleEnv.SearchPaths = new List<string>(searchPaths);
            }
            else
            {
                ModuleEnv.SearchPaths = new List<string> { "." };
            }
        }

        public override PythonType Type => PythonType.Module;
        public override bool IsTrue() => true;
        public override string ToPythonString() => $"<module '{Name}'>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        public override PythonTypeObject GetAttribute(string name)
        {
            try
            {
                return ModuleEnv.GetVariable(name);
            }
            catch (PythonException)
            {
                throw new PythonException("AttributeError", $"module '{Name}' has no attribute '{name}'");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAttribute(string name, PythonTypeObject value) => ModuleEnv.SetVariable(name, value);

        public override List<string> GetMethodNames()
        {
            var attrs = new List<string>();
            var vars = ModuleEnv.GetAllVariables();
            foreach (var kvp in vars)
            {
                attrs.Add(kvp.Key);
            }
            return attrs.OrderBy(a => a).ToList();
        }
    }

    public sealed class PythonSuper : PythonTypeObject
    {
        private readonly PythonClass targetClass;
        private readonly PythonInstance instance;

        public PythonSuper(PythonClass cls, PythonInstance inst = null)
        {
            targetClass = cls;
            instance = inst;
        }

        public override PythonType Type => PythonType.Super;
        public override bool IsTrue() => true;
        public override string ToPythonString() =>
            $"<super: {targetClass?.Name ?? "NULL"}, {instance?.Class.Name ?? "NULL"}>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);

        public override PythonTypeObject GetAttribute(string name)
        {
            // 부모 클래스가 없으면 object의 기본 메서드 제공
            if (targetClass?.ParentClass == null)
            {
                if (name == "__init__")
                {
                    // object.__init__은 추가 인자를 무시
                    return new BuiltinFunction("__init__", args => PythonNone.Instance);
                }
                throw new PythonException("AttributeError",
                    $"super object has no attribute '{name}'");
            }

            // 부모 클래스에서 속성 찾기
            PythonClass searchClass = targetClass.ParentClass;

            while (searchClass != null)
            {
                if (searchClass.ClassEnv.HasLocalVariable(name))
                {
                    var value = searchClass.ClassEnv.GetLocalVariable(name);

                    // 인스턴스가 있고 함수인 경우 바인딩
                    if (instance != null && value is UserFunction userFunc)
                    {
                        return new BoundMethod(name, userFunc, instance);
                    }

                    return value;
                }

                searchClass = searchClass.ParentClass;
            }

            // 못 찾았으면 기본 object 메서드 확인
            if (name == "__init__")
            {
                return new BuiltinFunction("__init__", args => PythonNone.Instance);
            }

            throw new PythonException("AttributeError",
                $"super object has no attribute '{name}'");
        }
    }
}

// enhanced_python_types.cs