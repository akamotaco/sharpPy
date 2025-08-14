using System;
using System.Linq;
using System.IO;

namespace SharpPy
{
    // Enhanced Python Interpreter Main Class with Path System and Line Number Error Reporting
    public class PythonInterpreter
    {
        private Environment globalEnv;
        public List<string> SearchPaths { get; private set; }

        public PythonInterpreter() 
        {
            globalEnv = new Environment();
            SearchPaths = new List<string>
            {
                ".", // Current directory
                "./lib", // Standard library directory (if exists)
                "./modules" // Additional modules directory (if exists)
            };
            globalEnv.SearchPaths = SearchPaths;
        }

        public void AddSearchPath(string path)
        {
            if (!SearchPaths.Contains(path))
            {
                SearchPaths.Add(path);
                globalEnv.SearchPaths = SearchPaths; // Update environment
            }
        }

        public void RemoveSearchPath(string path)
        {
            SearchPaths.Remove(path);
            globalEnv.SearchPaths = SearchPaths; // Update environment
        }

        public void SetGlobalEnv(Environment env) => globalEnv = env;
        public Environment GetGlobalEnv() => globalEnv;

        public object Execute(string code, string filename = "<string>")
        {
            try
            {
                var lexer = new Lexer(code);
                var tokens = lexer.Tokenize();

                var parser = new Parser(tokens);
                var ast = parser.Parse();

                object result = null;
                foreach (var statement in ast)
                {
                    try
                    {
                        result = statement.Evaluate(globalEnv);
                    }
                    catch (PythonException ex)
                    {
                        // If the exception doesn't have line info, try to use the statement's line info
                        if (ex.Line == 0 && statement.Line > 0)
                        {
                            throw new PythonException(ex.Type, ex.Message, statement.Line, statement.Column, filename);
                        }
                        else if (ex.FileName == "<string>" && filename != "<string>")
                        {
                            // Update filename if it's more specific
                            throw new PythonException(ex.Type, ex.Message, ex.Line, ex.Column, filename);
                        }
                        throw; // Re-throw with existing location info
                    }
                    catch (Exception ex) when (!(ex is PythonException))
                    {
                        // Convert other exceptions to PythonException with location info
                        int line = statement.Line > 0 ? statement.Line : 0;
                        int column = statement.Column > 0 ? statement.Column : 0;
                        throw new PythonException("RuntimeError", $"Internal error: {ex.Message}", line, column, filename);
                    }
                }

                return result;
            }
            catch (PythonException ex)
            {
                // Enhanced error output with actual line numbers
                if (ex.Line > 0)
                {
                    Console.WriteLine($"  File \"{ex.FileName}\", line {ex.Line}, column {ex.Column}");
                    
                    // Show the problematic line if we have access to the source
                    if (filename != "<string>" && File.Exists(filename))
                    {
                        try
                        {
                            var lines = File.ReadAllLines(filename);
                            if (ex.Line <= lines.Length)
                            {
                                Console.WriteLine($"    {lines[ex.Line - 1]}");
                                // Add pointer to the column
                                if (ex.Column > 0)
                                {
                                    var pointer = new string(' ', ex.Column - 1) + "^";
                                    Console.WriteLine($"    {pointer}");
                                }
                            }
                        }
                        catch
                        {
                            // Ignore file read errors
                        }
                    }
                    else if (filename == "<string>")
                    {
                        // For interactive/string execution, show the line from the code
                        try
                        {
                            var lines = code.Split('\n');
                            if (ex.Line <= lines.Length)
                            {
                                Console.WriteLine($"    {lines[ex.Line - 1]}");
                                // Add pointer to the column
                                if (ex.Column > 0)
                                {
                                    var pointer = new string(' ', ex.Column - 1) + "^";
                                    Console.WriteLine($"    {pointer}");
                                }
                            }
                        }
                        catch
                        {
                            // Ignore errors in showing source line
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"  File \"{ex.FileName}\"");
                }
                
                Console.WriteLine($"{ex.Type}: {ex.Message}");
                Console.WriteLine(); // Add empty line for readability
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  File \"{filename}\"");
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine($"Exception type: {ex.GetType().Name}");
                
                if (ex.StackTrace != null)
                {
                    var relevantStack = ex.StackTrace.Split('\n')
                        .Where(line => line.Contains("SharpPy") || line.Contains("PythonInterpreter"))
                        .Take(3);
                    foreach (var line in relevantStack)
                    {
                        Console.WriteLine($"  {line.Trim()}");
                    }
                }
                Console.WriteLine(); // Add empty line for readability
                return null;
            }
        }

        public void ExecuteFile(string filename)
        {
            try
            {
                if (!File.Exists(filename))
                {
                    Console.WriteLine($"Error: File '{filename}' not found");
                    return;
                }

                string code = File.ReadAllText(filename);
                Execute(code, filename); // Pass the actual filename for better error reporting
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading file '{filename}': {ex.Message}");
            }
        }

        public void StartRepl()
        {
            Console.WriteLine("Extended Pure C# Python Interpreter");
            Console.WriteLine("Type 'exit()' to quit");
            Console.WriteLine("Features: Variables, Functions, Classes, Inheritance, Type Hints, Slicing, etc.");
            Console.WriteLine();

            int lineNumber = 1;

            while (true)
            {
                Console.Write(">>> ");
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                {
                    lineNumber++;
                    continue;
                }

                if (input.Trim() == "exit()" || input.Trim() == "quit()")
                    break;

                try
                {
                    // For REPL, use a special filename that includes the line number
                    var result = Execute(input, $"<stdin:{lineNumber}>");
                    if (result != null && !(result is string && string.IsNullOrEmpty((string)result)))
                    {
                        // Format output nicely
                        if (result is bool b)
                            Console.WriteLine(b ? "True" : "False");
                        else if (result is double d && d == Math.Truncate(d))
                            Console.WriteLine((int)d);
                        else
                            Console.WriteLine(result);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                }

                lineNumber++;
            }
        }
    }

