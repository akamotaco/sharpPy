using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace SharpPy.Modules
{
    /// <summary>
    /// PC/_wmimodule.cpp, for the two tables Lib/platform.py asks about. The answers come from what
    /// .NET knows of the machine, not from WMI; a table or column it does not know is WMI's error.
    /// </summary>
    internal static class WmiModule
    {
        private static string Processor(string column) => column switch
        {
            "Architecture" => RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "9", Architecture.Arm64 => "12", Architecture.Arm => "5", _ => "0",
            },
            "Caption" => Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "",
            "Manufacturer" => (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "").Split(',').Last().Trim(),
            _ => null,
        };

        private static string System(string column) => column switch
        {
            "Version" => $"{Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{Environment.OSVersion.Version.Build}",
            "ProductType" => "1",
            "BuildType" => "Multiprocessor Free",
            "ServicePackMajorVersion" or "ServicePackMinorVersion" => "0",
            _ => null,
        };

        private static PythonException Invalid() => new PythonException(
            PyOSError.FromArguments(new PyObject[] { new PyInt(22), new PyString("Windows Error 0x80041010") }, null));

        internal static PyModule CreateModule()
        {
            var module = new PyModule("_wmi");
            module.ModuleDict["exec_query"] = new PyBuiltinFunction("exec_query", args =>
            {
                if (args.Length != 1 || args[0] is not PyString query)
                    throw PyTypeError.Create("exec_query() argument must be str");
                var parts = query.Value.Split(new[] { " FROM " }, StringSplitOptions.None);
                if (parts.Length != 2 || !parts[0].StartsWith("SELECT ", StringComparison.Ordinal))
                    throw Invalid();
                Func<string, string> table = parts[1].Trim() switch
                {
                    "Win32_OperatingSystem" => System,
                    "Win32_Processor" => Processor,
                    _ => throw Invalid(),
                };
                var rows = new List<string>();
                foreach (var column in parts[0].Substring(7).Split(',').Select(c => c.Trim()).OrderBy(c => c, StringComparer.Ordinal))
                    rows.Add($"{column}={table(column) ?? throw Invalid()}");
                return new PyString(string.Join("\0", rows));
            });
            return module;
        }
    }
}
