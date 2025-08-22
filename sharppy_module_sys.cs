// sys_module_instance.cs - sys 모듈 (static 완전 제거)
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpPy
{
    // sys.path를 나타내는 특별한 리스트 클래스
    public class SysPath : PythonList
    {
        public SysPath() : base()
        {
            // 기본 경로 초기화
            Items.Add(new PythonString("."));
            Items.Add(new PythonString("./lib"));
            Items.Add(new PythonString("./site-packages"));
        }
        
        // 실제 문자열 경로 리스트로 변환
        public List<string> ToStringList()
        {
            return Items
                .Where(item => item is PythonString)
                .Select(item => ((PythonString)item).Value)
                .ToList();
        }
        
        // 경로 추가 헬퍼
        public void AddPath(string path)
        {
            if (!Items.Any(item => item is PythonString ps && ps.Value == path))
            {
                Items.Add(new PythonString(path));
            }
        }
        
        public void InsertPath(int index, string path)
        {
            Items.Insert(index, new PythonString(path));
        }
    }
    
    // sys 모듈 구현
    public class SysModule : PythonModule
    {
        public SysPath Path { get; private set; }
        
        public SysModule() : base("sys", null)
        {
            this.Path = new SysPath();
            
            SetupSysModule();
        }
        
        private void SetupSysModule()
        {
            // sys.path 설정
            SetAttribute("path", Path);
            
            // sys.version 설정
            SetAttribute("version", new PythonString("3.9.0 (SharpPy Implementation)"));
            SetAttribute("version_info", CreateVersionInfo());
            
            // sys.platform 설정
            string platform = GetPlatform();
            SetAttribute("platform", new PythonString(platform));
            
            // sys.executable 설정 (현재 실행 파일 경로)
            SetAttribute("executable", new PythonString(System.Reflection.Assembly.GetExecutingAssembly().Location));
            
            // sys.modules - 로드된 모듈 딕셔너리
            SetAttribute("modules", new PythonDict());
            
            // sys.exit() 함수
            SetAttribute("exit", new BuiltinFunction("exit", args =>
            {
                int exitCode = 0;
                if (args.Count > 0 && args[0] is PythonInt pi)
                {
                    exitCode = pi.Value;
                }
                throw new SystemExitException(exitCode);
            }));
            
            // sys.argv - 명령줄 인자 (기본값)
            var argv = new PythonList();
            argv.Items.Add(new PythonString(""));
            SetAttribute("argv", argv);
            
            // sys.stdin, stdout, stderr (간단한 구현)
            SetAttribute("stdin", new PythonString("<stdin>"));
            SetAttribute("stdout", new PythonString("<stdout>"));
            SetAttribute("stderr", new PythonString("<stderr>"));
        }
        
        private PythonTuple CreateVersionInfo()
        {
            var versionInfo = new PythonTuple();
            versionInfo.Items.Add(new PythonInt(3));      // major
            versionInfo.Items.Add(new PythonInt(9));      // minor
            versionInfo.Items.Add(new PythonInt(0));      // micro
            versionInfo.Items.Add(new PythonString("final")); // releaselevel
            versionInfo.Items.Add(new PythonInt(0));      // serial
            return versionInfo;
        }
        
        private string GetPlatform()
        {
            // 플랫폼 감지
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return "win32";
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return "linux";
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return "darwin";
            }
            return "unknown";
        }
        
        // 로드된 모듈 업데이트
        public void UpdateModules(string name, PythonModule module)
        {
            var modules = GetAttribute("modules") as PythonDict;
            if (modules != null)
            {
                modules.Items[new PythonString(name)] = module;
            }
        }
    }
    
    // SystemExit 예외
    public class SystemExitException : Exception
    {
        public int ExitCode { get; }
        
        public SystemExitException(int exitCode) : base($"SystemExit: {exitCode}")
        {
            ExitCode = exitCode;
        }
    }
    
    /// <summary>
    /// sys 모듈 클래스 (static이 아닌 일반 인스턴스 클래스)
    /// </summary>
    public class SysModuleInstance : PythonModule
    {
        private SysPathList pathList;

        public SysModuleInstance(List<string> searchPaths)
            : base("sys", searchPaths)
        {
            SetupSysModule(searchPaths);
        }

        private void SetupSysModule(List<string> searchPaths)
        {
            // sys.path - 특별한 리스트
            var actualSearchPaths = searchPaths ?? new List<string> { "." };
            pathList = new SysPathList(actualSearchPaths);
            SetAttribute("path", pathList);

            // sys.version
            SetAttribute("version", new PythonString("SharpPy 1.0.0 (Python 3.x compatible)"));

            // sys.platform
            SetAttribute("platform", new PythonString(GetPlatform()));

            // sys.argv
            SetAttribute("argv", new PythonList());

            // sys.modules - 모든 로드된 모듈 딕셔너리
            SetAttribute("modules", new PythonDict());

            // sys.maxsize
            SetAttribute("maxsize", PythonInt.Create(int.MaxValue));

            // sys.version_info
            SetAttribute("version_info", CreateVersionInfo());

            // sys.executable
            SetAttribute("executable", new PythonString(
                System.Reflection.Assembly.GetExecutingAssembly().Location));

            // sys.stdin, stdout, stderr (간단한 구현)
            SetAttribute("stdin", new PythonString("<stdin>"));
            SetAttribute("stdout", new PythonString("<stdout>"));
            SetAttribute("stderr", new PythonString("<stderr>"));

            // sys 모듈 메서드들
            RegisterSysMethods();
        }

        private void RegisterSysMethods()
        {
            // sys.exit() 함수
            SetAttribute("exit", new BuiltinFunction("exit", args =>
            {
                int exitCode = 0;
                if (args.Count > 0 && NumberHelper.IsNumber(args[0]))
                    exitCode = NumberHelper.ToInt(args[0]);

                throw new PythonException("SystemExit", exitCode.ToString());
            }));

            // sys.getrecursionlimit()
            SetAttribute("getrecursionlimit", new BuiltinFunction("getrecursionlimit", args =>
            {
                if (args.Count != 0)
                    throw new PythonException("TypeError", "getrecursionlimit() takes no arguments");
                return PythonInt.Create(1000);  // Default Python recursion limit
            }));

            // sys.setrecursionlimit()
            SetAttribute("setrecursionlimit", new BuiltinFunction("setrecursionlimit", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "setrecursionlimit() takes exactly 1 argument");
                // 실제로는 설정하지 않고 None 반환
                return PythonNone.Instance;
            }));

            // sys.getsizeof() - 간단한 구현
            SetAttribute("getsizeof", new BuiltinFunction("getsizeof", args =>
            {
                if (args.Count < 1 || args.Count > 2)
                    throw new PythonException("TypeError", "getsizeof() takes 1 or 2 arguments");

                // 간단한 추정값 반환
                if (args[0] is PythonString str)
                    return PythonInt.Create(24 + str.Value.Length * 2);
                else if (args[0] is PythonList list)
                    return PythonInt.Create(40 + list.Items.Count * 8);
                else if (args[0] is PythonDict dict)
                    return PythonInt.Create(240 + dict.Items.Count * 24);
                else
                    return PythonInt.Create(24);  // 기본 객체 크기
            }));

            // sys.getrefcount() - 간단한 구현
            SetAttribute("getrefcount", new BuiltinFunction("getrefcount", args =>
            {
                if (args.Count != 1)
                    throw new PythonException("TypeError", "getrefcount() takes exactly 1 argument");
                // C#은 GC를 사용하므로 항상 2 반환 (더미 값)
                return PythonInt.Create(2);
            }));
        }

        private string GetPlatform()
        {
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Windows))
            {
                return "win32";
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.Linux))
            {
                return "linux";
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
            {
                return "darwin";
            }
            return "unknown";
        }

        private PythonTuple CreateVersionInfo()
        {
            var versionInfo = new PythonTuple();
            versionInfo.Items.Add(PythonInt.Create(3));      // major
            versionInfo.Items.Add(PythonInt.Create(9));      // minor
            versionInfo.Items.Add(PythonInt.Create(0));      // micro
            versionInfo.Items.Add(new PythonString("final")); // releaselevel
            versionInfo.Items.Add(PythonInt.Create(0));      // serial
            return versionInfo;
        }

        // sys.path를 List<string>로 가져오기
        public List<string> GetSearchPaths()
        {
            if (pathList != null)
            {
                return pathList.Items
                    .Where(item => item is PythonString)
                    .Select(item => ((PythonString)item).Value)
                    .ToList();
            }
            return new List<string> { "." };
        }

        // 로드된 모듈 업데이트
        public void RegisterModule(string name, PythonModule module)
        {
            var modules = GetAttribute("modules") as PythonDict;
            if (modules != null)
            {
                modules.Items[new PythonString(name)] = module;
            }
        }

        // 모듈이 로드되었는지 확인
        public bool IsModuleLoaded(string name)
        {
            var modules = GetAttribute("modules") as PythonDict;
            if (modules != null)
            {
                return modules.Items.ContainsKey(new PythonString(name));
            }
            return false;
        }

        // 로드된 모듈 가져오기
        public PythonModule GetLoadedModule(string name)
        {
            var modules = GetAttribute("modules") as PythonDict;
            if (modules != null)
            {
                var key = new PythonString(name);
                if (modules.Items.ContainsKey(key))
                {
                    return modules.Items[key] as PythonModule;
                }
            }
            return null;
        }

        // 모든 로드된 모듈 이름 가져오기
        public List<string> GetLoadedModuleNames()
        {
            var modules = GetAttribute("modules") as PythonDict;
            if (modules != null)
            {
                return modules.Items.Keys
                    .Where(key => key is PythonString)
                    .Select(key => ((PythonString)key).Value)
                    .ToList();
            }
            return new List<string>();
        }

        // sys.path에 경로 추가
        public void AddPath(string path)
        {
            if (pathList != null)
            {
                pathList.Items.Add(new PythonString(path));
            }
        }

        // sys.path에 경로 삽입
        public void InsertPath(int index, string path)
        {
            if (pathList != null)
            {
                pathList.Items.Insert(index, new PythonString(path));
            }
        }
    }
}