    // Enhanced Program Entry Point with better demo error handling
    class Program
    {
        static void Main(string[] args)
        {
            var interpreter = new PythonInterpreter();

            if (args.Length > 0)
            {
                // Execute file if argument provided
                interpreter.ExecuteFile(args[0]);
                return;
            }

            Console.WriteLine("=== Enhanced Modular Python Interpreter Demo ===");
            Console.WriteLine("Now with improved error reporting showing actual line numbers!");

            // Basic error demonstration
            Console.WriteLine("\n=== Error Reporting Demo ===");
            interpreter.Execute(@"print('This line works')
x = 5 / 0  # This will cause a division by zero error
print('This line will not execute')");

            // Variable error demo
            Console.WriteLine("\n=== Name Error Demo ===");
            interpreter.Execute(@"print('Before error')
print(undefined_variable)  # This will cause a name error
print('After error')");

            // Type error demo
            Console.WriteLine("\n=== Type Error Demo ===");
            interpreter.Execute(@"def add_numbers(x: int, y: int) -> int:
    return x + y

result = add_numbers('hello', 'world')  # Type error");

            // Index error demo
            Console.WriteLine("\n=== Index Error Demo ===");
            interpreter.Execute(@"my_list = [1, 2, 3]
print('List contents:', my_list)
print('Valid access:', my_list[1])
print('Invalid access:', my_list[10])  # Index error");

            // 들여쓰기 테스트
            Console.WriteLine("\n=== Indentation Test ===");
            interpreter.Execute(@"print('a')
def add(x, y):
    return x + y
print('b')
result = add(5, 3)
print('result:', result)");

            // 조건부 표현식 (삼항 연산자) 테스트
            Console.WriteLine("\n=== Conditional Expression Test ===");
            interpreter.Execute(@"
score = 85
grade = 'A' if score >= 90 else 'B' if score >= 80 else 'C'
print('Score:', score, 'Grade:', grade)

# 다양한 조건부 표현식 테스트
result1 = 'positive' if 10 > 0 else 'negative'
print('10 > 0:', result1)

result2 = 'even' if 8 % 2 == 0 else 'odd'
print('8 is:', result2)

# 중첩된 조건부 표현식
age = 25
category = 'child' if age < 13 else 'teen' if age < 20 else 'adult'
print('Age:', age, 'Category:', category)
");

            // 기본 기능 테스트
            Console.WriteLine("\n=== Basic Features ===");
            interpreter.Execute(@"
# 변수와 기본 연산
a = 10
b = 20
print('a + b =', a + b)
print('Hello, World!')
");

            // 튜플 테스트
            Console.WriteLine("\n=== Tuples ===");
            interpreter.Execute(@"
# 튜플 생성과 접근
t1 = (1, 2, 'hello')
print('Tuple:', t1)
print('First element:', t1[0])
print('Last element:', t1[-1])

# 빈 튜플과 단일 요소 튜플
empty = ()
single = (42,)
print('Empty tuple:', empty)
print('Single element tuple:', single)

# 튜플 언패킹과 언더스코어
a, b, c = (1, 2, 3)
print('Unpacked:', a, b, c)

# 언더스코어로 무시하기
x, _, z = (10, 20, 30)
print('With underscore:', x, z)

# 튜플 메서드
numbers = (1, 2, 3, 2, 4, 2)
print('Count of 2:', numbers.count(2))
print('Index of 3:', numbers.index(3))
");

            // 타입 힌트 테스트
            Console.WriteLine("\n=== Type Hints ===");
            interpreter.Execute(@"
def add_numbers(x: int, y: int) -> int:
    return x + y

def greet(name: str) -> str:
    return 'Hello, ' + name

def process_tuple(data: tuple[int, str, bool]) -> str:
    num, text, flag = data
    return text + ' ' + str(num) + ' ' + str(flag)

print('add_numbers(5, 3):', add_numbers(5, 3))
print('greet result:', greet('Alice'))
print('process_tuple:', process_tuple((42, 'test', True)))
");

            // 슬라이싱 테스트
            Console.WriteLine("\n=== Slicing ===");
            interpreter.Execute(@"
numbers = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9]
print('numbers[2:7]:', numbers[2:7])
print('numbers[::-1]:', numbers[::-1])
print('numbers[::2]:', numbers[::2])

# 튜플 슬라이싱
tuple_data = (0, 1, 2, 3, 4, 5)
print('tuple[1:4]:', tuple_data[1:4])
print('tuple[::-1]:', tuple_data[::-1])

# 문자열 슬라이싱
text = 'Hello World'
print('text[::2]:', text[::2])
print('text[::-1]:', text[::-1])
");

            // 상속 테스트
            Console.WriteLine("\n=== Inheritance ===");
            interpreter.Execute(@"
class Animal:
    def __init__(self, name):
        self.name = name
    
    def speak(self):
        print(self.name + ' makes a sound')

class Dog(Animal):
    def speak(self):
        print(self.name + ' barks!')

dog = Dog('Buddy')
dog.speak()

# 부모 클래스 메서드 접근
animal = Animal('Generic')
animal.speak()
");

            Console.WriteLine("\n=== Demo Complete ===");
            Console.WriteLine("Starting interactive mode...");
            Console.WriteLine("(You can also run: PythonInterpreter.exe filename.py)");
            interpreter.StartRepl();
        }
    }
}