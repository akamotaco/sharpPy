// sharppy_stdlib.cs
using System;
using System.Collections.Generic;

namespace SharpPy
{
    /// <summary>
    /// Python 표준 라이브러리 모듈 관리 클래스
    /// </summary>
    public static class StandardLibrary
    {
        // 표준 라이브러리 모듈 캐시
        private static readonly Dictionary<string, Func<List<string>, PythonModule>> _stdlibModules;

        // 전역 searchPaths 참조 (ImportModule에서 사용하는 것과 동일)
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
                ["json"] = JsonModule.CreateJsonModule,
                ["random"] = RandomModule.CreateRandomModule,
                ["datetime"] = searchPaths => CreateDateTimeModule(searchPaths),
                ["collections"] = searchPaths => CreateCollectionsModule(searchPaths),
                ["re"] = searchPaths => CreateRegexModule(searchPaths),
                ["sys"] = searchPaths => CreateSysModule(searchPaths),
                ["os.path"] = searchPaths => CreateOsPathModule(searchPaths),
                ["itertools"] = searchPaths => CreateItertoolsModule(searchPaths),
                ["functools"] = searchPaths => CreateFunctoolsModule(searchPaths),
                ["string"] = searchPaths => CreateStringModule(searchPaths)
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

        // === 추가 표준 라이브러리 모듈 구현 (예시) ===

        /// <summary>
        /// datetime 모듈 (간단한 구현 예시)
        /// </summary>
        private static PythonModule CreateDateTimeModule(List<string> searchPaths)
        {
            var module = new PythonModule("datetime", searchPaths);

            // datetime.datetime.now()
            module.SetAttribute("now", new BuiltinFunction("now", args =>
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
            }));

            // datetime.datetime.utcnow()
            module.SetAttribute("utcnow", new BuiltinFunction("utcnow", args =>
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
            }));

