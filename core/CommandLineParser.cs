using System;
using System.Collections.Generic;

namespace SharpPy.Core
{
    /// <summary>
    /// Command line split into the three parts the interpreter cares about.
    ///
    /// The shape follows CPython's config_parse_cmdline / config_update_argv
    /// (Python/initconfig.c:2358 and :2723): option parsing stops at the first non-option argument,
    /// and from that point on everything belongs to the program being run. Interpreter options never
    /// reach sys.argv; the script path in argv[0] is whatever was typed, not a canonicalised path.
    ///
    /// SharpPy recognises its own flag set here rather than CPython's full option grammar; what is
    /// mirrored is the position rule, because that is what sys.argv observes.
    /// </summary>
    public sealed class CommandLine
    {
        internal readonly Dictionary<string, string> Options = new Dictionary<string, string>();

        /// <summary>The script selected by the command line, or null.</summary>
        public string PythonFile;

        /// <summary>Arguments after the script. CPython puts these in sys.argv[1:].</summary>
        public string[] ScriptArgs = Array.Empty<string>();

        /// <summary>The operand of -c, or null.</summary>
        public string CodeString;

        /// <summary>Arguments after the -c operand. sys.argv becomes ["-c", ...these].</summary>
        public string[] CodeArgs = Array.Empty<string>();
    }

    /// <summary>
    /// 명령줄 인수 파싱을 전담하는 클래스
    /// </summary>
    public class CommandLineParser
    {
        /// <summary>
        /// 명령줄 인수를 파싱하여 옵션, Python 파일, 그리고 스크립트에 넘길 인수를 분리한다.
        /// </summary>
        public CommandLine Parse(string[] args)
        {
            var line = new CommandLine();

            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];

                // Once the script is known, option parsing is over: CPython's config_update_argv
                // slices sys.argv from the script onward, so nothing here may swallow the rest.
                if (!string.IsNullOrEmpty(line.PythonFile))
                {
                    line.ScriptArgs = Rest(args, i);
                    break;
                }

                // 옵션 플래그들
                switch (arg)
                {
                    case "--verbose":
                    case "-v":
                        SharpPyConfig.VerboseMode = true;
                        break;

                    case "--quiet":
                    case "-q":
                        SharpPyConfig.QuietMode = true;
                        break;

                    case "--dis":
                        line.Options["--dis"] = "true";
                        break;

                    case "--tokens":
                        line.Options["--tokens"] = "true";
                        break;

                    case "--ast":
                        line.Options["--ast"] = "true";
                        break;

                    case "--compile":
                        line.Options["--compile"] = "true";
                        break;

                    case "--compare-parsers":
                        line.Options["--compare-parsers"] = "true";
                        break;

                    case "--use-peg-parser":
                        line.Options["--use-peg-parser"] = "true";
                        break;

                    case "--test-generator-kwargs":
                        line.Options["--test-generator-kwargs"] = "true";
                        break;

                    case "--test-pymethod-kwargs":
                        line.Options["--test-pymethod-kwargs"] = "true";
                        break;

                    case "-c":
                        if (i + 1 < args.Length)
                        {
                            line.Options["-c"] = args[i + 1];
                            line.CodeString = args[i + 1];
                            // `python -c CODE a b` puts a and b after the forced argv[0] of "-c".
                            line.CodeArgs = Rest(args, i + 2);
                            i = args.Length;
                        }
                        break;

                    case "-m":
                        if (i + 1 < args.Length)
                        {
                            line.Options["-m"] = args[i + 1];
                            line.CodeArgs = Rest(args, i + 2);
                            i = args.Length;
                        }
                        break;

                    case "help":
                    case "--help":
                    case "-h":
                        line.Options["help"] = "true";
                        break;

                    default:
                        // main_run_python: the first argument that is no option is the script,
                        // whatever it is called. -W and -X take a value, joined or not.
                        if (arg is "-W" or "-X")
                            i++;
                        else if (!arg.StartsWith("-", StringComparison.Ordinal))
                            line.PythonFile = arg;
                        break;
                }
            }

            return line;
        }

        private static string[] Rest(string[] args, int from)
        {
            if (from >= args.Length) return Array.Empty<string>();
            var rest = new string[args.Length - from];
            Array.Copy(args, from, rest, 0, rest.Length);
            return rest;
        }
    }
}
