// enhanced_stdlib_with_registry.cs
using System;
using System.Collections.Generic;
using System.Linq;

#if GODOT
using Godot_IO;
#else
using DotNet_IO;
#endif

namespace SharpPy
{
    /// <summary>
    /// Python 표준 라이브러리 모듈 관리 클래스 (Method Registry 패턴 적용)
    /// </summary>
    public static class StandardLibrary
    {
        // 표준 라이브러리 모듈 캐시
        private static readonly Dictionary<string, Func<List<string>, PythonModule>> _stdlibModules;

        // 전역 searchPaths 참조
        private static List<string> _globalSearchPaths;

        public static void SetGlobalSearchPaths(List<string> searchPaths)
        {
            _globalSearchPaths = searchPaths;
        }

        public static List<string> GetGlobalSearchPaths()
        {
            return _globalSearchPaths ?? new List<string> { "." };
        }

        static StandardLibrary()
        {
            // 표준 라이브러리 모듈 등록
            _stdlibModules = new Dictionary<string, Func<List<string>, PythonModule>>
            {
                ["copy"] = CopyModule.CreateCopyModule,  // copy 모듈 추가
                ["json"] = JsonModule.CreateJsonModule,
                ["random"] = RandomModule.CreateRandomModule,
                ["math"] = MathModule.CreateMathModule,
                ["datetime"] = DateTimeModule.Create,
                ["collections"] = CollectionsModule.Create,
                ["re"] = RegexModule.Create,
                ["os"] = OsModule.Create,
                ["os.path"] = OsPathModule.Create,
                ["itertools"] = ItertoolsModule.Create,
                ["functools"] = FunctoolsModule.Create,
                ["string"] = StringModule.Create
            };
        }

        /// <summary>
        /// 표준 라이브러리 모듈 생성
        /// </summary>
        public static PythonModule CreateStdlibModule(string name, List<string> searchPaths = null)
        {
            if (_stdlibModules.TryGetValue(name, out var creator))
            {
                return creator(searchPaths);
            }
            return null;
        }

        /// <summary>
        /// 모듈이 표준 라이브러리인지 확인
        /// </summary>
        public static bool IsStdlibModule(string name)
        {
            return _stdlibModules.ContainsKey(name);
        }

        /// <summary>
        /// 사용 가능한 표준 라이브러리 모듈 목록
        /// </summary>
        public static List<string> GetAvailableModules()
        {
            return new List<string>(_stdlibModules.Keys);
        }
    }

    // === 각 모듈을 별도 클래스로 분리하여 Method Registry 패턴 적용 ===

    /// <summary>
    /// datetime 모듈
    /// </summary>
    public static class DateTimeModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static DateTimeModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["now"] = args =>
                {
                    if (args.Count != 0)
                        throw new PythonException("TypeError", "now() takes no arguments");

                    var now = DateTime.Now;
                    var dict = new PythonDict();
                    dict.SetItem(new PythonString("year"), PythonInt.Create(now.Year));
                    dict.SetItem(new PythonString("month"), PythonInt.Create(now.Month));
                    dict.SetItem(new PythonString("day"), PythonInt.Create(now.Day));
                    dict.SetItem(new PythonString("hour"), PythonInt.Create(now.Hour));
                    dict.SetItem(new PythonString("minute"), PythonInt.Create(now.Minute));
                    dict.SetItem(new PythonString("second"), PythonInt.Create(now.Second));
                    dict.SetItem(new PythonString("microsecond"), PythonInt.Create(now.Millisecond * 1000));
                    return dict;
                },

