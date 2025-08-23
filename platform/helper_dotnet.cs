using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DotNet_IO
{
    public class Helper
    {
        public static string[] GetFiles(string path, string extension = "")
        {
            var files = new List<string>();
            
            try
            {
                if (Directory.Exists(path))
                {
                    var allFiles = Directory.GetFiles(path);
                    foreach (var file in allFiles)
                    {
                        if (string.IsNullOrEmpty(extension) || file.EndsWith(extension))
                        {
                            // 경로를 슬래시로 정규화
                            files.Add(file.Replace('\\', '/'));
                        }
                    }
                }
            }
            catch (Exception)
            {
                // 오류 무시, 빈 배열 반환
            }
            
            return files.ToArray();
        }
        
        public static string[] GetDirectories(string path)
        {
            var directories = new List<string>();
            
            try
            {
                if (Directory.Exists(path))
                {
                    var allDirs = Directory.GetDirectories(path);
                    foreach (var dir in allDirs)
                    {
                        // 경로를 슬래시로 정규화
                        directories.Add(dir.Replace('\\', '/'));
                    }
                }
            }
            catch (Exception)
            {
                // 오류 무시, 빈 배열 반환
            }
            
            return directories.ToArray();
        }
        
        public static bool FileExists(string path)
        {
            return File.Exists(path);
        }
        
        public static bool DirExists(string path)
        {
            return Directory.Exists(path);
        }
        
        // 추가 유틸리티 메서드 - ModuleSystem에서 필요
        public static string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }
        
        // 경로 결합 메서드 - 슬래시로 정규화
        public static string CombinePath(params string[] paths)
        {
            string combined = Path.Combine(paths);
            return combined.Replace('\\', '/');
        }
        
        // 대소문자 정확한 매칭 (Windows에서도 엄격하게)
        public static bool FileExistsExact(string filePath)
        {
            if (!File.Exists(filePath))
                return false;
            
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileName(filePath);
                
                if (string.IsNullOrEmpty(directory))
                    directory = ".";
                
                var actualFiles = Directory.GetFiles(directory);
                
                foreach (var actualFile in actualFiles)
                {
                    if (Path.GetFileName(actualFile) == fileName)
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
        
        public static bool DirExistsExact(string dirPath)
        {
            if (!Directory.Exists(dirPath))
                return false;
            
            try
            {
                var parentDir = Path.GetDirectoryName(dirPath);
                var dirName = Path.GetFileName(dirPath);
                
                if (string.IsNullOrEmpty(parentDir))
                    parentDir = ".";
                
                var actualDirs = Directory.GetDirectories(parentDir);
                
                foreach (var actualDir in actualDirs)
                {
                    if (Path.GetFileName(actualDir) == dirName)
                        return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}