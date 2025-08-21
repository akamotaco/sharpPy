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

            // 함수 진입 시 스택 프레임 추가
            var frame = new StackFrame(funcEnv.CurrentFileName, Name, 0, 0);
            ExecutionContext.PushFrame(frame);

            try
            {

                // 파라미터 분류
                var normalParams = Parameters.Where(p => p.Kind == ParameterKind.Normal).ToList();
                var varArgsParam = Parameters.FirstOrDefault(p => p.Kind == ParameterKind.VarArgs);
                var kwArgsParam = Parameters.FirstOrDefault(p => p.Kind == ParameterKind.KwArgs);

                // 사용된 키워드 인자 추적
                var usedKeywords = new HashSet<string>();
                var assignedParams = new Dictionary<string, PythonTypeObject>();

                // 1. 위치 인자를 일반 매개변수에 할당
                int posArgIndex = 0;
                for (int i = 0; i < normalParams.Count && posArgIndex < positionalArgs.Count; i++)
                {
                    var param = normalParams[i];

                    // 키워드로 이미 제공된 경우 건너뛰기
                    if (keywordArgs.ContainsKey(param.Name))
                        continue;

                    var value = positionalArgs[posArgIndex];

                    // 타입 힌트 검사
                    if (param.TypeHint != null && !param.TypeHint.IsCompatible(value))
                    {
                        throw new PythonException("TypeError",
                            $"Argument '{param.Name}' expected {param.TypeHint}, got {GetValueType(value)}");
                    }

                    funcEnv.SetVariable(param.Name, value);
                    assignedParams[param.Name] = value;
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

                        // 타입 힌트 검사
                        if (param.TypeHint != null && !param.TypeHint.IsCompatible(kvp.Value))
                        {
                            throw new PythonException("TypeError",
                                $"Argument '{param.Name}' expected {param.TypeHint}, got {GetValueType(kvp.Value)}");
                        }

                        funcEnv.SetVariable(param.Name, kvp.Value);
                        assignedParams[param.Name] = kvp.Value;
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
                            var defaultValue = evaluatedDefaults[i];

                            // 기본값도 타입 힌트 검사
                            if (param.TypeHint != null && !param.TypeHint.IsCompatible(defaultValue))
                            {
                                throw new PythonException("TypeError",
                                    $"Default value for '{param.Name}' expected {param.TypeHint}, got {GetValueType(defaultValue)}");
                            }

                            funcEnv.SetVariable(param.Name, defaultValue);
                            assignedParams[param.Name] = defaultValue;
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
                    {
                        // 현재 실행 중인 문장의 위치 업데이트
                        if (ExecutionContext.CurrentFrame != null)
                        {
                            ExecutionContext.CurrentFrame.Line = stmt.Line;
                            ExecutionContext.CurrentFrame.Column = stmt.Column;
                        }

                        result = stmt.Evaluate(funcEnv);
                    }

                    // 암시적 return None에 대한 타입 체크
                    if (ReturnTypeHint != null && !ReturnTypeHint.IsCompatible(result))
                    {
                        // 함수의 마지막 줄 위치 사용
                        var lastLine = Body.Count > 0 ? Body[Body.Count - 1].Line : 0;
                        var lastColumn = Body.Count > 0 ? Body[Body.Count - 1].Column : 0;

                        var ex = new PythonException("TypeError",
                            $"Return value expected {ReturnTypeHint}, got {GetValueType(result)}",
                            lastLine, lastColumn, funcEnv.CurrentFileName);

                        // 현재 스택 프레임 추가
                        ex.AddStackFrame(new StackFrame(funcEnv.CurrentFileName, Name, lastLine, lastColumn));

                        // 호출 스택 추가
                        foreach (var sf in ExecutionContext.GetCallStack().Skip(1).Reverse())
                        {
                            ex.AddStackFrame(sf);
                        }

                        throw ex;
                    }

                    return result;
                }
                catch (ReturnException ex)
                {
                    // return 문에서 반환된 값의 타입 체크
                    if (ReturnTypeHint != null && !ReturnTypeHint.IsCompatible(ex.Value))
                    {
                        var typeEx = new PythonException("TypeError",
                            $"Return value expected {ReturnTypeHint}, got {GetValueType(ex.Value)}",
                            ex.Line, ex.Column, ex.FileName ?? funcEnv.CurrentFileName);

                        // return 문 위치를 스택에 추가
                        typeEx.AddStackFrame(new StackFrame(
                            ex.FileName ?? funcEnv.CurrentFileName,
                            Name,
                            ex.Line,
                            ex.Column
                        ));

                        // 호출 스택 추가
                        foreach (var sf in ExecutionContext.GetCallStack().Skip(1).Reverse())
                        {
                            typeEx.AddStackFrame(sf);
                        }

                        throw typeEx;
                    }

                    return ex.Value;
                }
            }
            catch (PythonException ex)
            {
                // 이미 스택 정보가 있으면 그대로 전달
                if (ex.CallStack.Count > 0)
                {
                    throw;
                }

                // 스택 정보가 없으면 현재 위치 추가
                ex.AddStackFrame(ExecutionContext.CurrentFrame);
                throw;
            }
            finally
            {
                ExecutionContext.PopFrame();
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
            var funcEnv = new Environment(ClosureEnv, ClosureEnv.globalEnv, EnvironmentType.Enclosing);

            int posArgIndex = 0;
            for (int i = 0; i < Parameters.Count && posArgIndex < positionalArgs.Count; i++)
            {
                var param = Parameters[i];
                if (keywordArgs.ContainsKey(param.Name))
                    continue;

                var value = positionalArgs[posArgIndex];

                // 타입 힌트 검사
                if (param.TypeHint != null && !param.TypeHint.IsCompatible(value))
                {
                    throw new PythonException("TypeError",
                        $"Lambda argument '{param.Name}' expected {param.TypeHint}, got {GetValueType(value)}");
                }

                funcEnv.SetVariable(param.Name, value);
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

                    // 타입 힌트 검사
                    if (param.TypeHint != null && !param.TypeHint.IsCompatible(kvp.Value))
                    {
                        throw new PythonException("TypeError",
                            $"Lambda argument '{param.Name}' expected {param.TypeHint}, got {GetValueType(kvp.Value)}");
                    }

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
                        var defaultValue = evaluatedDefaults[i];

                        // 기본값 타입 힌트 검사
                        if (param.TypeHint != null && !param.TypeHint.IsCompatible(defaultValue))
                        {
                            throw new PythonException("TypeError",
                                $"Lambda default value for '{param.Name}' expected {param.TypeHint}, got {GetValueType(defaultValue)}");
                        }

                        funcEnv.SetVariable(param.Name, defaultValue);
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
        public List<PythonClass> ParentClasses { get; }  // 다중 상속을 위해 List로 변경
        private List<PythonClass> _mro;  // Method Resolution Order 캐시

        // 단일 상속을 위한 호환성 유지
        public PythonClass ParentClass => ParentClasses?.FirstOrDefault();

        // 다중 상속 생성자
        public PythonClass(string name, Environment classEnv, List<PythonClass> parentClasses = null)
        {
            Name = name;
            ClassEnv = classEnv;
            ParentClasses = parentClasses ?? new List<PythonClass>();
            _mro = null; // 지연 계산
        }

        // 단일 상속 생성자 (호환성)
        public PythonClass(string name, Environment classEnv, PythonClass parentClass)
            : this(name, classEnv, parentClass != null ? new List<PythonClass> { parentClass } : null)
        {
        }

        public override PythonType Type => PythonType.Class;
        public override bool IsTrue() => true;
        public override bool IsCallable() => true;
        public override string ToPythonString() => $"<class '{Name}'>";
        public override object GetRawValue() => this;
        public override bool Equals(PythonTypeObject other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();

        // C3 선형화 알고리즘으로 MRO 계산
        public List<PythonClass> GetMRO()
        {
            if (_mro != null) return _mro;

            _mro = C3Linearization();
            return _mro;
        }

        private List<PythonClass> C3Linearization()
        {
            // C3 알고리즘 구현
            var result = new List<PythonClass> { this };

            if (ParentClasses.Count == 0)
                return result;

            // 부모들의 MRO 가져오기
            var parentMROs = new List<List<PythonClass>>();
            foreach (var parent in ParentClasses)
            {
                parentMROs.Add(parent.GetMRO());
            }

            // 부모 클래스 리스트 추가
            var parentsAsList = new List<PythonClass>(ParentClasses);

            // 모든 리스트를 작업 리스트로 복사
            var lists = new List<List<PythonClass>>();
            foreach (var mro in parentMROs)
            {
                lists.Add(new List<PythonClass>(mro));
            }
            lists.Add(parentsAsList);

            // C3 병합
            while (lists.Any(l => l.Count > 0))
            {
                PythonClass candidate = null;

                // 좋은 head 찾기
                foreach (var list in lists.Where(l => l.Count > 0))
                {
                    var head = list[0];

                    // head가 다른 리스트의 tail에 있는지 확인
                    bool inTail = false;
                    foreach (var otherList in lists)
                    {
                        if (otherList.Count > 1 && otherList.Skip(1).Contains(head))
                        {
                            inTail = true;
                            break;
                        }
                    }

                    if (!inTail)
                    {
                        candidate = head;
                        break;
                    }
                }

                if (candidate == null)
                {
                    throw new PythonException("TypeError",
                        $"Cannot create a consistent method resolution order (MRO) for bases {string.Join(", ", ParentClasses.Select(p => p.Name))}");
                }

                result.Add(candidate);

                // 모든 리스트에서 candidate 제거
                foreach (var list in lists)
                {
                    list.Remove(candidate);
                }

                // 빈 리스트 제거
                lists.RemoveAll(l => l.Count == 0);
            }

            return result;
        }

        public void SetAttribute(string name, PythonTypeObject value)
        {
            ClassEnv.SetVariable(name, value);
        }

        public PythonTypeObject GetClassAttribute(string name)
        {
            // MRO 순서대로 속성 찾기
            var mro = GetMRO();

            foreach (var cls in mro)
            {
                if (cls.ClassEnv.HasLocalVariable(name))
                {
                    var value = cls.ClassEnv.GetLocalVariable(name);

                    // StaticMethod는 언래핑
                    if (value is StaticMethod sm)
                        return sm.Method;

                    // ClassMethod는 클래스를 첫 번째 인자로 바인딩
                    if (value is ClassMethod cm)
                        return new BoundClassMethod(name, cm.Method, this);

                    return value;
                }
            }

            throw new PythonException("AttributeError",
                $"type object '{Name}' has no attribute '{name}'");
        }

        public PythonInstance CreateInstance(List<PythonTypeObject> args)
        {
            return CreateInstanceWithKeywords(args, new Dictionary<string, PythonTypeObject>());
        }

        // PythonClass의 CreateInstanceWithKeywords 메서드 수정
        public PythonInstance CreateInstanceWithKeywords(List<PythonTypeObject> positionalArgs,
                                                     Dictionary<string, PythonTypeObject> keywordArgs)
        {
            var instance = new PythonInstance(this);

            // __init__ 메서드 찾기
            UserFunction initMethod = null;
            var mro = GetMRO();

            foreach (var cls in mro)
            {
                if (cls.ClassEnv.HasLocalVariable("__init__"))
                {
                    var method = cls.ClassEnv.GetLocalVariable("__init__");
                    if (method is UserFunction userFunc)
                    {
                        initMethod = userFunc;
                        break;
                    }
                }
            }

            if (initMethod != null)
            {
                // __init__ 호출 위치 기록
                var frame = new StackFrame(
                    ClassEnv.CurrentFileName ?? "<class>",
                    "__init__",
                    0, 0
                );
                ExecutionContext.PushFrame(frame);

                try
                {
                    var argsWithSelf = new List<PythonTypeObject> { instance };
                    argsWithSelf.AddRange(positionalArgs);
                    initMethod.CallWithKeywords(argsWithSelf, keywordArgs);
                }
                finally
                {
                    ExecutionContext.PopFrame();
                }
            }
            else if (positionalArgs.Count > 0 || keywordArgs.Count > 0)
            {
                throw new PythonException("TypeError", $"{Name}() takes no arguments");
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
                    var bases = new PythonTuple();
                    foreach (var parent in ParentClasses)
                        bases.Items.Add(parent);
                    return bases;

                case "__mro__":
                    var mroTuple = new PythonTuple();
                    foreach (var cls in GetMRO())
                        mroTuple.Items.Add(cls);
                    return mroTuple;

                case "mro":
                    return new BuiltinFunction("mro", args =>
                    {
                        if (args.Count != 0)
                            throw new PythonException("TypeError", "mro() takes no arguments");

                        var mroList = new PythonList();
                        foreach (var cls in GetMRO())
                            mroList.Items.Add(cls);
                        return mroList;
                    });

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

            // MRO 순서대로 모든 메서드
            var mro = Class.GetMRO();
            foreach (var cls in mro)
            {
                foreach (var kvp in cls.ClassEnv.variables)
                {
                    if (!methods.Contains(kvp.Key))
                        methods.Add(kvp.Key);
                }
            }

            return methods.OrderBy(m => m).ToList();
        }

        public override PythonTypeObject GetAttribute(string name)
        {
            // 특수 속성들 먼저 확인
            if (name == "__class__")
                return Class;

            if (name == "__mro__")
            {
                // 인스턴스의 클래스 MRO 반환
                var mroTuple = new PythonTuple();
                foreach (var cls in Class.GetMRO())
                    mroTuple.Items.Add(cls);
                return mroTuple;
            }

            if (name == "__dict__")
            {
                var dict = new PythonDict();
                var vars = InstanceEnv.GetAllVariables();
                foreach (var kvp in vars)
                {
                    if (InstanceEnv.HasLocalVariable(kvp.Key))
                        dict.Items[new PythonString(kvp.Key)] = kvp.Value;
                }
                return dict;
            }

            // 1. 먼저 인스턴스 변수 확인
            if (InstanceEnv.HasLocalVariable(name))
            {
                var value = InstanceEnv.GetLocalVariable(name);
                return value is UserFunction userFunction
                    ? new BoundMethod(name, userFunction, this)
                    : value;
            }

            // 2. MRO 순서대로 클래스 메서드/속성 확인
            var mro = Class.GetMRO();
            foreach (var cls in mro)
            {
                if (cls.ClassEnv.HasLocalVariable(name))
                {
                    var value = cls.ClassEnv.GetLocalVariable(name);

                    // StaticMethod - 언래핑만 하고 바인딩 없음
                    if (value is StaticMethod sm)
                        return sm.Method;

                    // ClassMethod - 클래스를 첫 번째 인자로 바인딩
                    if (value is ClassMethod cm)
                        return new BoundClassMethod(name, cm.Method, Class);

                    // 일반 메서드 - self를 첫 번째 인자로 바인딩
                    if (value is UserFunction userFunc)
                        return new BoundMethod(name, userFunc, this);

                    return value;
                }
            }

            throw new PythonException("AttributeError",
                $"'{Class.Name}' object has no attribute '{name}'");
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
            // MRO에서 현재 클래스 다음부터 찾기
            var mro = instance?.Class.GetMRO() ?? targetClass.GetMRO();

            int startIndex = -1;
            for (int i = 0; i < mro.Count; i++)
            {
                if (ReferenceEquals(mro[i], targetClass))
                {
                    startIndex = i + 1;
                    break;
                }
            }

            if (startIndex < 0 || startIndex >= mro.Count)
            {
                // 기본 object 메서드들
                if (name == "__init__")
                {
                    return new BuiltinFunction("__init__", args => PythonNone.Instance);
                }
                throw new PythonException("AttributeError",
                    $"super object has no attribute '{name}'");
            }

            // MRO 순서대로 속성 찾기
            for (int i = startIndex; i < mro.Count; i++)
            {
                if (mro[i].ClassEnv.HasLocalVariable(name))
                {
                    var value = mro[i].ClassEnv.GetLocalVariable(name);

                    // 인스턴스가 있고 함수인 경우 바인딩
                    if (instance != null && value is UserFunction userFunc)
                    {
                        return new BoundMethod(name, userFunc, instance);
                    }

                    return value;
                }
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

    public sealed class BoundClassMethod : Function
    {
        private readonly Function method;
        private readonly PythonClass cls;

        public BoundClassMethod(string name, Function method, PythonClass cls) : base(name)
        {
            this.method = method;
            this.cls = cls;
        }

        public override PythonTypeObject Call(List<PythonTypeObject> arguments)
        {
            // 클래스를 첫 번째 인자로 추가
            var newArgs = new List<PythonTypeObject>(arguments.Count + 1) { cls };
            newArgs.AddRange(arguments);

            if (method is UserFunction userFunc)
                return userFunc.Call(newArgs);

            return method.Call(newArgs);
        }

        public PythonTypeObject CallWithKeywords(List<PythonTypeObject> positionalArgs,
                                                Dictionary<string, PythonTypeObject> keywordArgs)
        {
            var newArgs = new List<PythonTypeObject>(positionalArgs.Count + 1) { cls };
            newArgs.AddRange(positionalArgs);

            if (method is UserFunction userFunc)
                return userFunc.CallWithKeywords(newArgs, keywordArgs);

            return method.Call(newArgs);
        }
    }
}

// enhanced_python_types.cs