                ["utcnow"] = args =>
                {
                    if (args.Count != 0)
                        throw new PythonException("TypeError", "utcnow() takes no arguments");

                    var now = DateTime.UtcNow;
                    var dict = new PythonDict();
                    dict.SetItem(new PythonString("year"), PythonInt.Create(now.Year));
                    dict.SetItem(new PythonString("month"), PythonInt.Create(now.Month));
                    dict.SetItem(new PythonString("day"), PythonInt.Create(now.Day));
                    dict.SetItem(new PythonString("hour"), PythonInt.Create(now.Hour));
                    dict.SetItem(new PythonString("minute"), PythonInt.Create(now.Minute));
                    dict.SetItem(new PythonString("second"), PythonInt.Create(now.Second));
                    dict.SetItem(new PythonString("microsecond"), PythonInt.Create(now.Millisecond * 1000));
                    return dict;
                },

                ["date"] = args =>
                {
                    if (args.Count != 3)
                        throw new PythonException("TypeError", "date() takes exactly 3 arguments");

                    int year = NumberHelper.ToInt(args[0]);
                    int month = NumberHelper.ToInt(args[1]);
                    int day = NumberHelper.ToInt(args[2]);

                    var dict = new PythonDict();
                    dict.SetItem(new PythonString("year"), PythonInt.Create(year));
                    dict.SetItem(new PythonString("month"), PythonInt.Create(month));
                    dict.SetItem(new PythonString("day"), PythonInt.Create(day));
                    return dict;
                },

