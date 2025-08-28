using System;

namespace SharpPy
{
    public class ManualIterationTest
    {
        public static void RunTest()
        {
            Console.WriteLine("=== Testing sys module ===");
            
            try
            {
                // Test sys module creation
                var sysModule = SharpPy.Modules.SysModule.CreateSysModule();
                Console.WriteLine($"sys module created: {sysModule}");
                Console.WriteLine($"sys module name: {sysModule.Name}");
                
                // Test sys.path
                var sysPath = sysModule.GetAttribute("path") as PyList;
                Console.WriteLine($"\nsys.path length: {sysPath.Length()}");
                Console.WriteLine("sys.path contents:");
                for (int i = 0; i < sysPath.Length(); i++)
                {
                    Console.WriteLine($"  [{i}]: {sysPath.GetItem(i).ToRepr()}");
                }
                
                // Test sys.path.append
                Console.WriteLine("\n=== Testing sys.path.append ===");
                sysPath.Append(new PyString("/custom/path"));
                Console.WriteLine($"After append, length: {sysPath.Length()}");
                Console.WriteLine($"Last item: {sysPath.GetItem(sysPath.Length()-1).ToRepr()}");
                
                // Test sys.modules
                var sysModules = sysModule.GetAttribute("modules");
                Console.WriteLine($"\nsys.modules: {sysModules}");
                Console.WriteLine($"sys.modules length: {sysModules.Length()}");
                
                // Test other sys attributes
                var platform = sysModule.GetAttribute("platform");
                Console.WriteLine($"\nsys.platform: {platform.ToRepr()}");
                
                var version = sysModule.GetAttribute("version");
                Console.WriteLine($"sys.version: {version.ToRepr()}");
                
                var maxsize = sysModule.GetAttribute("maxsize");
                Console.WriteLine($"sys.maxsize: {maxsize.ToRepr()}");
                
                var byteorder = sysModule.GetAttribute("byteorder");
                Console.WriteLine($"sys.byteorder: {byteorder.ToRepr()}");
                
                // Test sys functions
                var getdefaultencoding = sysModule.GetAttribute("getdefaultencoding") as PyBuiltinFunction;
                var encoding = getdefaultencoding.Call();
                Console.WriteLine($"sys.getdefaultencoding(): {encoding.ToRepr()}");
                
                Console.WriteLine("\n=== sys module test completed successfully ===");
                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error testing sys module: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
}