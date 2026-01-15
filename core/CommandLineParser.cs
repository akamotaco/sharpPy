using System;
using System.Collections.Generic;

namespace SharpPy.Core
{
    /// <summary>
    /// 명령줄 인수 파싱을 전담하는 클래스
    /// </summary>
    public class CommandLineParser
    {
        /// <summary>
        /// 명령줄 인수를 파싱하여 옵션과 Python 파일을 분리
        /// </summary>
        /// <param name="args">명령줄 인수 배열</param>
        /// <returns>(옵션 딕셔너리, Python 파일 경로)</returns>
        public (Dictionary<string, string> options, string pythonFile) Parse(string[] args)
        {
            var options = new Dictionary<string, string>();
            string pythonFile = null;
            
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                
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
                        options["--dis"] = "true";
                        break;

                    case "--tokens":
                        options["--tokens"] = "true";
                        break;

                    case "--ast":
                        options["--ast"] = "true";
                        break;

                    case "--compile":
                        options["--compile"] = "true";
                        break;

                    case "--compare-parsers":
                        options["--compare-parsers"] = "true";
                        break;

                    case "--use-peg-parser":
                        options["--use-peg-parser"] = "true";
                        break;

                    case "--test-generator-kwargs":
                        options["--test-generator-kwargs"] = "true";
                        break;

                    case "--test-pymethod-kwargs":
                        options["--test-pymethod-kwargs"] = "true";
                        break;

                    case "-c":
                        if (i + 1 < args.Length)
                        {
                            options["-c"] = args[i + 1];
                            i++; // 다음 인수 건너뛰기
                        }
                        break;
                        
                    case "-m":
                        if (i + 1 < args.Length)
                        {
                            options["-m"] = args[i + 1];
                            i++; // 다음 인수 건너뛰기
                        }
                        break;
                        
                    case "help":
                    case "--help":
                    case "-h":
                        options["help"] = "true";
                        break;
                        
                    default:
                        // Python 파일이나 기타 명령어
                        if (arg.EndsWith(".py"))
                        {
                            pythonFile = arg;
                        }
                        else if (!arg.StartsWith("-") && pythonFile == null)
                        {
                            // 특별한 명령어들 (demo, test-iteration 등)
                            options["command"] = arg;
                        }
                        break;
                }
            }
            
            return (options, pythonFile);
        }
    }
}