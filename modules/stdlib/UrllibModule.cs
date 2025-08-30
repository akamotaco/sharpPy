using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace SharpPy.Modules.Stdlib
{
    public static class UrllibModule
    {
        internal static readonly HttpClient _httpClient = new HttpClient();
        
        public static PyModule CreateUrllibModule()
        {
            var module = new PyModule("urllib", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\urllib.py");
            
            // urllib.request 서브모듈
            var requestModule = new PyModule("urllib.request", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\urllib\\request.py");
            requestModule.ModuleDict["urlopen"] = new PyUrllibFunction("urlopen");
            
            // urllib.parse 서브모듈  
            var parseModule = new PyModule("urllib.parse", "C:\\Users\\m11\\Desktop\\work\\sharpPy\\modules\\urllib\\parse.py");
            parseModule.ModuleDict["urlparse"] = new PyUrllibFunction("urlparse");
            parseModule.ModuleDict["urljoin"] = new PyUrllibFunction("urljoin");
            parseModule.ModuleDict["quote"] = new PyUrllibFunction("quote");
            parseModule.ModuleDict["unquote"] = new PyUrllibFunction("unquote");
            
            module.ModuleDict["request"] = requestModule;
            module.ModuleDict["parse"] = parseModule;
            
            return module;
        }
    }

    public class PyUrllibFunction : PyBuiltinFunction
    {
        private readonly string _functionName;
        
        public PyUrllibFunction(string functionName) : base(functionName)
        {
            _functionName = functionName;
        }

        public override PyObject Call(params PyObject[] args)
        {
            try
            {
                switch (_functionName)
                {
                    case "urlopen":
                        return HandleUrlOpen(args);
                    case "urlparse":
                        return HandleUrlParse(args);
                    case "urljoin":
                        return HandleUrlJoin(args);
                    case "quote":
                        return HandleQuote(args);
                    case "unquote":
                        return HandleUnquote(args);
                    default:
                        throw PyException.Create($"Function {_functionName} not implemented");
                }
            }
            catch (Exception ex)
            {
                throw PyException.Create($"{_functionName} error: {ex.Message}");
            }
        }

        private PyObject HandleUrlOpen(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("urlopen() missing required argument: 'url'");
            
            var url = args[0].ToString();
            
            try
            {
                var response = UrllibModule._httpClient.GetAsync(url).Result;
                var content = response.Content.ReadAsStringAsync().Result;
                
                // 간단한 응답 객체를 문자열로 반환
                return new PyString(content);
            }
            catch (Exception ex)
            {
                throw PyException.Create($"URL open error: {ex.Message}");
            }
        }

        private PyObject HandleUrlParse(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("urlparse() missing required argument: 'urlstring'");
            
            var urlstring = args[0].ToString();
            
            try
            {
                var uri = new Uri(urlstring);
                
                // 간단한 파싱 결과를 딕셔너리로 반환
                var result = new PyDict();
                result.SetItem(new PyString("scheme"), new PyString(uri.Scheme));
                result.SetItem(new PyString("netloc"), new PyString(uri.Authority));
                result.SetItem(new PyString("path"), new PyString(uri.AbsolutePath));
                result.SetItem(new PyString("query"), new PyString(uri.Query.TrimStart('?')));
                result.SetItem(new PyString("fragment"), new PyString(uri.Fragment.TrimStart('#')));
                
                return result;
            }
            catch (Exception ex)
            {
                throw PyValueError.Create($"Invalid URL: {urlstring}");
            }
        }

        private PyObject HandleUrlJoin(PyObject[] args)
        {
            if (args.Length < 2)
                throw PyTypeError.Create("urljoin() missing required arguments");
            
            var baseUrl = args[0].ToString();
            var url = args[1].ToString();
            
            try
            {
                var baseUri = new Uri(baseUrl);
                var resultUri = new Uri(baseUri, url);
                return new PyString(resultUri.ToString());
            }
            catch (Exception ex)
            {
                throw PyValueError.Create($"URL join error: {ex.Message}");
            }
        }

        private PyObject HandleQuote(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("quote() missing required argument: 'string'");
            
            var input = args[0].ToString();
            return new PyString(Uri.EscapeDataString(input));
        }

        private PyObject HandleUnquote(PyObject[] args)
        {
            if (args.Length == 0)
                throw PyTypeError.Create("unquote() missing required argument: 'string'");
            
            var input = args[0].ToString();
            return new PyString(Uri.UnescapeDataString(input));
        }
    }
}