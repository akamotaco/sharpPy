using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

#if GODOT
using IOHelper = Godot_IO.Helper;
#else
using IOHelper = DotNet_IO.Helper;
#endif

namespace SharpPy
{
    /// <summary>
    /// SharpPy 전용 바이트코드 캐시 (import 속도 최적화)
    /// Parse+Compile 단계를 스킵하여 모듈 로딩 속도 ~50% 향상
    ///
    /// 포맷:
    ///   Header: "SPYC" (4B) + Version (4B) + EngineMVID (16B) + SourceTimestamp (8B) + SourceSize (4B)
    ///   Body: Serialized PyCodeObject (재귀적 — 중첩 함수/클래스 포함)
    ///
    /// CPython .pyc와 달리 SharpPy 내부 전용 포맷.
    /// CPython 호환이 필요하면 PycFileWriter/PyMarshal 사용.
    /// </summary>
    public static class SharpPyCache
    {
        private static readonly byte[] MAGIC = Encoding.ASCII.GetBytes("SPYC");
        private const int FORMAT_VERSION = 2;

        /// <summary>
        /// 엔진 빌드 해시 — SharpPy 자체가 변경되면 캐시 자동 무효화.
        /// CPython 3.12: MAGIC_NUMBER가 바이트코드 형식 변경 시 갱신되어 .pyc 무효화.
        /// SharpPy: 어셈블리 MVID(Module Version ID)를 사용하여
        /// 코드 변경 → 재빌드 → MVID 변경 → 캐시 자동 무효화.
        /// </summary>
        private static readonly Guid ENGINE_MVID =
            typeof(SharpPyCache).Assembly.ManifestModule.ModuleVersionId;

        // Cache directory name
        private const string CACHE_DIR = "__sharppy_cache__";

        /// <summary>
        /// .py 파일에 대응하는 캐시 파일 경로 반환
        /// </summary>
        public static string GetCachePath(string sourcePath)
        {
            var directory = IOHelper.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(directory)) directory = ".";
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
            return IOHelper.CombinePath(directory, CACHE_DIR, fileNameWithoutExt + ".spyc");
        }

        // Header: MAGIC(4) + VERSION(4) + ENGINE_MVID(16) + Timestamp(8) + Size(4) = 36 bytes
        private const int HEADER_SIZE = 36;

