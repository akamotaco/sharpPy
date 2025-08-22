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
                // Godot에서 대소문자 정확한 매칭 확인
                string directory = System.IO.Path.GetDirectoryName(filePath);
                string fileName = System.IO.Path.GetFileName(filePath);
                
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
                string parentDir = System.IO.Path.GetDirectoryName(dirPath);
                string dirName = System.IO.Path.GetFileName(dirPath);
                
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
    }
}

#endif