            return module;
        }

        /// <summary>
        /// collections 모듈 (간단한 구현 예시)
        /// </summary>
        private static PythonModule CreateCollectionsModule(List<string> searchPaths)
        {
            var module = new PythonModule("collections", searchPaths);

            // collections.Counter (간단한 버전)
            module.SetAttribute("Counter", new BuiltinFunction("Counter", args =>
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
            }));

            // collections.defaultdict (간단한 버전)
            module.SetAttribute("defaultdict", new BuiltinFunction("defaultdict", args =>
            {
                // 간단한 구현: 일반 dict 반환
                return new PythonDict();
            }));

            return module;
        }

        /// <summary>
        /// re 모듈 (정규식 - 간단한 구현 예시)
        /// </summary>
        private static PythonModule CreateRegexModule(List<string> searchPaths)
        {
            var module = new PythonModule("re", searchPaths);

            // re.match(pattern, string)
            module.SetAttribute("match", new BuiltinFunction("match", args =>
            {
                if (args.Count != 2)
                    throw new PythonException("TypeError", "match() takes exactly 2 arguments");

                if (!(args[0] is PythonString pattern) || !(args[1] is PythonString text))
                    throw new PythonException("TypeError", "match() arguments must be strings");

                // 간단한 구현: 문자열이 패턴으로 시작하는지 확인
                if (text.Value.StartsWith(pattern.Value))
                {
                    // Match object를 간단한 dict로 표현
                    var match = new PythonDict();
                    match.SetItem(new PythonString("group"), new PythonString(pattern.Value));
                    match.SetItem(new PythonString("start"), PythonInt.Create(0));
                    match.SetItem(new PythonString("end"), PythonInt.Create(pattern.Value.Length));
                    return match;
                }
                
                return PythonNone.Instance;
            }));

            // re.search(pattern, string)
            module.SetAttribute("search", new BuiltinFunction("search", args =>
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
            }));

            // re.findall(pattern, string)
            module.SetAttribute("findall", new BuiltinFunction("findall", args =>
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
            }));

            return module;
        }
        
        /// <summary>
        /// sys.path를 위한 특별한 리스트 클래스
        /// 수정 시 실제 searchPaths에 반영됨 , 이를 위해 PythonList를 상속받아 재구현 해야함.
        /// </summary>
        public class SysPathList : PythonList
        {
            private List<string> _searchPaths;
            
            public SysPathList(List<string> searchPaths) : base()
            {
                _searchPaths = searchPaths;
                
                // 초기값 설정
                foreach (var path in searchPaths)
                {
                    Items.Add(new PythonString(path));
                }
            }
            
            // append 메서드 오버라이드
            public new BuiltinFunction GetMethod(string name)
            {
                switch (name)
                {
                    case "append":
                        return new BuiltinFunction("append", args =>
                        {
                            if (args.Count != 1)
                                throw new PythonException("TypeError", "append() takes exactly one argument");
                            
                            if (!(args[0] is PythonString pathStr))
                                throw new PythonException("TypeError", "sys.path must contain strings");
                            
                            // 실제 리스트에 추가
                            Items.Add(args[0]);
                            
                            // searchPaths에도 추가
                            _searchPaths.Add(pathStr.Value);
                            
                            return PythonNone.Instance;
                        });
                        
                    case "insert":
                        return new BuiltinFunction("insert", args =>
                        {
                            if (args.Count != 2)
                                throw new PythonException("TypeError", "insert() takes exactly 2 arguments");
                            
                            if (!NumberHelper.IsNumber(args[0]))
                                throw new PythonException("TypeError", "insert() first argument must be an integer");
                            
                            if (!(args[1] is PythonString pathStr))
                                throw new PythonException("TypeError", "sys.path must contain strings");
                            
                            int index = NumberHelper.ToInt(args[0]);
                            if (index < 0) index = Math.Max(0, Items.Count + index);
                            if (index > Items.Count) index = Items.Count;
                            
                            // 실제 리스트에 삽입
                            Items.Insert(index, args[1]);
                            
                            // searchPaths에도 삽입
                            if (index <= _searchPaths.Count)
                                _searchPaths.Insert(index, pathStr.Value);
                            else
                                _searchPaths.Add(pathStr.Value);
                            
                            return PythonNone.Instance;
                        });
                        
                    case "remove":
                        return new BuiltinFunction("remove", args =>
                        {
                            if (args.Count != 1)
                                throw new PythonException("TypeError", "remove() takes exactly one argument");
                            
                            if (!(args[0] is PythonString pathStr))
                                throw new PythonException("TypeError", "sys.path must contain strings");
                            
                            // 찾아서 제거
                            for (int i = 0; i < Items.Count; i++)
                            {
                                if (Items[i].Equals(args[0]))
                                {
                                    Items.RemoveAt(i);
                                    
                                    // searchPaths에서도 제거
                                    _searchPaths.Remove(pathStr.Value);
                                    
                                    return PythonNone.Instance;
                                }
                            }
                            
                            throw new PythonException("ValueError", "list.remove(x): x not in list");
                        });
                        
                    case "clear":
                        return new BuiltinFunction("clear", args =>
                        {
                            if (args.Count != 0)
                                throw new PythonException("TypeError", "clear() takes no arguments");
                            
                            Items.Clear();
                            _searchPaths.Clear();
                            
                            return PythonNone.Instance;
                        });
                        
                    default:
                        return base.GetMethod(name);
                }
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

        /// <summary>
        /// sys 모듈 (시스템 관련)
        /// </summary>
        private static PythonModule CreateSysModule(List<string> searchPaths)
        {
            var module = new PythonModule("sys", searchPaths);

            // 전역 searchPaths를 직접 참조하는 리스트 생성
            var actualSearchPaths = searchPaths ?? GetGlobalSearchPaths();
            var pathList = new SysPathList(actualSearchPaths);

            module.SetAttribute("path", pathList);

            // sys.version
            module.SetAttribute("version", new PythonString("SharpPy 1.0.0 (Python 3.x compatible)"));

            // sys.platform
            module.SetAttribute("platform", new PythonString(
                System.Environment.OSVersion.Platform == PlatformID.Win32NT ? "win32" :
                System.Environment.OSVersion.Platform == PlatformID.Unix ? "linux" :
                System.Environment.OSVersion.Platform == PlatformID.MacOSX ? "darwin" :
                "unknown"
            ));

            // sys.argv (명령줄 인자 - 빈 리스트로 초기화)
            module.SetAttribute("argv", new PythonList());

            // sys.exit([code])
            module.SetAttribute("exit", new BuiltinFunction("exit", args =>
            {
                int exitCode = 0;
                if (args.Count > 0 && NumberHelper.IsNumber(args[0]))
                    exitCode = NumberHelper.ToInt(args[0]);

                throw new PythonException("SystemExit", exitCode.ToString());
            }));

            return module;
        }

        /// <summary>
        /// os.path 모듈 (경로 관련)
        /// </summary>
        private static PythonModule CreateOsPathModule(List<string> searchPaths)
        {
            var module = new PythonModule("os.path", searchPaths);

            // os.path.join(*paths)
            module.SetAttribute("join", new BuiltinFunction("join", args =>
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
            }));

            // os.path.exists(path)
            module.SetAttribute("exists", new BuiltinFunction("exists", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "exists() takes exactly 1 argument");

                if (!(args[0] is PythonString path))
                    throw new PythonException("TypeError", "exists() argument must be a string");

                bool exists = System.IO.File.Exists(path.Value) || System.IO.Directory.Exists(path.Value);
                return PythonBool.Create(exists);
            }));

            // os.path.basename(path)
            module.SetAttribute("basename", new BuiltinFunction("basename", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "basename() takes exactly 1 argument");

                if (!(args[0] is PythonString path))
                    throw new PythonException("TypeError", "basename() argument must be a string");

                return new PythonString(System.IO.Path.GetFileName(path.Value));
            }));

            // os.path.dirname(path)
            module.SetAttribute("dirname", new BuiltinFunction("dirname", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "dirname() takes exactly 1 argument");

                if (!(args[0] is PythonString path))
                    throw new PythonException("TypeError", "dirname() argument must be a string");

                return new PythonString(System.IO.Path.GetDirectoryName(path.Value) ?? "");
            }));

            return module;
        }

        /// <summary>
        /// itertools 모듈 (반복자 도구)
        /// </summary>
        private static PythonModule CreateItertoolsModule(List<string> searchPaths)
        {
            var module = new PythonModule("itertools", searchPaths);

            // itertools.count(start=0, step=1)
            module.SetAttribute("count", new BuiltinFunction("count", args =>
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
            }));

            // itertools.cycle(iterable)
            module.SetAttribute("cycle", new BuiltinFunction("cycle", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "cycle() takes exactly 1 argument");

                // 간단한 구현: 원본 리스트를 2번 반복
                if (args[0] is PythonList list)
                {
                    var result = new PythonList();
                    result.Items.AddRange(list.Items);
                    result.Items.AddRange(list.Items);
                    return result;
                }

                throw new PythonException("TypeError", "cycle() argument must be iterable");
            }));

            // itertools.repeat(object, times=None)
            module.SetAttribute("repeat", new BuiltinFunction("repeat", args =>
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
            }));

            return module;
        }

        /// <summary>
        /// functools 모듈 (함수형 도구)
        /// </summary>
        private static PythonModule CreateFunctoolsModule(List<string> searchPaths)
        {
            var module = new PythonModule("functools", searchPaths);

            // functools.reduce(function, iterable[, initializer])
            module.SetAttribute("reduce", new BuiltinFunction("reduce", args =>
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
            }));

            return module;
        }

        /// <summary>
        /// string 모듈 (문자열 상수와 도구)
        /// </summary>
        private static PythonModule CreateStringModule(List<string> searchPaths)
        {
            var module = new PythonModule("string", searchPaths);

            // string.ascii_lowercase
            module.SetAttribute("ascii_lowercase", new PythonString("abcdefghijklmnopqrstuvwxyz"));

            // string.ascii_uppercase
            module.SetAttribute("ascii_uppercase", new PythonString("ABCDEFGHIJKLMNOPQRSTUVWXYZ"));

            // string.ascii_letters
            module.SetAttribute("ascii_letters", new PythonString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ"));

            // string.digits
            module.SetAttribute("digits", new PythonString("0123456789"));

            // string.hexdigits
            module.SetAttribute("hexdigits", new PythonString("0123456789abcdefABCDEF"));

            // string.octdigits
            module.SetAttribute("octdigits", new PythonString("01234567"));

            // string.punctuation
            module.SetAttribute("punctuation", new PythonString("!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~"));

            // string.whitespace
            module.SetAttribute("whitespace", new PythonString(" \t\n\r\f\v"));

            // string.printable
            module.SetAttribute("printable", new PythonString(
                "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~ \t\n\r\f\v"
            ));

            return module;
        }
    }
}