        /// <summary>
        /// 캐시가 유효한지 확인
        /// - Magic + Version: 포맷 호환성
        /// - Engine MVID: SharpPy 엔진 빌드 변경 감지 (CPython MAGIC_NUMBER 역할)
        /// - Timestamp + Size: 소스 파일 변경 감지
        /// </summary>
        public static bool IsCacheValid(string cachePath, string sourcePath)
        {
            if (!IOHelper.FileExists(cachePath)) return false;

            try
            {
                var cacheBytes = IOHelper.ReadAllBytes(cachePath);
                if (cacheBytes.Length < HEADER_SIZE) return false;

                using (var ms = new MemoryStream(cacheBytes, 0, HEADER_SIZE))
                using (var reader = new BinaryReader(ms))
                {
                    // Magic check
                    var magic = reader.ReadBytes(4);
                    if (magic[0] != MAGIC[0] || magic[1] != MAGIC[1]
                        || magic[2] != MAGIC[2] || magic[3] != MAGIC[3])
                        return false;

                    // Version check
                    if (reader.ReadInt32() != FORMAT_VERSION) return false;

                    // Engine MVID check — SharpPy 재빌드 시 자동 무효화
                    var cachedMvid = new Guid(reader.ReadBytes(16));
                    if (cachedMvid != ENGINE_MVID) return false;

                    // Timestamp check
                    long cachedTimestamp = reader.ReadInt64();
                    long sourceTimestamp = IOHelper.GetFileTimestamp(sourcePath);
                    if (cachedTimestamp != sourceTimestamp) return false;

                    // Size check
                    int cachedSize = reader.ReadInt32();
                    if (cachedSize != IOHelper.GetFileSize(sourcePath)) return false;

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// PyCodeObject를 캐시 파일에 저장
        /// IOHelper 사용하여 Godot(user://) 호환
        /// </summary>
        public static void WriteCache(string cachePath, PyCodeObject code, string sourcePath)
        {
            try
            {
                var directory = IOHelper.GetDirectoryName(cachePath);
                if (!string.IsNullOrEmpty(directory) && !IOHelper.DirExists(directory))
                    IOHelper.CreateDirectory(directory);

                long sourceTimestamp = IOHelper.GetFileTimestamp(sourcePath);
                int sourceSize = IOHelper.GetFileSize(sourcePath);

                using (var ms = new MemoryStream())
                using (var writer = new BinaryWriter(ms, Encoding.UTF8))
                {
                    // Header: MAGIC + VERSION + ENGINE_MVID + Timestamp + Size
                    writer.Write(MAGIC);
                    writer.Write(FORMAT_VERSION);
                    writer.Write(ENGINE_MVID.ToByteArray());
                    writer.Write(sourceTimestamp);
                    writer.Write(sourceSize);

                    // Body
                    WriteCodeObject(writer, code);

                    // Flush to file via IOHelper
                    IOHelper.WriteAllBytes(cachePath, ms.ToArray());
                }
            }
            catch
            {
                // 캐시 쓰기 실패는 무시 — 다음번에 다시 컴파일
                try { if (IOHelper.FileExists(cachePath)) IOHelper.DeleteFile(cachePath); } catch { }
            }
        }

        /// <summary>
        /// Lib/ 디렉토리의 모든 .py 파일을 사전 컴파일하여 .spyc 캐시 생성
        /// 게임 배포 시 첫 실행 속도 최적화에 사용
        /// </summary>
        public static void PrecompileDirectory(string directory)
        {
            if (!IOHelper.DirExists(directory))
            {
                Console.WriteLine($"Directory not found: {directory}");
                return;
            }

            int compiled = 0, skipped = 0, failed = 0;
            PrecompileRecursive(directory, ref compiled, ref skipped, ref failed);
            Console.WriteLine($"Precompile done: {compiled} compiled, {skipped} up-to-date, {failed} failed");
        }

        private static void PrecompileRecursive(string directory, ref int compiled, ref int skipped, ref int failed)
        {
            // Compile .py files in this directory
            var pyFiles = IOHelper.GetFiles(directory, ".py");
            foreach (var pyFile in pyFiles)
            {
                var cachePath = GetCachePath(pyFile);
                if (IsCacheValid(cachePath, pyFile))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var source = IOHelper.ReadAllText(pyFile);
                    var tokens = SharpPy.Generated.PyParserRuntime.LexerSource(source);
                    var statements = SharpPy.Generated.PyParserRuntime.ParseSource(tokens, source, pyFile);
                    var compiler = new PythonCompiler();
                    var code = compiler.Compile(statements, "<module>", new System.Collections.Generic.List<string>(), pyFile);
                    WriteCache(cachePath, code, pyFile);
                    compiled++;
                    Console.WriteLine($"  compiled: {pyFile}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"  FAILED:   {pyFile} — {ex.Message}");
                }
            }

            // Recurse into subdirectories
            var subDirs = IOHelper.GetDirectories(directory);
            foreach (var subDir in subDirs)
            {
                var dirName = Path.GetFileName(subDir);
                // Skip cache directories and hidden directories
                if (dirName.StartsWith("__") || dirName.StartsWith("."))
                    continue;
                PrecompileRecursive(subDir, ref compiled, ref skipped, ref failed);
            }
        }

        /// <summary>
        /// 캐시 파일에서 PyCodeObject 로드
        /// IOHelper 사용하여 Godot(res://) 호환
        /// </summary>
        public static PyCodeObject ReadCache(string cachePath)
        {
            var data = IOHelper.ReadAllBytes(cachePath);
            using (var ms = new MemoryStream(data))
            using (var reader = new BinaryReader(ms, Encoding.UTF8))
            {
                // Skip header (already validated by IsCacheValid)
                reader.ReadBytes(4);   // magic
                reader.ReadInt32();    // version
                reader.ReadBytes(16);  // engine MVID
                reader.ReadInt64();    // timestamp
                reader.ReadInt32();    // size

                // Body
                return ReadCodeObject(reader);
            }
        }

        // ─── Serialization ───

        private static void WriteCodeObject(BinaryWriter w, PyCodeObject code)
        {
            // Scalar fields
            WriteStr(w, code.Name);
            WriteNullableStr(w, code.FileName);
            w.Write(code.ArgCount);
            w.Write(code.PosonlyArgCount);
            w.Write(code.KwonlyArgCount);
            w.Write(code.Flags);
            w.Write(code.IsOptimized);

            // String lists
            WriteStringList(w, code.Names);
            WriteStringList(w, code.VarNames);
            WriteStringList(w, code.FreeVars);
            WriteStringList(w, code.CellVars);

            // Instructions — FileName dedup: most instructions share code.FileName
            w.Write(code.Instructions.Count);
            foreach (var instr in code.Instructions)
            {
                w.Write((int)instr.OpCode);
                w.Write(instr.Argument);
                w.Write(instr.LineNumber);
                w.Write(instr.ColumnOffset);
                // Dedup: 0=same as code.FileName, 1=null, 2=different
                if (instr.FileName == code.FileName)
                    w.Write((byte)0);
                else if (instr.FileName == null)
                    w.Write((byte)1);
                else
                {
                    w.Write((byte)2);
                    WriteStr(w, instr.FileName);
                }
            }

            // Constants (재귀 — PyCodeObject 포함 가능)
            w.Write(code.Constants.Count);
            foreach (var c in code.Constants)
                WriteConstant(w, c);

            // Default values
            w.Write(code.DefaultValues.Count);
            foreach (var d in code.DefaultValues)
                WriteConstant(w, d);

            w.Write(code.KwDefaults.Count);
            foreach (var kd in code.KwDefaults)
                WriteConstant(w, kd);

            // Exception table
            w.Write(code.ExceptionTable.Count);
            foreach (var entry in code.ExceptionTable)
            {
                w.Write(entry.StartOffset);
                w.Write(entry.EndOffset);
                w.Write(entry.HandlerOffset);
                w.Write(entry.Depth);
                w.Write(entry.Lasti);
            }

            // Line number table
            w.Write(code.LineNumberTable.Count);
            foreach (var kv in code.LineNumberTable)
            {
                w.Write(kv.Key);
                w.Write(kv.Value);
            }

            // Source lines: skip serialization — too large for nested code objects
            // (each nested function/class duplicates the entire source)
            // SourceLines can be lazily loaded from source file at runtime if needed.
        }

        private static PyCodeObject ReadCodeObject(BinaryReader r)
        {
            // Scalar fields
            string name = ReadStr(r);
            string fileName = ReadNullableStr(r);
            int argCount = r.ReadInt32();
            int posonlyArgCount = r.ReadInt32();
            int kwonlyArgCount = r.ReadInt32();
            int flags = r.ReadInt32();
            bool isOptimized = r.ReadBoolean();

            // String lists
            var names = ReadStringList(r);
            var varNames = ReadStringList(r);
            var freeVars = ReadStringList(r);
            var cellVars = ReadStringList(r);

            // Instructions — FileName dedup
            int instrCount = r.ReadInt32();
            var instructions = new List<ByteCodeInstruction>(instrCount);
            for (int i = 0; i < instrCount; i++)
            {
                var opCode = (ByteCodeOp)r.ReadInt32();
                int arg = r.ReadInt32();
                int lineNo = r.ReadInt32();
                int colOffset = r.ReadInt32();
                byte fnFlag = r.ReadByte();
                string instrFileName = fnFlag == 0 ? fileName : (fnFlag == 1 ? null : ReadStr(r));
                instructions.Add(new ByteCodeInstruction(opCode, arg, lineNo, colOffset, instrFileName));
            }

            // Constants
            int constCount = r.ReadInt32();
            var constants = new List<PyObject>(constCount);
            for (int i = 0; i < constCount; i++)
                constants.Add(ReadConstant(r));

            // Default values
            int defaultCount = r.ReadInt32();
            var defaultValues = new List<PyObject>(defaultCount);
            for (int i = 0; i < defaultCount; i++)
                defaultValues.Add(ReadConstant(r));

            int kwDefaultCount = r.ReadInt32();
            var kwDefaults = new List<PyObject>(kwDefaultCount);
            for (int i = 0; i < kwDefaultCount; i++)
                kwDefaults.Add(ReadConstant(r));

            // Exception table
            int excCount = r.ReadInt32();
            var exceptionTable = new List<ExceptionTableEntry>(excCount);
            for (int i = 0; i < excCount; i++)
            {
                int start = r.ReadInt32();
                int end = r.ReadInt32();
                int handler = r.ReadInt32();
                int depth = r.ReadInt32();
                bool lasti = r.ReadBoolean();
                exceptionTable.Add(new ExceptionTableEntry(start, end, handler, depth, lasti));
            }

            // Line number table
            int lineTableCount = r.ReadInt32();
            var lineNumberTable = new Dictionary<int, int>(lineTableCount);
            for (int i = 0; i < lineTableCount; i++)
            {
                int key = r.ReadInt32();
                int val = r.ReadInt32();
                lineNumberTable[key] = val;
            }

            // Source lines: not stored in cache (too large with nested code objects)
            List<string> sourceLines = null;

            // Construct — constructor rebuilds all cached fields
            return new PyCodeObject(
                name, instructions, constants, names, varNames,
                argCount, posonlyArgCount, kwonlyArgCount,
                freeVars, cellVars, defaultValues, kwDefaults,
                flags, fileName, sourceLines, isOptimized,
                lineNumberTable, exceptionTable
            );
        }

        // ─── Constant serialization ───

        private const byte CONST_NONE = 0;
        private const byte CONST_TRUE = 1;
        private const byte CONST_FALSE = 2;
        private const byte CONST_INT = 3;
        private const byte CONST_FLOAT = 4;
        private const byte CONST_STR = 5;
        private const byte CONST_BYTES = 6;
        private const byte CONST_TUPLE = 7;
        private const byte CONST_CODE = 8;
        private const byte CONST_ELLIPSIS = 9;
        private const byte CONST_FROZENSET = 10;
        private const byte CONST_COMPLEX = 11;
        private const byte CONST_LONG_INT = 12;

        private static void WriteConstant(BinaryWriter w, PyObject obj)
        {
            if (obj == null || obj is PyNone)
            {
                w.Write(CONST_NONE);
            }
            else if (obj is PyBool b)
            {
                w.Write(b.Value ? CONST_TRUE : CONST_FALSE);
            }
            else if (obj is PyInt pyInt)
            {
                var bigVal = pyInt.Value;
                if (bigVal >= long.MinValue && bigVal <= long.MaxValue)
                {
                    w.Write(CONST_INT);
                    w.Write((long)bigVal);
                }
                else
                {
                    // BigInteger: serialize as byte array
                    w.Write(CONST_LONG_INT);
                    var bytes = bigVal.ToByteArray();
                    w.Write(bytes.Length);
                    w.Write(bytes);
                }
            }
            else if (obj is PyFloat pyFloat)
            {
                w.Write(CONST_FLOAT);
                w.Write(pyFloat.Value);
            }
            else if (obj is PyStr pyStr)
            {
                w.Write(CONST_STR);
                WriteStr(w, pyStr.Value);
            }
            else if (obj is PyBytes pyBytes)
            {
                w.Write(CONST_BYTES);
                w.Write(pyBytes.Value.Length);
                w.Write(pyBytes.Value);
            }
            else if (obj is PyTuple pyTuple)
            {
                w.Write(CONST_TUPLE);
                w.Write(pyTuple.Items.Length);
                foreach (var item in pyTuple.Items)
                    WriteConstant(w, item);
            }
            else if (obj is PyCodeObject code)
            {
                w.Write(CONST_CODE);
                WriteCodeObject(w, code);
            }
            else if (obj is PyEllipsis)
            {
                w.Write(CONST_ELLIPSIS);
            }
            else if (obj is PyFrozenSet frozenSet)
            {
                w.Write(CONST_FROZENSET);
                var items = frozenSet.Items;
                w.Write(items.Count);
                foreach (var item in items)
                    WriteConstant(w, item);
            }
            else
            {
                // Fallback: store as None
                w.Write(CONST_NONE);
            }
        }

        private static PyObject ReadConstant(BinaryReader r)
        {
            byte tag = r.ReadByte();
            switch (tag)
            {
                case CONST_NONE: return PyNone.Instance;
                case CONST_TRUE: return PyBool.True;
                case CONST_FALSE: return PyBool.False;
                case CONST_INT: return new PyInt(r.ReadInt64());
                case CONST_FLOAT: return new PyFloat(r.ReadDouble());
                case CONST_STR: return new PyStr(ReadStr(r));
                case CONST_BYTES:
                {
                    int len = r.ReadInt32();
                    return new PyBytes(r.ReadBytes(len));
                }
                case CONST_TUPLE:
                {
                    int len = r.ReadInt32();
                    var items = new PyObject[len];
                    for (int i = 0; i < len; i++)
                        items[i] = ReadConstant(r);
                    return new PyTuple(items);
                }
                case CONST_CODE: return ReadCodeObject(r);
                case CONST_ELLIPSIS: return PyEllipsis.Instance;
                case CONST_FROZENSET:
                {
                    int len = r.ReadInt32();
                    var items = new HashSet<PyObject>();
                    for (int i = 0; i < len; i++)
                        items.Add(ReadConstant(r));
                    return new PyFrozenSet(items);
                }
                case CONST_LONG_INT:
                {
                    int len = r.ReadInt32();
                    var bytes = r.ReadBytes(len);
                    return new PyInt(new System.Numerics.BigInteger(bytes));
                }
                default: return PyNone.Instance;
            }
        }

        // ─── Helpers ───

        private static void WriteStr(BinaryWriter w, string s)
        {
            var bytes = Encoding.UTF8.GetBytes(s ?? "");
            w.Write(bytes.Length);
            w.Write(bytes);
        }

        private static string ReadStr(BinaryReader r)
        {
            int len = r.ReadInt32();
            if (len == 0) return "";
            return Encoding.UTF8.GetString(r.ReadBytes(len));
        }

        private static void WriteNullableStr(BinaryWriter w, string s)
        {
            if (s == null)
            {
                w.Write(false);
            }
            else
            {
                w.Write(true);
                WriteStr(w, s);
            }
        }

        private static string ReadNullableStr(BinaryReader r)
        {
            return r.ReadBoolean() ? ReadStr(r) : null;
        }

        private static void WriteStringList(BinaryWriter w, List<string> list)
        {
            w.Write(list.Count);
            foreach (var s in list)
                WriteStr(w, s);
        }

        private static List<string> ReadStringList(BinaryReader r)
        {
            int count = r.ReadInt32();
            var list = new List<string>(count);
            for (int i = 0; i < count; i++)
                list.Add(ReadStr(r));
            return list;
        }
    }
}