                ["time"] = args =>
                {
                    if (args.Count < 1 || args.Count > 4)
                        throw new PythonException("TypeError", "time() takes 1 to 4 arguments");

                    int hour = NumberHelper.ToInt(args[0]);
                    int minute = args.Count > 1 ? NumberHelper.ToInt(args[1]) : 0;
                    int second = args.Count > 2 ? NumberHelper.ToInt(args[2]) : 0;
                    int microsecond = args.Count > 3 ? NumberHelper.ToInt(args[3]) : 0;

                    var dict = new PythonDict();
                    dict.SetItem(new PythonString("hour"), PythonInt.Create(hour));
                    dict.SetItem(new PythonString("minute"), PythonInt.Create(minute));
                    dict.SetItem(new PythonString("second"), PythonInt.Create(second));
                    dict.SetItem(new PythonString("microsecond"), PythonInt.Create(microsecond));
                    return dict;
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            var module = new EnhancedModule("datetime", searchPaths, methods);

            // 상수 추가
            module.SetAttribute("MINYEAR", PythonInt.Create(1));
            module.SetAttribute("MAXYEAR", PythonInt.Create(9999));

            return module;
        }
    }

    /// <summary>
    /// collections 모듈
    /// </summary>
    public static class CollectionsModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static CollectionsModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["Counter"] = args =>
                {
                    var counter = new PythonDict();

                    if (args.Count > 0)
                    {
                        if (args[0] is PythonList list)
                        {
                            foreach (var item in list.Items)
                            {
                                if (counter.ContainsKey(item))
                                {
                                    var count = (PythonInt)counter.GetItem(item);
                                    counter.SetItem(item, PythonInt.Create(count.Value + 1));
                                }
                                else
                                {
                                    counter.SetItem(item, PythonInt.Create(1));
                                }
                            }
                        }
                        else if (args[0] is PythonString str)
                        {
                            foreach (char c in str.Value)
                            {
                                var charStr = new PythonString(c.ToString());
                                if (counter.ContainsKey(charStr))
                                {
                                    var count = (PythonInt)counter.GetItem(charStr);
                                    counter.SetItem(charStr, PythonInt.Create(count.Value + 1));
                                }
                                else
                                {
                                    counter.SetItem(charStr, PythonInt.Create(1));
                                }
                            }
                        }
                    }

                    return counter;
                },

                ["defaultdict"] = args =>
                {
                    // 간단한 구현: 일반 dict 반환
                    return new PythonDict();
                },

                ["deque"] = args =>
                {
                    var deque = new PythonList();
                    if (args.Count > 0 && args[0] is PythonList list)
                    {
                        deque.Items.AddRange(list.Items);
                    }
                    return deque;
                },

                ["namedtuple"] = args =>
                {
                    if (args.Count < 2)
                        throw new PythonException("TypeError", "namedtuple() takes at least 2 arguments");

                    // 간단한 구현: 클래스 이름만 반환
                    if (args[0] is PythonString className)
                    {
                        return className;
                    }
                    throw new PythonException("TypeError", "namedtuple() first argument must be a string");
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            return new EnhancedModule("collections", searchPaths, methods);
        }
    }

    /// <summary>
    /// re 모듈 (정규식)
    /// </summary>
    public static class RegexModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static RegexModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["match"] = args =>
                {
                    if (args.Count != 2)
                        throw new PythonException("TypeError", "match() takes exactly 2 arguments");

                    if (!(args[0] is PythonString pattern) || !(args[1] is PythonString text))
                        throw new PythonException("TypeError", "match() arguments must be strings");

                    if (text.Value.StartsWith(pattern.Value))
                    {
                        var match = new PythonDict();
                        match.SetItem(new PythonString("group"), new PythonString(pattern.Value));
                        match.SetItem(new PythonString("start"), PythonInt.Create(0));
                        match.SetItem(new PythonString("end"), PythonInt.Create(pattern.Value.Length));
                        return match;
                    }

                    return PythonNone.Instance;
                },

                ["search"] = args =>
                {
                    if (args.Count != 2)
                        throw new PythonException("TypeError", "search() takes exactly 2 arguments");

                    if (!(args[0] is PythonString pattern) || !(args[1] is PythonString text))
                        throw new PythonException("TypeError", "search() arguments must be strings");

                    int index = text.Value.IndexOf(pattern.Value);
                    if (index >= 0)
                    {
                        var match = new PythonDict();
                        match.SetItem(new PythonString("group"), new PythonString(pattern.Value));
                        match.SetItem(new PythonString("start"), PythonInt.Create(index));
                        match.SetItem(new PythonString("end"), PythonInt.Create(index + pattern.Value.Length));
                        return match;
                    }

                    return PythonNone.Instance;
                },

                ["findall"] = args =>
                {
                    if (args.Count != 2)
                        throw new PythonException("TypeError", "findall() takes exactly 2 arguments");

                    if (!(args[0] is PythonString pattern) || !(args[1] is PythonString text))
                        throw new PythonException("TypeError", "findall() arguments must be strings");

                    var result = new PythonList();
                    string searchText = text.Value;
                    string searchPattern = pattern.Value;
                    int index = 0;

                    while ((index = searchText.IndexOf(searchPattern, index)) != -1)
                    {
                        result.Items.Add(new PythonString(searchPattern));
                        index += searchPattern.Length;
                    }

                    return result;
                },

                ["sub"] = args =>
                {
                    if (args.Count != 3)
                        throw new PythonException("TypeError", "sub() takes exactly 3 arguments");

                    if (!(args[0] is PythonString pattern) ||
                        !(args[1] is PythonString replacement) ||
                        !(args[2] is PythonString text))
                        throw new PythonException("TypeError", "sub() arguments must be strings");

                    return new PythonString(text.Value.Replace(pattern.Value, replacement.Value));
                },

                ["split"] = args =>
                {
                    if (args.Count != 2)
                        throw new PythonException("TypeError", "split() takes exactly 2 arguments");

                    if (!(args[0] is PythonString pattern) || !(args[1] is PythonString text))
                        throw new PythonException("TypeError", "split() arguments must be strings");

                    var parts = text.Value.Split(new[] { pattern.Value }, StringSplitOptions.None);
                    var result = new PythonList();
                    foreach (var part in parts)
                        result.Items.Add(new PythonString(part));

                    return result;
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            var module = new EnhancedModule("re", searchPaths, methods);

            // 정규식 플래그 상수
            module.SetAttribute("IGNORECASE", PythonInt.Create(2));
            module.SetAttribute("I", PythonInt.Create(2));
            module.SetAttribute("MULTILINE", PythonInt.Create(8));
            module.SetAttribute("M", PythonInt.Create(8));
            module.SetAttribute("DOTALL", PythonInt.Create(16));
            module.SetAttribute("S", PythonInt.Create(16));

            return module;
        }
    }

    /// <summary>
    /// os 모듈
    /// </summary>
    public static class OsModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static OsModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["getcwd"] = args =>
                {
                    if (args.Count != 0)
                        throw new PythonException("TypeError", "getcwd() takes no arguments");
                    return new PythonString(System.IO.Directory.GetCurrentDirectory());
                },

                ["chdir"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "chdir() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "chdir() argument must be a string");

                    try
                    {
                        System.IO.Directory.SetCurrentDirectory(path.Value);
                    }
                    catch (Exception ex)
                    {
                        throw new PythonException("OSError", ex.Message);
                    }

                    return PythonNone.Instance;
                },

                ["listdir"] = args =>
                {
                    string path = ".";
                    if (args.Count > 0)
                    {
                        if (!(args[0] is PythonString pathStr))
                            throw new PythonException("TypeError", "listdir() argument must be a string");
                        path = pathStr.Value;
                    }

                    try
                    {
                        var entries = System.IO.Directory.GetFileSystemEntries(path);
                        var result = new PythonList();
                        foreach (var entry in entries)
                        {
                            result.Items.Add(new PythonString(System.IO.Path.GetFileName(entry)));
                        }
                        return result;
                    }
                    catch (Exception ex)
                    {
                        throw new PythonException("OSError", ex.Message);
                    }
                },

                ["mkdir"] = args =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new PythonException("TypeError", "mkdir() takes 1 or 2 arguments");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "mkdir() path must be a string");

                    try
                    {
                        System.IO.Directory.CreateDirectory(path.Value);
                    }
                    catch (Exception ex)
                    {
                        throw new PythonException("OSError", ex.Message);
                    }

                    return PythonNone.Instance;
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            var module = new EnhancedModule("os", searchPaths, methods);

            // os 상수들
            module.SetAttribute("name", new PythonString(
                System.Environment.OSVersion.Platform == PlatformID.Win32NT ? "nt" : "posix"
            ));

            module.SetAttribute("sep", new PythonString(System.IO.Path.DirectorySeparatorChar.ToString()));
            module.SetAttribute("pathsep", new PythonString(System.IO.Path.PathSeparator.ToString()));
            module.SetAttribute("linesep", new PythonString(System.Environment.NewLine));

            return module;
        }
    }

    /// <summary>
    /// os.path 모듈
    /// </summary>
    public static class OsPathModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static OsPathModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["join"] = args =>
                {
                    if (args.Count == 0)
                        throw new PythonException("TypeError", "join() requires at least one argument");

                    var paths = new List<string>();
                    foreach (var arg in args)
                    {
                        if (arg is PythonString str)
                            paths.Add(str.Value);
                        else
                            throw new PythonException("TypeError", "join() arguments must be strings");
                    }

                    return new PythonString(System.IO.Path.Combine(paths.ToArray()));
                },

                ["exists"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "exists() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "exists() argument must be a string");

                    bool exists = Helper.FileExists(path.Value) || Helper.DirExists(path.Value);
                    return PythonBool.Create(exists);
                },

                ["basename"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "basename() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "basename() argument must be a string");

                    return new PythonString(System.IO.Path.GetFileName(path.Value));
                },

                ["dirname"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "dirname() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "dirname() argument must be a string");

                    return new PythonString(System.IO.Path.GetDirectoryName(path.Value) ?? "");
                },

                ["splitext"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "splitext() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "splitext() argument must be a string");

                    var name = System.IO.Path.GetFileNameWithoutExtension(path.Value);
                    var ext = System.IO.Path.GetExtension(path.Value);

                    var tuple = new PythonTuple();
                    tuple.Items.Add(new PythonString(name));
                    tuple.Items.Add(new PythonString(ext));
                    return tuple;
                },

                ["abspath"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "abspath() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "abspath() argument must be a string");

                    return new PythonString(System.IO.Path.GetFullPath(path.Value));
                },

                ["isfile"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "isfile() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "isfile() argument must be a string");

                    return PythonBool.Create(Helper.FileExists(path.Value));
                },

                ["isdir"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "isdir() takes exactly 1 argument");

                    if (!(args[0] is PythonString path))
                        throw new PythonException("TypeError", "isdir() argument must be a string");

                    return PythonBool.Create(Helper.DirExists(path.Value));
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            return new EnhancedModule("os.path", searchPaths, methods);
        }
    }

    /// <summary>
    /// itertools 모듈
    /// </summary>
    public static class ItertoolsModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static ItertoolsModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["count"] = args =>
                {
                    int start = 0, step = 1;

                    if (args.Count > 0 && NumberHelper.IsNumber(args[0]))
                        start = NumberHelper.ToInt(args[0]);
                    if (args.Count > 1 && NumberHelper.IsNumber(args[1]))
                        step = NumberHelper.ToInt(args[1]);

                    // 간단한 구현: 리스트로 10개만 생성
                    var result = new PythonList();
                    for (int i = 0; i < 10; i++)
                    {
                        result.Items.Add(PythonInt.Create(start + i * step));
                    }
                    return result;
                },

                ["cycle"] = args =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "cycle() takes exactly 1 argument");

                    if (args[0] is PythonList list)
                    {
                        var result = new PythonList();
                        result.Items.AddRange(list.Items);
                        result.Items.AddRange(list.Items);
                        return result;
                    }

                    throw new PythonException("TypeError", "cycle() argument must be iterable");
                },

                ["repeat"] = args =>
                {
                    if (args.Count < 1)
                        throw new PythonException("TypeError", "repeat() requires at least 1 argument");

                    var obj = args[0];
                    int times = 10; // 기본값

                    if (args.Count > 1 && NumberHelper.IsNumber(args[1]))
                        times = NumberHelper.ToInt(args[1]);

                    var result = new PythonList();
                    for (int i = 0; i < times; i++)
                    {
                        result.Items.Add(obj);
                    }
                    return result;
                },

                ["chain"] = args =>
                {
                    var result = new PythonList();
                    foreach (var arg in args)
                    {
                        if (arg is PythonList list)
                            result.Items.AddRange(list.Items);
                        else if (arg is PythonTuple tuple)
                            result.Items.AddRange(tuple.Items);
                        else
                            throw new PythonException("TypeError", "chain() arguments must be iterables");
                    }
                    return result;
                },

                ["combinations"] = args =>
                {
                    if (args.Count != 2)
                        throw new PythonException("TypeError", "combinations() takes exactly 2 arguments");

                    // 간단한 구현: 빈 리스트 반환
                    return new PythonList();
                },

                ["permutations"] = args =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new PythonException("TypeError", "permutations() takes 1 or 2 arguments");

                    // 간단한 구현: 빈 리스트 반환
                    return new PythonList();
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            return new EnhancedModule("itertools", searchPaths, methods);
        }
    }

    /// <summary>
    /// functools 모듈
    /// </summary>
    public static class FunctoolsModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static FunctoolsModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["reduce"] = args =>
                {
                    if (args.Count < 2 || args.Count > 3)
                        throw new PythonException("TypeError", "reduce() takes 2 or 3 arguments");

                    if (!(args[0] is Function func))
                        throw new PythonException("TypeError", "reduce() first argument must be callable");

                    List<PythonTypeObject> items;
                    if (args[1] is PythonList list)
                        items = list.Items;
                    else if (args[1] is PythonTuple tuple)
                        items = tuple.Items;
                    else
                        throw new PythonException("TypeError", "reduce() second argument must be iterable");

                    if (items.Count == 0 && args.Count < 3)
                        throw new PythonException("TypeError", "reduce() of empty sequence with no initial value");

                    PythonTypeObject accumulator;
                    int startIndex;

                    if (args.Count == 3)
                    {
                        accumulator = args[2];
                        startIndex = 0;
                    }
                    else
                    {
                        accumulator = items[0];
                        startIndex = 1;
                    }

                    for (int i = startIndex; i < items.Count; i++)
                    {
                        var funcArgs = new List<PythonTypeObject> { accumulator, items[i] };
                        accumulator = func.Call(funcArgs);
                    }

                    return accumulator;
                },

                ["partial"] = args =>
                {
                    if (args.Count < 1)
                        throw new PythonException("TypeError", "partial() takes at least 1 argument");

                    // 간단한 구현: 원래 함수 반환
                    return args[0];
                },

                ["lru_cache"] = args =>
                {
                    // 데코레이터로 사용됨 - 간단히 항등 함수 반환
                    return new BuiltinFunction("lru_cache", innerArgs =>
                    {
                        if (innerArgs.Count > 0)
                            return innerArgs[0];
                        return PythonNone.Instance;
                    });
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            return new EnhancedModule("functools", searchPaths, methods);
        }
    }

    /// <summary>
    /// string 모듈
    /// </summary>
    public static class StringModule
    {
        private static readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods;

        static StringModule()
        {
            methods = new Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>>
            {
                ["capwords"] = args =>
                {
                    if (args.Count < 1 || args.Count > 2)
                        throw new PythonException("TypeError", "capwords() takes 1 or 2 arguments");

                    if (!(args[0] is PythonString text))
                        throw new PythonException("TypeError", "capwords() first argument must be a string");

                    string sep = args.Count > 1 && args[1] is PythonString sepStr ? sepStr.Value : " ";

                    var words = text.Value.Split(new[] { sep }, StringSplitOptions.None);
                    for (int i = 0; i < words.Length; i++)
                    {
                        if (words[i].Length > 0)
                        {
                            words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
                        }
                    }

                    return new PythonString(string.Join(sep, words));
                }
            };
        }

        public static PythonModule Create(List<string> searchPaths)
        {
            var module = new EnhancedModule("string", searchPaths, methods);

            // string 상수들
            module.SetAttribute("ascii_lowercase", new PythonString("abcdefghijklmnopqrstuvwxyz"));
            module.SetAttribute("ascii_uppercase", new PythonString("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));
            module.SetAttribute("ascii_letters", new PythonString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"));
            module.SetAttribute("digits", new PythonString("0123456789"));
            module.SetAttribute("hexdigits", new PythonString("0123456789abcdefABCDEF"));
            module.SetAttribute("octdigits", new PythonString("01234567"));
            module.SetAttribute("punctuation", new PythonString("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"));
            module.SetAttribute("whitespace", new PythonString(" \t\n\r\f\v"));
            module.SetAttribute("printable", new PythonString(
                "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ \t\n\r\f\v"
            ));

            return module;
        }
    }

    /// <summary>
    /// 향상된 모듈 클래스 - Method Registry 패턴 적용
    /// </summary>
    public class EnhancedModule : PythonModule
    {
        private readonly Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methodRegistry;

        public EnhancedModule(string name, List<string> searchPaths,
                            Dictionary<string, Func<List<PythonTypeObject>, PythonTypeObject>> methods)
            : base(name, searchPaths)
        {
            methodRegistry = methods;

            // 메소드를 모듈 속성으로 등록
            foreach (var kvp in methods)
            {
                SetAttribute(kvp.Key, new BuiltinFunction(kvp.Key, kvp.Value));
            }
        }

        public override List<string> GetMethodNames()
        {
            var names = new List<string>();

            // 모든 모듈 속성 가져오기
            var vars = ModuleEnv.GetAllVariables();
            foreach (var kvp in vars)
            {
                names.Add(kvp.Key);
            }

            // 메소드 레지스트리에서도 가져오기
            foreach (var method in methodRegistry.Keys)
            {
                if (!names.Contains(method))
                    names.Add(method);
            }

            return names.OrderBy(n => n).ToList();
        }
    }

    /// <summary>
    /// sys.path를 위한 특별한 리스트 클래스
    /// </summary>
    public class SysPathList : PythonList
    {
        private List<string> _searchPaths;
        private static Dictionary<string, Func<SysPathList, List<PythonTypeObject>, PythonTypeObject>> customMethods;

        static SysPathList()
        {
            customMethods = new Dictionary<string, Func<SysPathList, List<PythonTypeObject>, PythonTypeObject>>
            {
                ["append"] = (self, args) =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "append() takes exactly one argument");

                    if (!(args[0] is PythonString pathStr))
                        throw new PythonException("TypeError", "sys.path must contain strings");

                    self.Items.Add(args[0]);
                    self._searchPaths.Add(pathStr.Value);

                    return PythonNone.Instance;
                },

                ["insert"] = (self, args) =>
                {
                    if (args.Count != 2)
                        throw new PythonException("TypeError", "insert() takes exactly 2 arguments");

                    if (!NumberHelper.IsNumber(args[0]))
                        throw new PythonException("TypeError", "insert() first argument must be an integer");

                    if (!(args[1] is PythonString pathStr))
                        throw new PythonException("TypeError", "sys.path must contain strings");

                    int index = NumberHelper.ToInt(args[0]);
                    if (index < 0) index = Math.Max(0, self.Items.Count + index);
                    if (index > self.Items.Count) index = self.Items.Count;

                    self.Items.Insert(index, args[1]);

                    if (index <= self._searchPaths.Count)
                        self._searchPaths.Insert(index, pathStr.Value);
                    else
                        self._searchPaths.Add(pathStr.Value);

                    return PythonNone.Instance;
                },

                ["remove"] = (self, args) =>
                {
                    if (args.Count != 1)
                        throw new PythonException("TypeError", "remove() takes exactly one argument");

                    if (!(args[0] is PythonString pathStr))
                        throw new PythonException("TypeError", "sys.path must contain strings");

                    for (int i = 0; i < self.Items.Count; i++)
                    {
                        if (self.Items[i].Equals(args[0]))
                        {
                            self.Items.RemoveAt(i);
                            self._searchPaths.Remove(pathStr.Value);
                            return PythonNone.Instance;
                        }
                    }

                    throw new PythonException("ValueError", "list.remove(x): x not in list");
                },

                ["clear"] = (self, args) =>
                {
                    if (args.Count != 0)
                        throw new PythonException("TypeError", "clear() takes no arguments");

                    self.Items.Clear();
                    self._searchPaths.Clear();

                    return PythonNone.Instance;
                }
            };
        }

        public SysPathList(List<string> searchPaths) : base()
        {
            _searchPaths = searchPaths;

            // 초기값 설정
            foreach (var path in searchPaths)
            {
                Items.Add(new PythonString(path));
            }
        }

        public override BuiltinFunction GetMethod(string name)
        {
            // 커스텀 메소드가 있으면 우선 사용
            if (customMethods.TryGetValue(name, out var customMethod))
            {
                return new BuiltinFunction(name, args => customMethod(this, args));
            }

            // 없으면 기본 리스트 메소드 사용
            return base.GetMethod(name);
        }

        public override List<string> GetMethodNames()
        {
            var baseNames = base.GetMethodNames();
            // sys.path는 일반 리스트와 동일한 메소드를 가짐
            return baseNames;
        }

        // 인덱스 접근 시 searchPaths도 업데이트
        public new void SetItem(int index, PythonTypeObject value)
        {
            if (!(value is PythonString pathStr))
                throw new PythonException("TypeError", "sys.path must contain strings");

            if (index < 0) index += Items.Count;
            if (index < 0 || index >= Items.Count)
                throw new PythonException("IndexError", "list assignment index out of range");

            Items[index] = value;

            if (index < _searchPaths.Count)
                _searchPaths[index] = pathStr.Value;
        }
    }
}