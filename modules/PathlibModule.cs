using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// Python pathlib 모듈 구현 - 현대적 객체지향 경로 처리
    /// </summary>
    public static class PathlibModule
    {
        public static PyModule CreatePathlibModule()
        {
            var module = new PyModule("pathlib", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\pathlib.py");

            // 핵심 클래스들
            module.ModuleDict["PurePath"] = new PyPathlibType("PurePath", typeof(PyPurePath));
            module.ModuleDict["Path"] = new PyPathlibType("Path", typeof(PyPath));
            
            // 플랫폼별 Path 클래스들
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                module.ModuleDict["WindowsPath"] = new PyPathlibType("WindowsPath", typeof(PyPath));
                module.ModuleDict["PosixPath"] = null; // Windows에서는 사용 불가
            }
            else
            {
                module.ModuleDict["PosixPath"] = new PyPathlibType("PosixPath", typeof(PyPath));
                module.ModuleDict["WindowsPath"] = null; // POSIX에서는 사용 불가
            }

            return module;
        }
    }

    /// <summary>
    /// pathlib 타입을 위한 PyType 래퍼
    /// </summary>
    public class PyPathlibType : PyType
    {
        private readonly Type _implementationType;

        public PyPathlibType(string name, Type implementationType) : base(name, new PyType[0])
        {
            _implementationType = implementationType;
        }

        public override PyObject Call(params PyObject[] args)
        {
            if (_implementationType == typeof(PyPurePath))
            {
                return new PyPurePath(args);
            }
            else if (_implementationType == typeof(PyPath))
            {
                return new PyPath(args);
            }
            
            throw PyTypeError.Create($"Cannot instantiate {Name}");
        }
    }

    /// <summary>
    /// PurePath - 순수 계산용 경로 클래스 (파일시스템 접근 없음)
    /// </summary>
    public class PyPurePath : PyObject
    {
        protected string[] _parts;
        protected bool _isAbsolute;
        protected string _pathString;

        public PyPurePath(PyObject[] args)
        {
            if (args.Length == 0)
            {
                _parts = new[] { "." };
                _isAbsolute = false;
            }
            else if (args.Length == 1 && args[0] is PyString pathStr)
            {
                InitFromString(pathStr.Value);
            }
            else
            {
                // 여러 경로 세그먼트 결합
                var pathParts = new List<string>();
                foreach (var arg in args)
                {
                    if (arg is PyString str)
                        pathParts.Add(str.Value);
                    else if (arg is PyPurePath path)
                        pathParts.Add(path._pathString);
                    else
                        pathParts.Add(arg.ToStr());
                }
                InitFromString(Path.Combine(pathParts.ToArray()));
            }

            _pathString = NormalizePath();
        }

        protected virtual void InitFromString(string pathString)
        {
            if (string.IsNullOrEmpty(pathString))
            {
                pathString = ".";
            }

            _isAbsolute = Path.IsPathRooted(pathString);
            
            // 경로를 부분으로 나누기
            var normalized = pathString.Replace('/', Path.DirectorySeparatorChar)
                                     .Replace('\\', Path.DirectorySeparatorChar);
            
            _parts = normalized.Split(new[] { Path.DirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            
            // 절대 경로인 경우 루트 정보 보존
            if (_isAbsolute)
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    // Windows: C: 형태로 드라이브 정보 보존
                    var root = Path.GetPathRoot(pathString);
                    if (!string.IsNullOrEmpty(root))
                    {
                        _parts = new[] { root.TrimEnd('\\') }.Concat(_parts.Skip(_parts[0] == root.TrimEnd('\\') ? 1 : 0)).ToArray();
                    }
                }
                else
                {
                    // POSIX: / 루트 표시
                    _parts = new[] { "" }.Concat(_parts).ToArray();
                }
            }
        }

        protected virtual string NormalizePath()
        {
            if (_parts.Length == 0)
                return ".";

            if (_isAbsolute)
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    return string.Join(Path.DirectorySeparatorChar.ToString(), _parts);
                }
                else
                {
                    return "/" + string.Join("/", _parts.Skip(1));
                }
            }
            else
            {
                return string.Join(Path.DirectorySeparatorChar.ToString(), _parts);
            }
        }

        public override PyType GetPyType() => PyType.ObjectType;
        public override string GetTypeName() => "PurePath";

        public override string ToString() => _pathString;
        public override string ToStr() => _pathString;
        public override string ToRepr() => $"{GetTypeName()}('{_pathString}')";

        // PurePath 속성들
        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                case "parts":
                    return new PyTuple(_parts.Select(p => new PyString(p)).Cast<PyObject>().ToArray());
                
                case "name":
                    return new PyString(_parts.Length > 0 ? _parts[_parts.Length - 1] : "");
                
                case "stem":
                    var fileName = _parts.Length > 0 ? _parts[_parts.Length - 1] : "";
                    var dotIndex = fileName.LastIndexOf('.');
                    return new PyString(dotIndex > 0 ? fileName.Substring(0, dotIndex) : fileName);
                
                case "suffix":
                    var suffixFileName = _parts.Length > 0 ? _parts[_parts.Length - 1] : "";
                    var lastDot = suffixFileName.LastIndexOf('.');
                    return new PyString(lastDot > 0 ? suffixFileName.Substring(lastDot) : "");
                
                case "parent":
                    if (_parts.Length <= 1)
                    {
                        return _isAbsolute ? new PyPurePath(new PyObject[] { new PyString(Path.GetPathRoot(_pathString) ?? "/") }) 
                                          : new PyPurePath(new PyObject[] { new PyString("..") });
                    }
                    var parentParts = _parts.Take(_parts.Length - 1).ToArray();
                    var parentPath = _isAbsolute ? 
                        (Environment.OSVersion.Platform == PlatformID.Win32NT ? 
                            string.Join(Path.DirectorySeparatorChar.ToString(), parentParts) :
                            "/" + string.Join("/", parentParts.Skip(1))) :
                        string.Join(Path.DirectorySeparatorChar.ToString(), parentParts);
                    return new PyPurePath(new PyObject[] { new PyString(parentPath) });
                
                case "is_absolute":
                    return new PyBuiltinFunction("is_absolute", args => PyBool.FromBool(_isAbsolute));
                
                case "joinpath":
                    return new PyBuiltinFunction("joinpath", JoinPath);
                
                case "with_name":
                    return new PyBuiltinFunction("with_name", WithName);
                
                case "with_suffix":
                    return new PyBuiltinFunction("with_suffix", WithSuffix);
                
                default:
                    return base.GetAttribute(name);
            }
        }

        protected PyObject JoinPath(PyObject[] args)
        {
            var allParts = new List<string>(_parts);
            
            foreach (var arg in args)
            {
                if (arg is PyString str)
                {
                    allParts.AddRange(str.Value.Split(new[] { Path.DirectorySeparatorChar, '/' }, StringSplitOptions.RemoveEmptyEntries));
                }
                else if (arg is PyPurePath path)
                {
                    allParts.AddRange(path._parts);
                }
            }
            
            var combinedPath = _isAbsolute ? 
                (Environment.OSVersion.Platform == PlatformID.Win32NT ?
                    string.Join(Path.DirectorySeparatorChar.ToString(), allParts) :
                    "/" + string.Join("/", allParts.Skip(1))) :
                string.Join(Path.DirectorySeparatorChar.ToString(), allParts);
            
            return new PyPurePath(new PyObject[] { new PyString(combinedPath) });
        }

        protected PyObject WithName(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"with_name() takes exactly 1 argument ({args.Length} given)");
            
            var newName = args[0].ToStr();
            
            if (_parts.Length == 0)
                throw PyValueError.Create("Empty path has no name");
            
            var newParts = _parts.Take(_parts.Length - 1).Concat(new[] { newName }).ToArray();
            var newPath = _isAbsolute ?
                (Environment.OSVersion.Platform == PlatformID.Win32NT ?
                    string.Join(Path.DirectorySeparatorChar.ToString(), newParts) :
                    "/" + string.Join("/", newParts.Skip(1))) :
                string.Join(Path.DirectorySeparatorChar.ToString(), newParts);
            
            return new PyPurePath(new PyObject[] { new PyString(newPath) });
        }

        protected PyObject WithSuffix(PyObject[] args)
        {
            if (args.Length != 1)
                throw PyTypeError.Create($"with_suffix() takes exactly 1 argument ({args.Length} given)");
            
            var newSuffix = args[0].ToStr();
            var currentFileName = _parts.Length > 0 ? _parts[_parts.Length - 1] : "";
            
            var dotIndex = currentFileName.LastIndexOf('.');
            var stem = dotIndex > 0 ? currentFileName.Substring(0, dotIndex) : currentFileName;
            var newName = stem + newSuffix;
            
            return WithName(new PyObject[] { new PyString(newName) });
        }

        // 연산자 오버로딩
        public override PyObject Add(PyObject other)
        {
            if (other is PyString str)
                return JoinPath(new PyObject[] { str });
            if (other is PyPurePath path)
                return JoinPath(new PyObject[] { path });
            
            throw PyTypeError.Create($"unsupported operand type(s) for +: 'PurePath' and '{other.GetTypeName()}'");
        }

        public override PyObject Divide(PyObject other)
        {
            return Add(other); // / 연산자는 + 와 동일하게 작동
        }

        public override bool Equals(object obj)
        {
            return obj is PyPurePath other && _pathString == other._pathString;
        }

        public override int GetHashCode()
        {
            return _pathString.GetHashCode();
        }
    }

    /// <summary>
    /// Path - 실제 파일시스템 조작 기능을 포함한 경로 클래스
    /// </summary>
    public class PyPath : PyPurePath
    {
        public PyPath(PyObject[] args) : base(args) { }

        public override string GetTypeName() => "Path";

        public override PyObject GetAttribute(string name)
        {
            switch (name)
            {
                // 파일시스템 조작 메서드들
                case "exists":
                    return new PyBuiltinFunction("exists", args => PyBool.FromBool(Exists()));
                
                case "is_file":
                    return new PyBuiltinFunction("is_file", args => PyBool.FromBool(IsFile()));
                
                case "is_dir":
                    return new PyBuiltinFunction("is_dir", args => PyBool.FromBool(IsDirectory()));
                
                case "mkdir":
                    return new PyBuiltinFunction("mkdir", MkDir);
                
                case "touch":
                    return new PyBuiltinFunction("touch", Touch);
                
                case "unlink":
                    return new PyBuiltinFunction("unlink", Unlink);
                
                case "rmdir":
                    return new PyBuiltinFunction("rmdir", RmDir);
                
                case "read_text":
                    return new PyBuiltinFunction("read_text", ReadText);
                
                case "write_text":
                    return new PyBuiltinFunction("write_text", WriteText);
                
                case "read_bytes":
                    return new PyBuiltinFunction("read_bytes", ReadBytes);
                
                case "write_bytes":
                    return new PyBuiltinFunction("write_bytes", WriteBytes);
                
                case "iterdir":
                    return new PyBuiltinFunction("iterdir", IterDir);
                
                case "glob":
                    return new PyBuiltinFunction("glob", Glob);
                
                case "resolve":
                    return new PyBuiltinFunction("resolve", Resolve);
                
                case "stat":
                    return new PyBuiltinFunction("stat", Stat);
                
                default:
                    return base.GetAttribute(name);
            }
        }

        // 파일시스템 조작 메서드 구현들
        private bool Exists()
        {
            return File.Exists(_pathString) || Directory.Exists(_pathString);
        }

        private bool IsFile()
        {
            return File.Exists(_pathString);
        }

        private bool IsDirectory()
        {
            return Directory.Exists(_pathString);
        }

        private PyObject MkDir(PyObject[] args)
        {
            bool parents = false;
            bool existOk = false;

            // 키워드 인자 처리 (간단화)
            foreach (var arg in args)
            {
                if (arg is PyBool boolArg)
                {
                    if (!parents) parents = boolArg.Value;
                    else existOk = boolArg.Value;
                }
            }

            try
            {
                if (parents)
                    Directory.CreateDirectory(_pathString);
                else
                    Directory.CreateDirectory(_pathString);

                return PyNone.Instance;
            }
            catch (Exception ex) when (!existOk || !Directory.Exists(_pathString))
            {
                throw PyOSError.Create($"mkdir failed: {ex.Message}");
            }
            
            return PyNone.Instance;
        }

        private PyObject Touch(PyObject[] args)
        {
            try
            {
                if (!File.Exists(_pathString))
                {
                    File.Create(_pathString).Close();
                }
                else
                {
                    File.SetLastWriteTime(_pathString, DateTime.Now);
                }
                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"touch failed: {ex.Message}");
            }
        }

        private PyObject Unlink(PyObject[] args)
        {
            try
            {
                if (File.Exists(_pathString))
                    File.Delete(_pathString);
                else if (Directory.Exists(_pathString))
                    throw PyIsADirectoryError.Create($"Is a directory: '{_pathString}'");
                else
                    throw PyFileNotFoundError.Create($"No such file: '{_pathString}'");
                
                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"unlink failed: {ex.Message}");
            }
        }

        private PyObject RmDir(PyObject[] args)
        {
            try
            {
                if (Directory.Exists(_pathString))
                    Directory.Delete(_pathString, false);
                else
                    throw PyFileNotFoundError.Create($"No such directory: '{_pathString}'");
                
                return PyNone.Instance;
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"rmdir failed: {ex.Message}");
            }
        }

        private PyObject ReadText(PyObject[] args)
        {
            try
            {
                var encoding = "utf-8"; // 기본 인코딩
                
                if (args.Length > 0 && args[0] is PyString enc)
                    encoding = enc.Value;

                var content = File.ReadAllText(_pathString, Encoding.UTF8);
                return new PyString(content);
            }
            catch (FileNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file: '{_pathString}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"read_text failed: {ex.Message}");
            }
        }

        private PyObject WriteText(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("write_text() missing 1 required positional argument: 'data'");

            try
            {
                var content = args[0].ToStr();
                File.WriteAllText(_pathString, content, Encoding.UTF8);
                return new PyInt(content.Length);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"write_text failed: {ex.Message}");
            }
        }

        private PyObject ReadBytes(PyObject[] args)
        {
            try
            {
                var bytes = File.ReadAllBytes(_pathString);
                return new PyBytes(bytes);
            }
            catch (FileNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such file: '{_pathString}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"read_bytes failed: {ex.Message}");
            }
        }

        private PyObject WriteBytes(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("write_bytes() missing 1 required positional argument: 'data'");

            try
            {
                if (args[0] is PyBytes bytes)
                {
                    File.WriteAllBytes(_pathString, bytes.Value);
                    return new PyInt(bytes.Value.Length);
                }
                else
                {
                    var content = args[0].ToStr();
                    var byteArray = Encoding.UTF8.GetBytes(content);
                    File.WriteAllBytes(_pathString, byteArray);
                    return new PyInt(byteArray.Length);
                }
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"write_bytes failed: {ex.Message}");
            }
        }

        private PyObject IterDir(PyObject[] args)
        {
            try
            {
                if (!Directory.Exists(_pathString))
                    throw PyNotADirectoryError.Create($"Not a directory: '{_pathString}'");

                var entries = Directory.GetFileSystemEntries(_pathString)
                    .Select(entry => new PyPath(new PyObject[] { new PyString(entry) }))
                    .Cast<PyObject>()
                    .ToArray();

                return new PyList(entries);
            }
            catch (DirectoryNotFoundException)
            {
                throw PyFileNotFoundError.Create($"No such directory: '{_pathString}'");
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"iterdir failed: {ex.Message}");
            }
        }

        private PyObject Glob(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("glob() missing 1 required positional argument: 'pattern'");

            try
            {
                var pattern = args[0].ToStr();
                var searchPath = Directory.Exists(_pathString) ? _pathString : Path.GetDirectoryName(_pathString);
                var searchPattern = Directory.Exists(_pathString) ? pattern : Path.GetFileName(_pathString);

                var matches = Directory.GetFiles(searchPath ?? ".", searchPattern, SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetDirectories(searchPath ?? ".", searchPattern, SearchOption.TopDirectoryOnly))
                    .Select(match => new PyPath(new PyObject[] { new PyString(match) }))
                    .Cast<PyObject>()
                    .ToArray();

                return new PyList(matches);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"glob failed: {ex.Message}");
            }
        }

        private PyObject Resolve(PyObject[] args)
        {
            try
            {
                var resolved = Path.GetFullPath(_pathString);
                return new PyPath(new PyObject[] { new PyString(resolved) });
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"resolve failed: {ex.Message}");
            }
        }

        private PyObject Stat(PyObject[] args)
        {
            try
            {
                FileSystemInfo info;
                
                if (File.Exists(_pathString))
                    info = new FileInfo(_pathString);
                else if (Directory.Exists(_pathString))
                    info = new DirectoryInfo(_pathString);
                else
                    throw PyFileNotFoundError.Create($"No such file or directory: '{_pathString}'");
                    
                return new PyStatResult(info);
            }
            catch (Exception ex)
            {
                throw PyOSError.Create($"stat failed: {ex.Message}");
            }
        }
    }

    #region Additional Exception Types

    /// <summary>
    /// Python IsADirectoryError
    /// </summary>
    public class PyIsADirectoryError : PyOSError
    {
        public PyIsADirectoryError(string message) : base(message) { }
        public override string GetTypeName() => "IsADirectoryError";

        public new static PythonException Create(string message)
        {
            return new PythonException(new PyIsADirectoryError(message));
        }
    }

    /// <summary>
    /// Python NotADirectoryError
    /// </summary>
    public class PyNotADirectoryError : PyOSError
    {
        public PyNotADirectoryError(string message) : base(message) { }
        public override string GetTypeName() => "NotADirectoryError";

        public new static PythonException Create(string message)
        {
            return new PythonException(new PyNotADirectoryError(message));
        }
    }

    #endregion
}