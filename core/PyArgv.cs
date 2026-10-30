using System;

namespace SharpPy.Core;

/// <summary>
/// What sys.argv will be. POLICY: this is the SharpPy counterpart of PyConfig.argv.
///
/// CPython builds sys.argv in two steps, and both are worth naming because the shapes look
/// arbitrary otherwise:
///
///   Python/initconfig.c:2723  config_update_argv(config, opt_index)
///       - copies the command line starting at the index where option parsing stopped, i.e. from
///         the first non-option argument on. Interpreter options are therefore not in sys.argv, and
///         the script path appears exactly as it was typed, not canonicalised.
///       - if that slice is empty it appends L"", with the comment "Ensure at least one (empty)
///         argument is seen". That is why bare `python` reports sys.argv == [''].
///       - forces argv[0] = L"-c" when a command string was given ("Force sys.argv[0] = '-c'"), and
///         L"-m" for module mode.
///   Python/sysmodule.c:3612   COPY_LIST("argv", config->argv) - sys.argv is simply a copy of
///         PyConfig.argv, so the host decides it; Py_Main is only the default host.
///
/// Measured on the pinned reference CPython 3.12.15 (.artifacts/tmp/argv_shapes.py):
///
///   script.py one two      -> ['.artifacts\\...\\script.py', 'one', 'two']
///   script.py              -> ['.artifacts\\...\\script.py']
///   -u script.py x         -> ['.artifacts\\...\\script.py', 'x']
///   -c CODE                -> ['-c']
///   -c CODE one two        -> ['-c', 'one', 'two']
///   - one                  -> ['-', 'one']            (script read from stdin)
///   (no arguments)         -> ['']
///
/// Default value: an empty list. An embedded host that never fills PyConfig.argv gets an empty
/// sys.argv from COPY_LIST above, and SharpPy inside Godot is exactly that case, so the default
/// keeps the Godot host's behaviour instead of inventing arguments the host did not pass. The CLI
/// entry point (Program.Main) sets the real values.
/// </summary>
internal static class PyArgv
{
    private static string[] _values = Array.Empty<string>();

    internal static string[] Values => _values;

    internal static void Set(string[] values) => _values = values ?? Array.Empty<string>();

    /// <summary>
    /// config_update_argv's "ensure at least one (empty) argument is seen", used for the REPL path.
    /// </summary>
    internal static void SetEmptyArg() => _values = new[] { "" };

    /// <summary>
    /// Script mode: the slice begins at the script itself, which keeps the path as the user typed
    /// it, and everything after it belongs to the program.
    /// </summary>
    internal static string[] ForScript(string script, string[] trailing)
    {
        var values = new string[1 + (trailing?.Length ?? 0)];
        values[0] = script;
        if (trailing != null)
            Array.Copy(trailing, 0, values, 1, trailing.Length);
        return values;
    }

    /// <summary>
    /// Command mode: argv[0] is forced to "-c", and the operands after the code string follow.
    /// </summary>
    internal static string[] ForCommand(string[] trailing)
    {
        var values = new string[1 + (trailing?.Length ?? 0)];
        values[0] = "-c";
        if (trailing != null)
            Array.Copy(trailing, 0, values, 1, trailing.Length);
        return values;
    }
}
