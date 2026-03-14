#if GODOT

using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Godot_IO
{
    public class Helper
    {
        public static string[] GetFiles(string path, string extension = "")
        {
            var files = new List<string>();

            using (var dir = Godot.DirAccess.Open(path))
            {
                if (dir != null)
                {
                    dir.ListDirBegin();
                    string fileName = dir.GetNext();

                    while (!string.IsNullOrEmpty(fileName))
                    {
                        if (!dir.CurrentIsDir())
                        {
                            if (string.IsNullOrEmpty(extension) || fileName.EndsWith(extension))
                            {
                                files.Add(path + "/" + fileName);
                            }
                        }
                        fileName = dir.GetNext();
                    }
                    dir.ListDirEnd();
                }
            }

            return files.ToArray();
        }

        public static string[] GetDirectories(string path)
        {
            var directories = new List<string>();

            using (var dir = DirAccess.Open(path))
            {
                if (dir != null)
                {
                    dir.ListDirBegin();
                    string dirName = dir.GetNext();

                    while (!string.IsNullOrEmpty(dirName))
                    {
                        if (dir.CurrentIsDir() && dirName != "." && dirName != "..")
                        {
                            directories.Add(path + "/" + dirName);
                        }
                        dirName = dir.GetNext();
                    }
                    dir.ListDirEnd();
                }
            }

            return directories.ToArray();
        }

        public static bool FileExists(string path)
        {
            return Godot.FileAccess.FileExists(path);
        }

        public static bool DirExists(string path)
        {
            // res://, user:// 경로는 DirAccess.Open으로 확인
            if (IsGodotPath(path))
            {
                using (var dir = DirAccess.Open(path))
                {
                    return dir != null;
                }
            }
            return Godot.DirAccess.DirExistsAbsolute(path);
        }
        
        // 파일 내용 읽기
        public static string ReadAllText(string path)
        {
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
            {
                if (file != null && file.IsOpen())
                {
                    string content = file.GetAsText();
                    file.Close();
                    return content;
                }
                else
                {
                    throw new System.IO.FileNotFoundException($"Could not open file: {path}");
                }
            }
        }
        
        // 바이너리 파일 읽기/쓰기 (SharpPyCache용)
        public static byte[] ReadAllBytes(string path)
        {
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
            {
                if (file != null && file.IsOpen())
                {
                    var length = (long)file.GetLength();
                    var data = file.GetBuffer(length);
                    file.Close();
                    return data;
                }
                else
                {
                    throw new System.IO.FileNotFoundException($"Could not open file: {path}");
                }
            }
        }

        public static void WriteAllBytes(string path, byte[] data)
        {
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
            {
                if (file != null && file.IsOpen())
                {
                    file.StoreBuffer(data);
                    file.Close();
                }
                else
                {
                    throw new System.IO.IOException($"Could not write to file: {path}");
                }
            }
        }

        public static long GetFileTimestamp(string path)
        {
            // Godot doesn't have direct file timestamp API — use FileAccess.GetModifiedTime
            var modifiedTime = Godot.FileAccess.GetModifiedTime(path);
            return (long)modifiedTime * 1000; // seconds to milliseconds
        }

        public static int GetFileSize(string path)
        {
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
            {
                if (file != null && file.IsOpen())
                {
                    var size = (int)file.GetLength();
                    file.Close();
                    return size;
                }
                return 0;
            }
        }

        // 경로 결합 - 항상 슬래시 사용
        public static string CombinePath(params string[] paths)
        {
            if (paths == null || paths.Length == 0)
                return "";
            
            // Godot은 항상 슬래시를 사용하므로 단순히 결합
            var combined = string.Join("/", paths.Where(p => !string.IsNullOrEmpty(p)));
            
            // 중복 슬래시 제거 (res:// 와 user:// 는 유지)
            while (combined.Contains("//") && 
                   !combined.Contains("res://") && 
                   !combined.Contains("user://"))
            {
                combined = combined.Replace("//", "/");
            }
            
            // res:// 또는 user:// 프로토콜 정리
            if (combined.Contains("res://"))
            {
                combined = combined.Replace("res://", "GODOT_RES_TEMP");
                combined = combined.Replace("//", "/");
                combined = combined.Replace("GODOT_RES_TEMP", "res://");
            }
            if (combined.Contains("user://"))
            {
                combined = combined.Replace("user://", "GODOT_USER_TEMP");
                combined = combined.Replace("//", "/");
                combined = combined.Replace("GODOT_USER_TEMP", "user://");
            }
            
            return combined;
        }
        
        // 대소문자 정확한 파일 존재 확인
        public static bool FileExistsExact(string filePath)
        {
            if (!FileExists(filePath))
                return false;
            
            try
            {
                // Godot String 메서드 사용
                string directory = filePath.GetBaseDir();
                string fileName = filePath.GetFile();
                
                if (string.IsNullOrEmpty(directory))
                    directory = ".";
                
                // Godot 방식으로 디렉토리 내 파일 목록 가져오기
                using (var dir = DirAccess.Open(directory))
                {
                    if (dir != null)
                    {
                        dir.ListDirBegin();
                        string currentFile = dir.GetNext();
                        
                        while (!string.IsNullOrEmpty(currentFile))
                        {
                            if (!dir.CurrentIsDir() && currentFile == fileName)
                            {
                                dir.ListDirEnd();
                                return true;
                            }
                            currentFile = dir.GetNext();
                        }
                        dir.ListDirEnd();
                    }
                }
                
                return false;
            }
            catch
            {
                // 오류 발생 시 기본 FileExists 결과 반환
                return FileExists(filePath);
            }
        }
        
        // 대소문자 정확한 디렉토리 존재 확인
        public static bool DirExistsExact(string dirPath)
        {
            if (!DirExists(dirPath))
                return false;
            
            try
            {
                // Godot String 메서드 사용
                string parentDir = dirPath.GetBaseDir();
                string dirName = dirPath.GetFile();
                
                // 디렉토리 경로가 "/"로 끝나는 경우 처리
                if (string.IsNullOrEmpty(dirName) && dirPath.EndsWith("/"))
                {
                    dirPath = dirPath.TrimSuffix("/");
                    parentDir = dirPath.GetBaseDir();
                    dirName = dirPath.GetFile();
                }
                
                if (string.IsNullOrEmpty(parentDir))
                    parentDir = ".";
                
                // Godot 방식으로 디렉토리 목록 가져오기
                using (var dir = DirAccess.Open(parentDir))
                {
                    if (dir != null)
                    {
                        dir.ListDirBegin();
                        string currentDir = dir.GetNext();
                        
                        while (!string.IsNullOrEmpty(currentDir))
                        {
                            if (dir.CurrentIsDir() && currentDir == dirName)
                            {
                                dir.ListDirEnd();
                                return true;
                            }
                            currentDir = dir.GetNext();
                        }
                        dir.ListDirEnd();
                    }
                }
                
                return false;
            }
            catch
            {
                // 오류 발생 시 기본 DirExists 결과 반환
                return DirExists(dirPath);
            }
        }
        
        // 추가 유틸리티: 경로가 Godot 프로토콜인지 확인
        public static bool IsGodotPath(string path)
        {
            return path != null && (path.StartsWith("res://") || path.StartsWith("user://"));
        }
        
        // 추가 유틸리티: 절대 경로 변환
        public static string GetAbsolutePath(string path)
        {
            if (IsGodotPath(path))
                return path; // Godot 경로는 그대로 반환
            
            // 상대 경로를 절대 경로로 변환
            using (var dir = DirAccess.Open("."))
            {
                if (dir != null)
                {
                    string currentDir = dir.GetCurrentDir();
                    return CombinePath(currentDir, path);
                }
            }
            
            return path;
        }
        
        // 추가 유틸리티: 확장자 가져오기
        public static string GetExtension(string path)
        {
            return path.GetExtension();
        }
        
        // 추가 유틸리티: 확장자 없는 파일명 가져오기
        public static string GetBaseName(string path)
        {
            return path.GetFile().GetBaseName();
        }
        
        // 추가 유틸리티: 파일명만 가져오기
        public static string GetFileName(string path)
        {
            return path.GetFile();
        }
        
        // 추가 유틸리티: 디렉토리 경로만 가져오기
        public static string GetDirectory(string path)
        {
            return path.GetBaseDir();
        }
        
        // 추가 유틸리티: 경로 정규화 (중복 슬래시 제거 등)
        public static string SimplifyPath(string path)
        {
            return path.SimplifyPath();
        }
        
        // 추가 유틸리티: 상대 경로인지 확인
        public static bool IsRelativePath(string path)
        {
            return path.IsRelativePath();
        }
        
        // 추가 유틸리티: 절대 경로인지 확인
        public static bool IsAbsolutePath(string path)
        {
            return path.IsAbsolutePath();
        }
        
        // 추가 유틸리티: 경로 유효성 검사
        public static bool IsValidPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            // Godot 프로토콜 체크
            if (IsGodotPath(path))
                return true;

            // 일반 경로 유효성 체크 - 금지된 문자 확인
            return !path.Contains("\0") && !path.Contains("\n") && !path.Contains("\r");
        }

        // 추가 메서드: 모든 라인 읽기
        public static string[] ReadAllLines(string path)
        {
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
            {
                if (file != null && file.IsOpen())
                {
                    var lines = new List<string>();
                    while (!file.EofReached())
                    {
                        lines.Add(file.GetLine());
                    }
                    file.Close();
                    return lines.ToArray();
                }
                else
                {
                    throw new System.IO.FileNotFoundException($"Could not open file: {path}");
                }
            }
        }

        // 추가 메서드: 파일에 텍스트 쓰기
        public static void WriteAllText(string path, string content)
        {
            using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
            {
                if (file != null && file.IsOpen())
                {
                    file.StoreString(content);
                    file.Close();
                }
                else
                {
                    throw new System.IO.IOException($"Could not write to file: {path}");
                }
            }
        }

        // 추가 메서드: 디렉토리 생성
        public static void CreateDirectory(string path)
        {
            var error = DirAccess.MakeDirRecursiveAbsolute(path);
            if (error != Godot.Error.Ok)
            {
                throw new System.IO.IOException($"Failed to create directory: {path}, Error: {error}");
            }
        }

        // 추가 메서드: 디렉토리 삭제
        public static void DeleteDirectory(string path, bool recursive)
        {
            if (recursive)
            {
                // 재귀적 삭제: 내부 파일/디렉토리 모두 삭제
                using (var dir = DirAccess.Open(path))
                {
                    if (dir != null)
                    {
                        // 먼저 모든 파일과 서브디렉토리 삭제
                        dir.ListDirBegin();
                        string item = dir.GetNext();

                        while (!string.IsNullOrEmpty(item))
                        {
                            if (item != "." && item != "..")
                            {
                                string itemPath = CombinePath(path, item);
                                if (dir.CurrentIsDir())
                                {
                                    DeleteDirectory(itemPath, true);
                                }
                                else
                                {
                                    DeleteFile(itemPath);
                                }
                            }
                            item = dir.GetNext();
                        }
                        dir.ListDirEnd();
                    }
                }
            }

            // 디렉토리 자체 삭제
            var error = DirAccess.RemoveAbsolute(path);
            if (error != Godot.Error.Ok)
            {
                throw new System.IO.IOException($"Failed to delete directory: {path}, Error: {error}");
            }
        }

        // 추가 메서드: 파일 삭제
        public static void DeleteFile(string path)
        {
            using (var dir = DirAccess.Open(path.GetBaseDir()))
            {
                if (dir != null)
                {
                    var error = dir.Remove(path);
                    if (error != Godot.Error.Ok)
                    {
                        throw new System.IO.IOException($"Failed to delete file: {path}, Error: {error}");
                    }
                }
                else
                {
                    throw new System.IO.IOException($"Could not access directory for file: {path}");
                }
            }
        }

        // 추가 메서드: 파일 시스템 엔트리 가져오기
        public static string[] GetFileSystemEntries(string path)
        {
            var entries = new List<string>();

            using (var dir = DirAccess.Open(path))
            {
                if (dir != null)
                {
                    dir.ListDirBegin();
                    string item = dir.GetNext();

                    while (!string.IsNullOrEmpty(item))
                    {
                        if (item != "." && item != "..")
                        {
                            entries.Add(CombinePath(path, item));
                        }
                        item = dir.GetNext();
                    }
                    dir.ListDirEnd();
                }
            }

            return entries.ToArray();
        }

        // 추가 메서드: 절대 경로 가져오기 (이미 GetAbsolutePath가 있지만 GetFullPath 이름으로도 제공)
        public static string GetFullPath(string path)
        {
            return GetAbsolutePath(path);
        }

        // 추가 메서드: 파일/디렉토리 이동 (이름 변경)
        public static void Move(string source, string dest)
        {
            if (FileExists(source))
            {
                // 파일 이동
                using (var dir = DirAccess.Open(source.GetBaseDir()))
                {
                    if (dir != null)
                    {
                        var error = dir.Rename(source, dest);
                        if (error != Godot.Error.Ok)
                        {
                            throw new System.IO.IOException($"Failed to move file from {source} to {dest}, Error: {error}");
                        }
                    }
                }
            }
            else if (DirExists(source))
            {
                // 디렉토리 이동
                using (var dir = DirAccess.Open(source.GetBaseDir()))
                {
                    if (dir != null)
                    {
                        var error = dir.Rename(source, dest);
                        if (error != Godot.Error.Ok)
                        {
                            throw new System.IO.IOException($"Failed to move directory from {source} to {dest}, Error: {error}");
                        }
                    }
                }
            }
            else
            {
                throw new System.IO.FileNotFoundException($"Source not found: {source}");
            }
        }

        // 추가 유틸리티: 디렉토리 이름 가져오기 (GetDirectory와 동일하지만 .NET 호환성을 위해)
        public static string GetDirectoryName(string path)
        {
            return GetDirectory(path);
        }
    }
}

#endif