namespace SharpPy
{
    #region Compiler Extension (AST → Bytecode)
    
    /// <summary>
    /// CPython-style free variable analyzer for closure detection
    /// </summary>
    public class FreeVariableAnalyzer
    {
        private readonly HashSet<string> _definedVars = new HashSet<string>();
        private readonly HashSet<string> _usedVars = new HashSet<string>();
        private readonly HashSet<string> _parameters = new HashSet<string>();
        
        /// <summary>
        /// Analyze function/lambda for free variables
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeScope(Expression body, List<string> parameters)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            
            // Parameters are always defined in local scope
            foreach (var param in parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze the body expression
            AnalyzeExpression(body);
            
            // Free variables: used but not defined locally
            var freeVars = _usedVars.Except(_definedVars).ToList();
            
            // Cell variables: defined locally but referenced by nested functions
            var cellVars = new List<string>();
            
            // Phase 2: Find variables that need to be cells (referenced by nested lambdas)
            var cellNeededVars = FindCellNeededVars(body);
            foreach (var varName in cellNeededVars)
            {
                // Only parameters can be cell variables in lambda
                if (parameters.Contains(varName))
                {
                    cellVars.Add(varName);
                }
            }
            
            return (freeVars, cellVars);
        }
        
        /// <summary>
        /// Phase 2: Analyze nested function for free variables
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeNestedFunction(FunctionDefStatement func, List<string> outerVarNames)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            
            // Function parameters are defined locally
            foreach (var param in func.Parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze function body
            foreach (var statement in func.Body)
            {
                AnalyzeStatement(statement);
            }
            
            // Free variables: used but not defined locally (CPython 방식)
            // 외부 스코프의 모든 변수(매개변수 + 지역 변수)가 자유 변수가 될 수 있음
            var freeVars = _usedVars.Except(_definedVars)
                                   .Where(var => outerVarNames.Contains(var)) // 외부 스코프의 모든 변수
                                   .ToList();
            
            // Cell variables: analyze nested functions to see what they reference
            var cellVars = new List<string>();
            foreach (var statement in func.Body)
            {
                if (statement is FunctionDefStatement nestedFunc)
                {
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeNestedFunction(nestedFunc, func.Parameters);
                    
                    // Any of our parameters that nested functions use as free variables become cells
                    foreach (var nestedFreeVar in nestedFreeVars)
                    {
                        if (func.Parameters.Contains(nestedFreeVar) && !cellVars.Contains(nestedFreeVar))
                        {
                            cellVars.Add(nestedFreeVar);
                        }
                    }
                }
            }
            
            return (freeVars, cellVars);
        }
        
        /// <summary>
        /// Async function 분석 - 일반 함수와 동일한 방식으로 자유 변수 분석
        /// </summary>
        public (List<string> freeVars, List<string> cellVars) AnalyzeAsyncFunction(AsyncFunctionDefStatement asyncFunc, List<string> outerVarNames)
        {
            _definedVars.Clear();
            _usedVars.Clear();
            _parameters.Clear();
            
            // Async function parameters are defined locally
            foreach (var param in asyncFunc.Parameters)
            {
                _parameters.Add(param);
                _definedVars.Add(param);
            }
            
            // Analyze the function body for variable usage
            foreach (var statement in asyncFunc.Body)
            {
                AnalyzeStatement(statement);
            }
            
            // Free variables: variables used but not defined locally, and exist in outer scope
            var freeVars = _usedVars.Where(var => !_definedVars.Contains(var))
                                   .Where(var => outerVarNames.Contains(var)) // 외부 스코프의 모든 변수
                                   .ToList();
            
            // Cell variables: analyze nested functions to see what they reference
            var cellVars = new List<string>();
            foreach (var statement in asyncFunc.Body)
            {
                if (statement is FunctionDefStatement nestedFunc)
                {
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeNestedFunction(nestedFunc, asyncFunc.Parameters);
                    
                    // Any of our parameters that nested functions use as free variables become cells
                    foreach (var nestedFreeVar in nestedFreeVars)
                    {
                        if (asyncFunc.Parameters.Contains(nestedFreeVar) && !cellVars.Contains(nestedFreeVar))
                        {
                            cellVars.Add(nestedFreeVar);
                        }
                    }
                }
                else if (statement is AsyncFunctionDefStatement nestedAsyncFunc)
                {
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeAsyncFunction(nestedAsyncFunc, asyncFunc.Parameters);
                    
                    // Any of our parameters that nested async functions use as free variables become cells
                    foreach (var nestedFreeVar in nestedFreeVars)
                    {
                        if (asyncFunc.Parameters.Contains(nestedFreeVar) && !cellVars.Contains(nestedFreeVar))
                        {
                            cellVars.Add(nestedFreeVar);
                        }
                    }
                }
            }
            
            return (freeVars, cellVars);
        }
        
        private void AnalyzeStatement(Statement stmt)
        {
            switch (stmt)
            {
                case ExpressionStatement exprStmt:
                    AnalyzeExpression(exprStmt.Expression);
                    break;
                    
                case ReturnStatement retStmt:
                    if (retStmt.Value != null)
                        AnalyzeExpression(retStmt.Value);
                    break;
                    
                case AssignStatement assignStmt:
                    AnalyzeExpression(assignStmt.Value);
                    _definedVars.Add(assignStmt.VariableName); // 새로 정의된 변수
                    break;
                    
                // TODO: 다른 statement 타입들 추가 가능
            }
        }
        
        private void AnalyzeExpression(Expression expr)
        {
            switch (expr)
            {
                case NameExpression name:
                    _usedVars.Add(name.Name);
                    break;
                    
                case BinaryOpExpression binary:
                    AnalyzeExpression(binary.Left);
                    AnalyzeExpression(binary.Right);
                    break;
                    
                case UnaryOpExpression unary:
                    AnalyzeExpression(unary.Operand);
                    break;
                    
                case CallExpression call:
                    AnalyzeExpression(call.Function);
                    foreach (var arg in call.Arguments)
                    {
                        AnalyzeExpression(arg);
                    }
                    break;
                    
                case ConditionalExpression cond:
                    AnalyzeExpression(cond.Test);
                    AnalyzeExpression(cond.Body);
                    AnalyzeExpression(cond.OrElse);
                    break;
                    
                case LambdaExpression lambda:
                    // Nested lambda - analyze its scope
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeScope(lambda.Body, lambda.Args);
                    
                    // Nested lambda's free variables are our used variables
                    foreach (var freeVar in nestedFreeVars)
                    {
                        _usedVars.Add(freeVar);
                    }
                    break;
                    
                case AttributeExpression attr:
                    AnalyzeExpression(attr.Value);
                    break;
                    
                case SubscriptExpression subscript:
                    AnalyzeExpression(subscript.Value);
                    AnalyzeExpression(subscript.Slice);
                    break;
                    
                case ListExpression list:
                    foreach (var item in list.Elements)
                    {
                        AnalyzeExpression(item);
                    }
                    break;
                    
                case TupleExpression tuple:
                    foreach (var item in tuple.Elements)
                    {
                        AnalyzeExpression(item);
                    }
                    break;
                    
                case DictExpression dict:
                    foreach (var (key, value) in dict.Items)
                    {
                        AnalyzeExpression(key);
                        AnalyzeExpression(value);
                    }
                    break;
                    
                case FStringExpression fstring:
                    if (fstring.Values != null)
                    {
                        foreach (var value in fstring.Values)
                        {
                            AnalyzeExpression(value);
                        }
                    }
                    break;
                    
                case FormattedValue formatted:
                    AnalyzeExpression(formatted.Value);
                    break;
                    
                // For ConstantExpression and other leaf expressions, no variables are used
                case ConstantExpression _:
                    break;
                    
                // TODO: Add more expression types as needed
            }
        }
        
        /// <summary>
        /// Find variables that need to be cells (referenced by nested lambdas)
        /// </summary>
        private List<string> FindCellNeededVars(Expression body)
        {
            var cellNeeded = new List<string>();
            FindCellNeededVarsRecursive(body, cellNeeded);
            return cellNeeded;
        }
        
        private void FindCellNeededVarsRecursive(Expression expr, List<string> cellNeeded)
        {
            switch (expr)
            {
                case LambdaExpression lambda:
                    // This is a nested lambda - find what variables it uses from outer scope
                    var nestedAnalyzer = new FreeVariableAnalyzer();
                    var (nestedFreeVars, _) = nestedAnalyzer.AnalyzeScope(lambda.Body, lambda.Args);
                    
                    // Variables used by nested lambda from outer scope need to be cells
                    foreach (var freeVar in nestedFreeVars)
                    {
                        if (!cellNeeded.Contains(freeVar))
                        {
                            cellNeeded.Add(freeVar);
                        }
                    }
                    break;
                    
                case BinaryOpExpression binary:
                    FindCellNeededVarsRecursive(binary.Left, cellNeeded);
                    FindCellNeededVarsRecursive(binary.Right, cellNeeded);
                    break;
                    
                case UnaryOpExpression unary:
                    FindCellNeededVarsRecursive(unary.Operand, cellNeeded);
                    break;
                    
                case CallExpression call:
                    FindCellNeededVarsRecursive(call.Function, cellNeeded);
                    foreach (var arg in call.Arguments)
                    {
                        FindCellNeededVarsRecursive(arg, cellNeeded);
                    }
                    break;
                    
                case ConditionalExpression cond:
                    FindCellNeededVarsRecursive(cond.Test, cellNeeded);
                    FindCellNeededVarsRecursive(cond.Body, cellNeeded);
                    FindCellNeededVarsRecursive(cond.OrElse, cellNeeded);
                    break;
                    
                case AttributeExpression attr:
                    FindCellNeededVarsRecursive(attr.Value, cellNeeded);
                    break;
                    
                case SubscriptExpression subscript:
                    FindCellNeededVarsRecursive(subscript.Value, cellNeeded);
                    FindCellNeededVarsRecursive(subscript.Slice, cellNeeded);
                    break;
                    
                case ListExpression list:
                    foreach (var element in list.Elements)
                    {
                        FindCellNeededVarsRecursive(element, cellNeeded);
                    }
                    break;
                    
                case FStringExpression fstring:
                    if (fstring.Values != null)
                    {
                        foreach (var value in fstring.Values)
                        {
                            FindCellNeededVarsRecursive(value, cellNeeded);
                        }
                    }
                    break;
                    
                case FormattedValue formatted:
                    FindCellNeededVarsRecursive(formatted.Value, cellNeeded);
                    break;
                    
                // Leaf nodes don't need recursion
                case NameExpression _:
                case ConstantExpression _:
                    break;
                    
                // TODO: Add more expression types as needed
            }
        }
    }

    // AST를 바이트코드로 컴파일 (기존 시스템과 연동)
    public class PythonCompiler
    {
        private readonly bool _enable_optimizer = true;
        private List<ByteCodeInstruction> _instructions;
        private List<PyObject> _constants;
        private List<string> _names;
        private List<string> _varNames;
        private bool _isInFunction = false; // Track if we're compiling inside a function
        private string _currentFunctionName = null; // Track current function name for module level detection
        private bool _isInComprehension = false; // Track if we're compiling inside a comprehension
        
        // Phase 2: 클로저 지원
        private List<string> _cellVars = new List<string>();
        private List<string> _freeVars = new List<string>();
        private List<ExceptionTableEntry> _exceptionTable = new List<ExceptionTableEntry>(); // CPython 3.12 Exception Table
        
        public PyCodeObject Compile(List<Statement> statements, string name = "<module>")
        {
            return Compile(statements, name, new List<string>());
        }
        
        public PyCodeObject Compile(List<Statement> statements, string name, List<string> parameters)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in parameters)
            {
                _varNames.Add(param);
            }
            
            Console.WriteLine($"\n🔧 컴파일: {name}");
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 모듈은 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, parameters.Count, null, null, null, 0);
            
            // Add Exception Table entries (CPython 3.12 compatible)
            if (_exceptionTable.Count > 0)
            {
                codeObject.ExceptionTable.AddRange(_exceptionTable);
                Console.WriteLine($"📋 Exception Table: {_exceptionTable.Count}개 엔트리 추가됨");
            }
            else
            {
                Console.WriteLine($"📋 Exception Table: 비어있음 (CPython 3.12 compatible)");
            }
            Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
            
            // 바이트코드 최적화 적용
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            return optimizedCode;
        }
        
        /// <summary>
        /// Phase 2: 클로저 정보를 포함한 컴파일
        /// </summary>
        public PyCodeObject CompileWithClosure(List<Statement> statements, string name, List<string> parameters,
                                             List<string> freeVars, List<string> cellVars)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in parameters)
            {
                _varNames.Add(param);
            }
            
            Console.WriteLine($"\n🔧 컴파일 (클로저): {name}");
            Console.WriteLine($"  FreeVars: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  CellVars: [{string.Join(", ", cellVars)}]");
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행
            foreach (var cellVar in cellVars)
            {
                var paramIndex = parameters.IndexOf(cellVar);
                if (paramIndex >= 0)
                {
                    Console.WriteLine($"  → Making cell for parameter: {cellVar}");
                    EmitInstruction(ByteCodeOp.MAKE_CELL, paramIndex);
                }
            }
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 모듈은 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                            parameters.Count, freeVars, cellVars, null, 0);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            Console.WriteLine($"\u2705 컴파일 완료: {_instructions.Count}개 명령어");
            
            // 바이트코드 최적화 적용
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            return optimizedCode;
        }
        
        /// <summary>
        /// CPython 호환: 매개변수 문자열에서 이름과 기본값 분리
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults, int flags) ParseFunctionParameters(List<string> parameters)
        {
            var paramNames = new List<string>();
            var defaults = new List<PyObject>();
            int flags = 0;
            
            foreach (var param in parameters)
            {
                string cleanName = param;
                PyObject defaultValue = null;
                
                // CPython 방식: 매개변수 문자열 파싱
                if (param.Contains("="))
                {
                    // name=value 또는 name:type=value 처리
                    var equalIndex = param.LastIndexOf('=');
                    var nameTypePart = param.Substring(0, equalIndex).Trim();
                    var defaultValueStr = param.Substring(equalIndex + 1).Trim();
                    
                    // 타입 주석 제거: name:type -> name  
                    if (nameTypePart.Contains(":"))
                    {
                        cleanName = nameTypePart.Substring(0, nameTypePart.IndexOf(':')).Trim();
                    }
                    else
                    {
                        cleanName = nameTypePart;
                    }
                    
                    // CPython 호환: 기본값을 정의 시점에서 평가
                    defaultValue = ParseAndEvaluateDefaultValue(defaultValueStr);
                }
                else if (param.Contains(":"))
                {
                    // 타입 주석만 있는 경우: name:type
                    cleanName = param.Substring(0, param.IndexOf(':')).Trim();
                }
                else
                {
                    // 단순 매개변수 이름
                    cleanName = param.Trim();
                }
                
                // CPython 방식: **kwargs 및 *args 플래그 설정
                if (cleanName.StartsWith("**"))
                {
                    flags |= PyCodeObject.CO_VARKEYWORDS;
                    cleanName = cleanName.Substring(2); // ** 제거
                }
                else if (cleanName.StartsWith("*"))
                {
                    flags |= PyCodeObject.CO_VARARGS;
                    cleanName = cleanName.Substring(1); // * 제거
                }
                
                paramNames.Add(cleanName);
                // CPython 방식: null이 아닌 기본값만 defaults 리스트에 추가
                if (defaultValue != null)
                {
                    defaults.Add(defaultValue);
                }
            }
            
            return (paramNames, defaults, flags);
        }
        
        /// <summary>
        /// Async function 매개변수 파싱 - 일반 함수와 동일한 로직
        /// </summary>
        private (List<string> paramNames, List<PyObject> defaults, int flags) ParseAsyncFunctionParameters(List<string> parameters)
        {
            return ParseFunctionParameters(parameters); // 동일한 로직 재사용
        }
        
        /// <summary>
        /// Async function body 컴파일 - CO_COROUTINE 플래그 추가
        /// </summary>
        private PyCodeObject CompileAsyncFunctionBody(AsyncFunctionDefStatement asyncFunc, List<string> freeVars, List<string> cellVars)
        {
            var (paramNames, defaults, flags) = ParseAsyncFunctionParameters(asyncFunc.Parameters);
            
            // CO_COROUTINE 플래그 추가
            flags |= PyCodeObject.CO_COROUTINE;
            
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars);
            return compiler.CompileWithClosureAndDefaults(asyncFunc.Body, asyncFunc.Name, paramNames, defaults, freeVars, cellVars, flags);
        }
        
        /// <summary>
        /// CPython 호환: 기본값을 정의 시점에서 파싱하고 평가
        /// </summary>
        private PyObject ParseAndEvaluateDefaultValue(string defaultValueStr)
        {
            // CPython 방식: 리터럴 우선 처리
            if (int.TryParse(defaultValueStr, out int intValue))
            {
                return new PyInt(intValue);
            }
            
            if (double.TryParse(defaultValueStr, out double floatValue))
            {
                return new PyFloat(floatValue);
            }
            
            // 불린 리터럴
            if (defaultValueStr == "True")
                return PyBool.True;
            if (defaultValueStr == "False")
                return PyBool.False;
            
            // 문자열 리터럴 처리
            if ((defaultValueStr.StartsWith("\"") && defaultValueStr.EndsWith("\"")) ||
                (defaultValueStr.StartsWith("'") && defaultValueStr.EndsWith("'")))
            {
                var content = defaultValueStr.Substring(1, defaultValueStr.Length - 2);
                // 기본적인 이스케이프 처리
                content = content.Replace("\\n", "\n")
                              .Replace("\\t", "\t")
                              .Replace("\\r", "\r")
                              .Replace("\\'", "'")
                              .Replace("\\\"", "\"")
                              .Replace("\\\\", "\\");
                return new PyString(content);
            }
            
            // None 처리
            if (defaultValueStr == "None")
                return PyNone.Instance;
            
            // 복합 표현식은 나중에 처리 (현재는 단순 리터럴만)
            Console.WriteLine($"⚠️ Warning: Complex default value '{defaultValueStr}' not yet supported");
            return PyNone.Instance;
        }
        
        /// <summary>
        /// CPython 호환: 클로저와 기본값을 모두 지원하는 컴파일
        /// </summary>
        public PyCodeObject CompileWithClosureAndDefaults(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, List<string> freeVars, List<string> cellVars, int flags = 0)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            _isInFunction = true; // We are now compiling inside a function
            _currentFunctionName = name; // Track function name for module level detection
            
            // 함수 매개변수를 _varNames에 추가 (LOAD_FAST/STORE_FAST용)
            foreach (var param in paramNames)
            {
                // **kwargs 매개변수는 변수명에서 ** 제거 (예: **kwargs -> kwargs)
                string localVarName = param;
                if (param.StartsWith("**"))
                {
                    localVarName = param.Substring(2);
                }
                else if (param.StartsWith("*"))
                {
                    localVarName = param.Substring(1);
                }
                
                _varNames.Add(localVarName);
            }
            
            Console.WriteLine($"\n🔧 컴파일 (클로저+기본값): {name}");
            Console.WriteLine($"  매개변수: [{string.Join(", ", paramNames)}]");
            Console.WriteLine($"  기본값: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            Console.WriteLine($"  FreeVars: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  CellVars: [{string.Join(", ", cellVars)}]");
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행
            foreach (var cellVar in cellVars)
            {
                var paramIndex = paramNames.IndexOf(cellVar);
                if (paramIndex >= 0)
                {
                    Console.WriteLine($"  → Making cell for parameter: {cellVar}");
                    EmitInstruction(ByteCodeOp.MAKE_CELL, paramIndex);
                }
            }
            
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 함수는 None 반환
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                            paramNames.Count, freeVars, cellVars, defaults, flags);
            
            // Add Exception Table entries (CPython 3.12 compatible)
            if (_exceptionTable.Count > 0)
            {
                codeObject.ExceptionTable.AddRange(_exceptionTable);
                Console.WriteLine($"📋 Exception Table: {_exceptionTable.Count}개 엔트리 추가됨");
            }
            else
            {
                Console.WriteLine($"📋 Exception Table: 비어있음 (CPython 3.12 compatible)");
            }
            Console.WriteLine($"✅ 컴파일 완료: {_instructions.Count}개 명령어");
            
            // 바이트코드 최적화 적용
            var optimizer = new ByteCodeOptimizer(_enable_optimizer);
            var optimizedCode = optimizer.OptimizeCode(codeObject);
            
            _isInFunction = false; // Reset function context
            _currentFunctionName = null; // Reset function name
            return optimizedCode;
        }
        
        /// <summary>
        /// CPython 호환: 함수를 매개변수 기본값과 함께 컴파일
        /// </summary>
        public PyCodeObject CompileFunction(List<Statement> statements, string name, List<string> paramNames, List<PyObject> defaults, int flags = 0)
        {
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            _isInFunction = true; // We are now compiling inside a function
            _currentFunctionName = name; // Track function name for module level detection
            
            // 함수 매개변수를 _varNames에 추가
            foreach (var param in paramNames)
            {
                _varNames.Add(param);
            }
            
            Console.WriteLine($"\n🔧 컴파일 함수: {name}");
            Console.WriteLine($"  매개변수: [{string.Join(", ", paramNames)}]");
            Console.WriteLine($"  기본값: [{string.Join(", ", defaults.Select(d => d?.ToString() ?? "None"))}]");
            
            // 함수 본문 컴파일
            foreach (var statement in statements)
            {
                CompileStatement(statement);
            }
            
            // 함수는 None 반환 (return문이 없을 경우)
            EmitLoadConst(PyNone.Instance);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            // PyCodeObject 생성 (기본값 포함)
            var codeObject = new PyCodeObject(name, _instructions, _constants, _names, _varNames, 
                                            paramNames.Count, null, null, defaults, flags);
            
            // Add Exception Table entries (CPython 3.12)
            codeObject.ExceptionTable.AddRange(_exceptionTable);
            Console.WriteLine($"✅ 함수 컴파일 완료: {_instructions.Count}개 명령어");
            
            _isInFunction = false; // Reset function context
            _currentFunctionName = null; // Reset function name
            return codeObject;
        }
        
        /// <summary>
        /// Phase 2: 컴파일러에 클로저 정보 설정
        /// </summary>
        public void SetupClosureCompilation(List<string> cellVars, List<string> freeVars = null)
        {
            _cellVars = cellVars ?? new List<string>();
            _freeVars = freeVars ?? new List<string>();
        }
        
        private void CompileStatement(Statement statement)
        {
            switch (statement)
            {
                case AssignStatement assign:
                    CompileExpression(assign.Value);
                    EmitStoreName(assign.VariableName);
                    break;
                    
                case AssignTargetStatement assignTarget:
                    CompileAssignTarget(assignTarget);
                    break;
                    
                case AnnAssignStatement annAssign:
                    CompileAnnAssign(annAssign);
                    break;
                    
                case AugAssignStatement augAssign:
                    CompileAugAssign(augAssign);
                    break;
                    
                case WalrusStatement walrus:
                    CompileExpression(walrus.Value);
                    EmitInstruction(ByteCodeOp.DUP_TOP);  // 값 복사
                    EmitStoreName(walrus.Target);         // 저장
                    break;
                    
                case ExpressionStatement expr:
                    CompileExpression(expr.Expression);
                    EmitInstruction(ByteCodeOp.POP_TOP);
                    break;
                    
                case ReturnStatement ret:
                    if (ret.Value != null)
                        CompileExpression(ret.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.RETURN_VALUE);
                    break;
                    
                case YieldStatement yield:
                    if (yield.Value != null)
                        CompileExpression(yield.Value);
                    else
                        EmitLoadConst(PyNone.Instance);
                    EmitInstruction(ByteCodeOp.YIELD_VALUE);
                    break;
                    
                case YieldFromStatement yieldFrom:
                    CompileExpression(yieldFrom.Value);
                    EmitInstruction(ByteCodeOp.GET_ITER);
                    EmitInstruction(ByteCodeOp.YIELD_FROM);
                    break;
                    
                case FunctionDefStatement func:
                    CompileNestedFunction(func);
                    break;
                    
                case AsyncFunctionDefStatement asyncFunc:
                    CompileAsyncFunction(asyncFunc);
                    break;
                    
                case ClassDefStatement cls:
                    CompileClass(cls);
                    break;
                    
                case TypeAliasStatement typeAlias:
                    CompileTypeAlias(typeAlias);
                    break;
                    
                case ImportStatement import:
                    CompileImport(import);
                    break;
                    
                case ImportFromStatement importFrom:
                    CompileImportFrom(importFrom);
                    break;
                    
                case IfStatement ifStmt:
                    CompileIf(ifStmt);
                    break;
                    
                case WhileStatement whileStmt:
                    CompileWhile(whileStmt);
                    break;
                    
                case ForStatement forStmt:
                    CompileFor(forStmt);
                    break;
                    
                case ForTupleStatement forTupleStmt:
                    CompileForTuple(forTupleStmt);
                    break;
                    
                case TryStatement tryStmt:
                    CompileTry(tryStmt);
                    break;
                    
                case WithStatement withStmt:
                    CompileWith(withStmt);
                    break;
                    
                case MatchStatement matchStmt:
                    CompileMatch(matchStmt);
                    break;
                    
                case BreakStatement:
                    EmitInstruction(ByteCodeOp.BREAK_LOOP);
                    break;
                    
                case ContinueStatement:
                    EmitInstruction(ByteCodeOp.CONTINUE_LOOP);
                    break;
                    
                case PassStatement:
                    EmitInstruction(ByteCodeOp.NOP);
                    break;
                    
                case AssertStatement assert:
                    CompileAssert(assert);
                    break;
                    
                case RaiseStatement raise:
                    CompileRaise(raise);
                    break;
                    
                case DeleteStatement delete:
                    CompileDelete(delete);
                    break;
                    
                case GlobalStatement global:
                    CompileGlobal(global);
                    break;
                    
                case NonlocalStatement nonlocal:
                    CompileNonlocal(nonlocal);
                    break;
                    
                default:
                    throw new NotImplementedException($"Statement {statement.GetType().Name} not implemented");
            }
        }
        
        private void CompileExpression(Expression expression)
        {
            switch (expression)
            {
                case ConstantExpression constant:
                    EmitLoadConst(constant.Value);
                    break;
                    
                case NameExpression name:
                    EmitLoadName(name.Name);
                    break;
                    
                case WalrusExpression walrus:
                    // Compile value first
                    CompileExpression(walrus.Value);
                    // Duplicate value on stack for assignment
                    EmitInstruction(ByteCodeOp.DUP_TOP);
                    // Store to variable
                    EmitStoreName(walrus.Target);
                    // Value remains on stack as return value
                    break;
                    
                case BinaryOpExpression binOp:
                    CompileExpression(binOp.Left);
                    CompileExpression(binOp.Right);
                    EmitBinaryOp(binOp.Operator);
                    break;
                    
                case UnaryOpExpression unaryOp:
                    CompileExpression(unaryOp.Operand);
                    EmitUnaryOp(unaryOp.Op);
                    break;
                    
                case CompareExpression compare:
                    CompileExpression(compare.Left);
                    CompileExpression(compare.Right);
                    EmitCompareOp(compare.Op);
                    break;
                    
                case BoolOpExpression boolOp:
                    CompileBoolOp(boolOp);
                    break;
                    
                case CallExpression call:
                    CompileExpression(call.Function);
                    
                    // 위치 인수 컴파일
                    foreach (var arg in call.Arguments)
                    {
                        CompileExpression(arg);
                    }
                    
                    // 키워드 인수가 있는 경우
                    if (call.Keywords.Count > 0)
                    {
                        // 키워드 인수 값들을 스택에 푸시
                        foreach (var keyword in call.Keywords)
                        {
                            CompileExpression(keyword.Value);
                        }
                        
                        // 키워드 이름들을 튜플로 만들어서 스택에 푸시
                        foreach (var keyword in call.Keywords)
                        {
                            EmitLoadConst(new PyString(keyword.Arg ?? ""));
                        }
                        EmitInstruction(ByteCodeOp.BUILD_TUPLE, call.Keywords.Count);
                        
                        // CALL_FUNCTION_KW 사용 (위치인수개수, 키워드인수개수)
                        EmitInstruction(ByteCodeOp.CALL_FUNCTION_KW, call.Arguments.Count);
                    }
                    else
                    {
                        // 키워드 인수가 없는 경우 기존 방식
                        EmitInstruction(ByteCodeOp.CALL_FUNCTION, call.Arguments.Count);
                    }
                    break;
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);
                    EmitLoadAttr(attr.Attr);
                    break;
                    
                case SubscriptExpression subscript:
                    CompileExpression(subscript.Value);
                    CompileExpression(subscript.Slice);
                    EmitInstruction(ByteCodeOp.LOAD_SUBSCR);
                    break;
                    
                case ListExpression list:
                    foreach (var element in list.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_LIST, list.Elements.Count);
                    break;
                    
                case TupleExpression tuple:
                    foreach (var element in tuple.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_TUPLE, tuple.Elements.Count);
                    break;
                    
                case SetExpression set:
                    foreach (var element in set.Elements)
                    {
                        CompileExpression(element);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_SET, set.Elements.Count);
                    break;
                    
                case DictExpression dict:
                    foreach (var kvp in dict.Items)
                    {
                        CompileExpression(kvp.Key);
                        CompileExpression(kvp.Value);
                    }
                    EmitInstruction(ByteCodeOp.BUILD_MAP, dict.Items.Count);
                    break;
                    
                case LambdaExpression lambda:
                    CompileLambda(lambda);
                    break;
                    
                case ConditionalExpression conditional:
                    CompileConditional(conditional);
                    break;
                    
                case AwaitExpression await:
                    CompileExpression(await.Value);
                    EmitInstruction(ByteCodeOp.GET_AWAITABLE);
                    break;
                    
                case FStringExpression fstring:
                    CompileFString(fstring);
                    break;
                    
                case FormattedValue formatted:
                    CompileFormattedValue(formatted);
                    break;
                    
                case StarredExpression starred:
                    CompileExpression(starred.Value);
                    // 별표 처리는 문맥에 따라 다름
                    break;
                    
                // Python 3.12 Type Parameters
                case TypeVarExpression typeVar:
                    EmitLoadConst(new PyString(typeVar.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.TYPEVAR);
                    break;
                    
                case ParamSpecExpression paramSpec:
                    EmitLoadConst(new PyString(paramSpec.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.PARAMSPEC);
                    break;
                    
                case TypeVarTupleExpression typeVarTuple:
                    EmitLoadConst(new PyString(typeVarTuple.Name));
                    EmitInstruction(ByteCodeOp.CALL_INTRINSIC_1, (int)IntrinsicFunction.TYPEVARTUPLE);
                    break;
                    
                // PEP 709 Comprehension Optimization - Bytecode inlining
                case ListComprehension listComp:
                    CompileListComprehension(listComp);
                    break;
                    
                case DictComprehension dictComp:
                    CompileDictComprehension(dictComp);
                    break;
                    
                case SetComprehension setComp:
                    CompileSetComprehension(setComp);
                    break;
                    
                case GeneratorExpression genExp:
                    CompileGeneratorExpression(genExp);
                    break;
                    
                default:
                    throw new NotImplementedException($"Expression {expression.GetType().Name} not implemented");
            }
        }
        
        private void CompileFunction(FunctionDefStatement func)
        {
            // 모든 함수를 CompileNestedFunction으로 바이패스 (Phase 2 수정)
            CompileNestedFunction(func);
        }
        
        /// <summary>
        /// Phase 2: 중청 함수 컴파일 (자유 변수 지원)
        /// </summary>
        private void CompileNestedFunction(FunctionDefStatement func)
        {
            Console.WriteLine($"\n🔍 Compiling nested function: {func.Name}");
            
            // 1. 자유 변수 분석
            var analyzer = new FreeVariableAnalyzer();
            Console.WriteLine($"  DEBUG: Current _varNames: [{string.Join(", ", _varNames)}]");
            var (freeVars, cellVars) = analyzer.AnalyzeNestedFunction(func, _varNames);
            
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            
            // 2. 매개변수와 기본값 파싱 (FunctionDefStatement에서 수행하던 로직)
            var (paramNames, defaults, flags) = ParseFunctionParameters(func.Parameters);
            
            // 3. 코드 객체 컴파일 (자유 변수 정보와 기본값 포함)
            var compiler = new PythonCompiler();
            compiler.SetupClosureCompilation(cellVars, freeVars); // 셀 변수와 자유 변수 설정
            var funcCode = compiler.CompileWithClosureAndDefaults(func.Body, func.Name, paramNames, defaults, freeVars, cellVars, flags);
            
            // 3. 자유 변수가 있는 경우 클로저 생성
            if (freeVars.Count > 0)
            {
                Console.WriteLine($"  → Creating closure for {freeVars.Count} free variables");
                
                // 각 자유 변수에 대해 LOAD_CLOSURE 발행
                foreach (var freeVar in freeVars)
                {
                    // 자유 변수가 현재 스코프의 cell 변수인지 확인
                    var cellIndex = _cellVars.IndexOf(freeVar);
                    if (cellIndex >= 0)
                    {
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, cellIndex);
                        Console.WriteLine($"    → LOAD_CLOSURE for {freeVar} (cell index {cellIndex})");
                    }
                    else
                    {
                        Console.WriteLine($"    ⚠️ Warning: Free variable {freeVar} not found in current scope cells");
                        // 빈 셀 생성
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, 0);
                    }
                }
                
                // 클로저 튜플 생성
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }
            
            // 4. 기본값이 있는 경우 기본값 튜플을 스택에 푸시
            int makeFunctionFlags = 0;
            if (defaults.Count > 0)
            {
                foreach (var defaultValue in defaults)
                {
                    EmitLoadConst(defaultValue);
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, defaults.Count);
                makeFunctionFlags |= 1; // MAKE_FUNCTION_DEFAULTS flag
            }
            
            // 5. 코드 객체 로드
            EmitLoadConst(funcCode);
            
            // 6. 함수 생성 (기본값 + 클로저 플래그 설정)
            if (freeVars.Count > 0)
            {
                makeFunctionFlags |= 8; // MAKE_FUNCTION_CLOSURE flag
            }
            
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFunctionFlags);
            
            // 7. 데코레이터 적용 (함수가 생성된 후)
            if (func.Decorators != null && func.Decorators.Count > 0)
            {
                // 데코레이터는 역순으로 적용됩니다 (마지막 데코레이터부터)
                foreach (var decorator in func.Decorators)
                {
                    CompileExpression(decorator.DecoratorFunction); // 데코레이터 함수를 스택에 로드
                    EmitInstruction(ByteCodeOp.ROT_TWO); // 함수와 데코레이터 순서 바꾸기
                    
                    // 데코레이터에 인수가 있는 경우 처리 (@decorator(args))
                    int argCount = 1; // 기본적으로 함수 1개
                    if (decorator.Arguments.Count > 0)
                    {
                        foreach (var arg in decorator.Arguments)
                        {
                            CompileExpression(arg);
                            argCount++;
                        }
                    }
                    
                    EmitInstruction(ByteCodeOp.CALL_FUNCTION, argCount); // 데코레이터(함수, args...) 호출
                }
            }
            
            EmitStoreName(func.Name);
        }
        
        /// <summary>
        /// 함수 내의 모든 중청 함수를 분석하여 셀이 필요한 변수들을 찾는다
        /// </summary>
        private void AnalyzeFunctionClosures(List<Statement> statements, List<string> parameters, 
                                           HashSet<string> allFreeVars, List<string> cellVars)
        {
            foreach (var statement in statements)
            {
                if (statement is FunctionDefStatement nestedFunc)
                {
                    // 중청 함수 발견 - 자유 변수 분석
                    var analyzer = new FreeVariableAnalyzer();
                    var (freeVars, _) = analyzer.AnalyzeNestedFunction(nestedFunc, parameters);
                    
                    foreach (var freeVar in freeVars)
                    {
                        allFreeVars.Add(freeVar);
                        
                        // 이 변수가 현재 함수의 매개변수이거나 지역변수이면 Cell로 만들어야 함
                        if (parameters.Contains(freeVar) && !cellVars.Contains(freeVar))
                        {
                            cellVars.Add(freeVar);
                        }
                    }
                    
                    // 재귀적으로 중청 함수들도 분석
                    AnalyzeFunctionClosures(nestedFunc.Body, nestedFunc.Parameters, allFreeVars, cellVars);
                }
            }
        }
        
        // 바이트코드 생성 도우미들
        private void EmitInstruction(ByteCodeOp opCode, int argument = 0)
        {
            _instructions.Add(new ByteCodeInstruction(opCode, argument));
        }
        
        private void EmitLoadConst(PyObject value)
        {
            var index = AddConstant(value);
            EmitInstruction(ByteCodeOp.LOAD_CONST, index);
        }
        
        private void EmitLoadName(string name)
        {
            // Phase 2: 클로저 지원 - 자유 변수 처리 개선
            
            // 1. 지역 변수(매개변수 포함) 처리
            var varIndex = _varNames.IndexOf(name);
            if (varIndex >= 0)
            {
                // 이 변수가 cell로 변환되었는지 확인
                if (_cellVars.Contains(name))
                {
                    // Cell 변수는 LOAD_DEREF로 접근
                    var cellIndex = _cellVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.LOAD_DEREF, cellIndex);
                    Console.WriteLine($"    → LOAD_DEREF for cell var: {name} (index {cellIndex})");
                }
                else
                {
                    EmitInstruction(ByteCodeOp.LOAD_FAST, varIndex);
                }
                return;
            }
            
            // 2. 자유 변수 처리 (Phase 2)
            if (_freeVars.Contains(name))
            {
                var freeIndex = _freeVars.IndexOf(name);
                EmitInstruction(ByteCodeOp.LOAD_DEREF, freeIndex);
                Console.WriteLine($"    → LOAD_DEREF for free var: {name} (index {freeIndex})");
                return;
            }
            
            // 3. 모듈 레벨: CPython 3.12 호환성을 위해 LOAD_GLOBAL 사용
            if (!_isInFunction)
            {
                // 내장 함수 우선 처리
                if (IsBuiltinFunction(name))
                {
                    var builtinIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.LOAD_GLOBAL_BUILTIN, builtinIndex);
                }
                else
                {
                    var globalIndex = AddName(name);
                    EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
                }
                return;
            }
            
            // 4. 함수 내부에서의 전역 변수/내장 함수 참조 - CPython 3.12 호환성
            // 내장 함수 우선 처리
            if (IsBuiltinFunction(name))
            {
                var builtinIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_GLOBAL_BUILTIN, builtinIndex);
            }
            else
            {
                // 일반 전역 변수
                var globalIndex = AddName(name);
                EmitInstruction(ByteCodeOp.LOAD_GLOBAL, globalIndex);
            }
        }
        
        private void EmitStoreName(string name)
        {
            // 함수 내부에서는 지역변수로 등록하고 STORE_FAST 사용
            if (_isInFunction)
            {
                // 셀 변수 처리 (Phase 2)
                if (_cellVars.Contains(name))
                {
                    var cellIndex = _cellVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, cellIndex);
                    Console.WriteLine($"    → STORE_DEREF for cell var: {name} (index {cellIndex})");
                    return;
                }
                
                // 자유 변수 처리 (Phase 2)
                if (_freeVars.Contains(name))
                {
                    var freeIndex = _freeVars.IndexOf(name);
                    EmitInstruction(ByteCodeOp.STORE_DEREF, freeIndex);
                    Console.WriteLine($"    → STORE_DEREF for free var: {name} (index {freeIndex})");
                    return;
                }
                
                // 지역 변수로 등록하고 STORE_FAST 사용
                var varIndex = GetOrAddVarName(name);
                EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
                return;
            }
            
            // 모듈 레벨: CPython 3.12 호환성을 위해 STORE_GLOBAL 사용
            var index = AddName(name);
            EmitInstruction(ByteCodeOp.STORE_GLOBAL, index);
        }
        
        /// <summary>
        /// CPython 3.12: 내장 함수 판별 - LOAD_GLOBAL_BUILTIN 최적화용
        /// </summary>
        private bool IsBuiltinFunction(string name)
        {
            // CPython 3.12 주요 내장 함수들
            var builtins = new HashSet<string>
            {
                "print", "len", "str", "int", "float", "bool", "list", "tuple", "dict", "set",
                "range", "enumerate", "zip", "map", "filter", "sum", "min", "max", "abs",
                "round", "sorted", "reversed", "any", "all", "isinstance", "issubclass",
                "type", "id", "hash", "repr", "format", "input", "open", "iter", "next",
                "getattr", "setattr", "hasattr", "delattr", "vars", "dir", "globals", "locals",
                "eval", "exec", "compile", "callable", "chr", "ord", "hex", "oct", "bin"
            };
            
            return builtins.Contains(name);
        }
        
        private void EmitBinaryOp(string op)
        {
            // CPython 3.12+ BINARY_OP 구조 사용 - 연산 타입을 argument로 전달
            var operation = op switch
            {
                "+" => BinaryOpType.ADD,                  // 0
                "&" => BinaryOpType.AND,                  // 1
                "//" => BinaryOpType.FLOOR_DIVIDE,        // 2
                "<<" => BinaryOpType.LSHIFT,              // 3
                "%" => BinaryOpType.MODULO,               // 4
                "*" => BinaryOpType.MULTIPLY,             // 5
                "|" => BinaryOpType.OR,                   // 6
                ">>" => BinaryOpType.RSHIFT,              // 7
                "**" => BinaryOpType.POWER,               // 8
                "-" => BinaryOpType.SUBTRACT,             // 9
                "^" => BinaryOpType.XOR,                  // 10
                "/" => BinaryOpType.TRUE_DIVIDE,          // 11 - Python 3.x true division
                "@" => BinaryOpType.MATRIX_MULTIPLY,      // 12 - 행렬 곱셈
                // Boolean operators (simplified implementation)
                "and" => BinaryOpType.AND,               // Logical AND (simplified as bitwise AND)
                "or" => BinaryOpType.OR,                 // Logical OR (simplified as bitwise OR)
                _ => throw new NotImplementedException($"Binary operator '{op}' not implemented")
            };
            
            // BINARY_OP OpCode와 operation 타입을 argument로 전달
            EmitInstruction(ByteCodeOp.BINARY_OP, (int)operation);
        }
        
        private int AddConstant(PyObject value)
        {
            _constants.Add(value);
            return _constants.Count - 1;
        }
        
        private int AddName(string name)
        {
            if (!_names.Contains(name))
                _names.Add(name);
            return _names.IndexOf(name);
        }
        
        // 추가 컴파일 메서드들
        
        private void EmitUnaryOp(string op)
        {
            var opCode = op switch
            {
                "+" => ByteCodeOp.UNARY_POSITIVE,
                "-" => ByteCodeOp.UNARY_NEGATIVE,
                "not" => ByteCodeOp.UNARY_NOT,
                "~" => ByteCodeOp.UNARY_INVERT,
                _ => throw new NotImplementedException($"Unary operator '{op}' not implemented")
            };
            EmitInstruction(opCode);
        }
        
        private void EmitCompareOp(string op)
        {
            var compareOp = op switch
            {
                "<" => 0,  // LT
                "<=" => 1, // LE
                "==" => 2, // EQ
                "!=" => 3, // NE
                ">" => 4,  // GT
                ">=" => 5, // GE
                "in" => 6, // IN
                "not in" => 7, // NOT_IN
                "is" => 8, // IS
                "is not" => 9, // IS_NOT
                _ => throw new NotImplementedException($"Compare operator '{op}' not implemented")
            };
            EmitInstruction(ByteCodeOp.COMPARE_OP, compareOp);
        }
        
        private void EmitLoadAttr(string attrName)
        {
            var index = AddName(attrName);
            EmitInstruction(ByteCodeOp.LOAD_ATTR, index);
        }
        
        private void CompileAugAssign(AugAssignStatement augAssign)
        {
            // target += value -> LOAD target, LOAD value, INPLACE_ADD, STORE target
            EmitLoadName(augAssign.Target);
            CompileExpression(augAssign.Value);
            
            var opCode = augAssign.Op switch
            {
                "+=" => ByteCodeOp.INPLACE_ADD,
                "-=" => ByteCodeOp.INPLACE_SUBTRACT,
                "*=" => ByteCodeOp.INPLACE_MULTIPLY,
                "/=" => ByteCodeOp.INPLACE_DIVIDE,
                "//=" => ByteCodeOp.INPLACE_FLOOR_DIVIDE,
                "%=" => ByteCodeOp.INPLACE_MODULO,
                "**=" => ByteCodeOp.INPLACE_POWER,
                "&=" => ByteCodeOp.INPLACE_AND,
                "|=" => ByteCodeOp.INPLACE_OR,
                "^=" => ByteCodeOp.INPLACE_XOR,
                "<<=" => ByteCodeOp.INPLACE_LSHIFT,
                ">>=" => ByteCodeOp.INPLACE_RSHIFT,
                "@=" => ByteCodeOp.INPLACE_MATRIX_MULTIPLY,
                _ => throw new NotImplementedException($"Augment assign operator '{augAssign.Op}' not implemented")
            };
            
            EmitInstruction(opCode);
            EmitStoreName(augAssign.Target);
        }
        
        private void CompileAnnAssign(AnnAssignStatement annAssign)
        {
            // Annotated assignment: var: type = value
            // For now, we'll treat this like a regular assignment if there's a value
            if (annAssign.Value != null)
            {
                CompileExpression(annAssign.Value);
                EmitStoreName(annAssign.VariableName);
            }
            
            // TODO: Store type annotation in __annotations__ dict for runtime introspection
            // This would require checking if we're at module/class level and maintaining __annotations__
        }
        
        /// <summary>
        /// Async function compilation - similar to CompileNestedFunction but creates async function
        /// </summary>
        private void CompileAsyncFunction(AsyncFunctionDefStatement asyncFunc)
        {
            Console.WriteLine($"\n🔍 Compiling async function: {asyncFunc.Name}");
            
            // 1. 자유 변수 분석 (동일한 방식으로 분석)
            var analyzer = new FreeVariableAnalyzer();
            Console.WriteLine($"  DEBUG: Current _varNames: [{string.Join(", ", _varNames)}]");
            var (freeVars, cellVars) = analyzer.AnalyzeAsyncFunction(asyncFunc, _varNames);
            
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            
            // 2. 매개변수와 기본값 파싱
            var (paramNames, defaults, flags) = ParseAsyncFunctionParameters(asyncFunc.Parameters);
            
            // 3. 코드 객체 컴파일 (async 함수 전용)
            var codeObject = CompileAsyncFunctionBody(asyncFunc, freeVars, cellVars);
            
            // 4. 기본값들을 스택에 로드
            foreach (var defaultValue in defaults)
            {
                EmitLoadConst(defaultValue); // 기본값은 이미 PyObject이므로 직접 로드
            }
            
            // 5. 클로저가 있으면 셀 변수들을 스택에 로드
            if (freeVars.Any())
            {
                foreach (var freeVar in freeVars)
                {
                    if (_varNames.Contains(freeVar))
                    {
                        // Local variable in enclosing scope
                        EmitInstruction(ByteCodeOp.LOAD_CLOSURE, _varNames.IndexOf(freeVar));
                    }
                    else
                    {
                        // Could be in enclosing function's free vars
                        EmitLoadName(freeVar);
                    }
                }
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }
            
            // 6. 코드 객체를 상수로 로드 (이름은 이미 코드 객체에 포함됨)
            EmitLoadConst(codeObject);
            
            // 8. MAKE_ASYNC_FUNCTION 명령어 생성 (아직 없으므로 MAKE_FUNCTION으로 대체)
            var makeFlags = 0;
            if (defaults.Any()) makeFlags |= 0x01;  // CO_HAS_DEFAULTS
            if (freeVars.Any()) makeFlags |= 0x08;  // CO_HAS_CLOSURE
            makeFlags |= 0x10; // CO_ASYNC_FUNCTION (async 함수 플래그)
            
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFlags);
            
            // 9. 함수를 변수에 저장
            EmitStoreName(asyncFunc.Name);
            
            Console.WriteLine($"✅ Async function {asyncFunc.Name} compiled successfully");
        }
        private void CompileClass(ClassDefStatement cls)
        {
            // CPython 3.12: Compile class body as a proper function
            // Load __build_class__ function first
            EmitLoadName("__build_class__");
            
            // Compile class body into a function (with potential free variables)
            var classBodyName = $"<class_body_{cls.Name}>";
            var classBodyCode = CompileClassBody(cls.Body, classBodyName);
            
            // For now, don't create closure for class bodies - use simpler approach
            // Class body should access parent scope variables via normal name lookup
            int makeFunctionFlags = 0;
            
            // Load the class body function code
            EmitLoadConst(classBodyCode);
            
            // Create function (with or without closure)
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, makeFunctionFlags);
            
            // Load class name
            EmitLoadConst(new PyString(cls.Name));
            
            // Load base classes
            foreach (var baseExpr in cls.Bases)
            {
                CompileExpression(baseExpr);
            }
            
            // Load metaclass if specified
            int totalArgs = 2 + cls.Bases.Count;
            if (cls.Metaclass != null)
            {
                CompileExpression(cls.Metaclass);
                totalArgs++;
            }
            
            // Call __build_class__(class_body_function, name, *bases [, metaclass])
            EmitInstruction(ByteCodeOp.CALL_FUNCTION, totalArgs);
            
            // Store the created class
            EmitStoreName(cls.Name);
        }
        
        private PyCodeObject CompileClassBody(List<Statement> body, string className)
        {
            // Save current compilation state
            var savedInstructions = _instructions;
            var savedConstants = _constants;
            var savedNames = _names;
            var savedVarNames = _varNames;
            var savedCellVars = _cellVars;
            var savedFreeVars = _freeVars;
            
            // Initialize new compilation state for class body
            _instructions = new List<ByteCodeInstruction>();
            _constants = new List<PyObject>();
            _names = new List<string>();
            _varNames = new List<string>();
            _cellVars = new List<string>();
            _freeVars = new List<string>();
            _exceptionTable = new List<ExceptionTableEntry>(); // Reset Exception Table
            
            // For now, disable free variable analysis for class bodies
            // Class bodies will use normal name lookup instead of closure mechanism
            // var freeVariableAnalyzer = new ClassBodyFreeVariableAnalyzer();
            // var classFreeVars = freeVariableAnalyzer.AnalyzeClassBody(body, savedNames);
            // _freeVars.AddRange(classFreeVars);
            
            try
            {
                // Compile class body statements
                foreach (var stmt in body)
                {
                    CompileStatement(stmt);
                }
                
                // Return None at the end
                EmitLoadConst(PyNone.Instance);
                EmitInstruction(ByteCodeOp.RETURN_VALUE);
                
                // Create code object for class body with free variables
                var codeObject = new PyCodeObject(
                    className,
                    _instructions.ToList(),
                    _constants.ToList(),
                    _names.ToList(),
                    _varNames.ToList(),
                    argCount: 0,  // Class body has no arguments
                    freeVars: _freeVars.ToList(),  // Include free variables
                    cellVars: _cellVars.ToList()
                );
                
                return codeObject;
            }
            finally
            {
                // Restore compilation state
                _instructions = savedInstructions;
                _constants = savedConstants;
                _names = savedNames;
                _varNames = savedVarNames;
                _cellVars = savedCellVars;
                _freeVars = savedFreeVars;
            }
        }
        /// <summary>
        /// 클래스 body에서 사용되는 자유변수를 분석하는 클래스
        /// </summary>
        private class ClassBodyFreeVariableAnalyzer
        {
            public List<string> AnalyzeClassBody(List<Statement> body, List<string> parentScopeNames)
            {
                var usedNames = new HashSet<string>();
                var definedNames = new HashSet<string>();
                
                // 클래스 body에서 정의되는 이름들 수집
                foreach (var stmt in body)
                {
                    CollectDefinedNames(stmt, definedNames);
                }
                
                // 클래스 body에서 사용되는 이름들 수집
                foreach (var stmt in body)
                {
                    CollectUsedNames(stmt, usedNames);
                }
                
                // 사용되지만 클래스 내부에서 정의되지 않은 이름들 = 자유변수
                var freeVars = new List<string>();
                foreach (var name in usedNames)
                {
                    // 클래스 내부에서 정의되지 않고, 부모 스코프에 존재하는 변수들
                    if (!definedNames.Contains(name) && parentScopeNames.Contains(name))
                    {
                        freeVars.Add(name);
                    }
                }
                
                return freeVars;
            }
            
            private void CollectDefinedNames(Statement stmt, HashSet<string> definedNames)
            {
                switch (stmt)
                {
                    case FunctionDefStatement func:
                        definedNames.Add(func.Name);
                        break;
                    case AssignStatement assign:
                        // AssignStatement uses VariableName, not Target
                        definedNames.Add(assign.VariableName);
                        break;
                    case AssignTargetStatement assignTarget:
                        if (assignTarget.Target is NameExpression nameExpr2)
                            definedNames.Add(nameExpr2.Name);
                        break;
                    // 다른 정의 구문들도 필요시 추가
                }
            }
            
            private void CollectUsedNames(Statement stmt, HashSet<string> usedNames)
            {
                switch (stmt)
                {
                    case FunctionDefStatement func:
                        // 데코레이터에서 사용되는 이름들
                        foreach (var decorator in func.Decorators)
                        {
                            CollectUsedNamesFromExpression(decorator.DecoratorFunction, usedNames);
                        }
                        // 함수 body는 별도 스코프이므로 분석하지 않음
                        break;
                    case ExpressionStatement exprStmt:
                        CollectUsedNamesFromExpression(exprStmt.Expression, usedNames);
                        break;
                    case AssignStatement assign:
                        CollectUsedNamesFromExpression(assign.Value, usedNames);
                        break;
                    case AssignTargetStatement assignTarget:
                        CollectUsedNamesFromExpression(assignTarget.Value, usedNames);
                        break;
                    // 다른 구문들도 필요시 추가
                }
            }
            
            private void CollectUsedNamesFromExpression(Expression expr, HashSet<string> usedNames)
            {
                switch (expr)
                {
                    case NameExpression nameExpr:
                        usedNames.Add(nameExpr.Name);
                        break;
                    case CallExpression callExpr:
                        CollectUsedNamesFromExpression(callExpr.Function, usedNames);
                        foreach (var arg in callExpr.Arguments)
                            CollectUsedNamesFromExpression(arg, usedNames);
                        break;
                    case AttributeExpression attrExpr:
                        CollectUsedNamesFromExpression(attrExpr.Value, usedNames);
                        break;
                    // 다른 표현식들도 필요시 추가
                }
            }
        }

        private void CompileTypeAlias(TypeAliasStatement typeAlias)
        {
            // PEP 695: type X[T] = Y creates a TypeAliasType object
            // For type aliases with type parameters, we need to make the parameters available
            // This is a simplified implementation - type parameters are bound as variables
            
            // Bind type parameters to the current scope
            foreach (var typeParam in typeAlias.TypeParams)
            {
                // Create type parameter objects and bind them to variables  
                EmitLoadConst(new PyString(typeParam)); // Type parameter name as placeholder
                EmitStoreName(typeParam); // Bind to current scope
            }
            
            // Compile the type expression (right-hand side) with type parameters available
            CompileExpression(typeAlias.Value);
            
            // Store the result with the alias name
            EmitStoreName(typeAlias.Name);
        }
        private void CompileImport(ImportStatement import)
        {
            foreach (var moduleName in import.Names)
            {
                // Handle "module as alias" format
                string actualModule, alias;
                if (moduleName.Contains(" as "))
                {
                    var parts = moduleName.Split(new[] { " as " }, StringSplitOptions.RemoveEmptyEntries);
                    actualModule = parts[0].Trim();
                    alias = parts[1].Trim();
                }
                else
                {
                    actualModule = moduleName;
                    alias = moduleName;
                }
                
                // Emit IMPORT_NAME bytecode
                var moduleIndex = AddConstant(new PyString(actualModule));
                EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);
                
                // Store the imported module in the correct variable name
                // CPython 3.12: Use STORE_GLOBAL for module level imports
                var nameIndex = AddName(alias);
                if (IsModuleLevel())
                {
                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, nameIndex);
                }
                else
                {
                    EmitInstruction(ByteCodeOp.STORE_NAME, nameIndex);
                }
            }
        }
        private void CompileImportFrom(ImportFromStatement importFrom)
        {
            // Load the module first
            var moduleIndex = AddConstant(new PyString(importFrom.Module));
            EmitInstruction(ByteCodeOp.IMPORT_NAME, moduleIndex);
            
            foreach (var itemName in importFrom.Names)
            {
                // Handle "item as alias" format
                string actualItem, alias;
                if (itemName.Contains(" as "))
                {
                    var parts = itemName.Split(new[] { " as " }, StringSplitOptions.RemoveEmptyEntries);
                    actualItem = parts[0].Trim();
                    alias = parts[1].Trim();
                }
                else
                {
                    actualItem = itemName;
                    alias = itemName;
                }
                
                // Emit IMPORT_FROM bytecode
                var itemIndex = AddConstant(new PyString(actualItem));
                EmitInstruction(ByteCodeOp.IMPORT_FROM, itemIndex);
                
                // Store the imported item in the correct variable name
                // CPython 3.12: Use STORE_GLOBAL for module level imports
                var nameIndex = AddName(alias);
                if (IsModuleLevel())
                {
                    EmitInstruction(ByteCodeOp.STORE_GLOBAL, nameIndex);
                }
                else
                {
                    EmitInstruction(ByteCodeOp.STORE_NAME, nameIndex);
                }
            }
            
            // Pop the module from stack (cleanup)
            EmitInstruction(ByteCodeOp.POP_TOP);
        }
        /// <summary>
        /// CPython-style if statement compilation - simplified implementation
        /// </summary>
        private void CompileIf(IfStatement ifStmt)
        {
            // CPython-style if statement with proper conditional jumps
            
            // Compile condition expression
            CompileExpression(ifStmt.Test);
            
            // Jump past the if body if condition is false
            var jumpIfFalse = _instructions.Count;
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // Address will be patched later
            
            // Compile the if body
            foreach (var stmt in ifStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // If there's an else clause, we need to jump past it after the if body
            int? jumpAfterIf = null;
            if (ifStmt.OrElse.Count > 0)
            {
                jumpAfterIf = _instructions.Count;
                EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0); // Address will be patched later
            }
            
            // Patch the false jump to point to the else clause (or end)
            var elseStart = _instructions.Count;
            _instructions[jumpIfFalse] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, elseStart);
            
            // Compile else clause
            foreach (var stmt in ifStmt.OrElse)
            {
                CompileStatement(stmt);
            }
            
            // Patch the jump after if body to point past the else clause
            if (jumpAfterIf.HasValue)
            {
                var afterElse = _instructions.Count;
                _instructions[jumpAfterIf.Value] = new ByteCodeInstruction(ByteCodeOp.JUMP_FORWARD, afterElse);
            }
        }
        
        /// <summary>
        /// CPython-style while loop compilation - simplified implementation
        /// </summary>
        private void CompileWhile(WhileStatement whileStmt)
        {
            // CPython-style while loop compilation
            
            // Mark loop start for JUMP_BACKWARD
            var loopStart = _instructions.Count;
            
            // Compile condition expression
            CompileExpression(whileStmt.Test);
            
            // Jump past the while body if condition is false
            var jumpIfFalse = _instructions.Count;
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // Address will be patched later
            
            // Compile the while body
            foreach (var stmt in whileStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // Jump back to loop condition - CPython 3.12 style relative offset
            // JUMP_BACKWARD argument = number of instructions to jump backward
            int currentPos = _instructions.Count;
            int relativeOffset = currentPos - loopStart;
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, relativeOffset);
            
            // While completed normally - execute else clause if present
            var normalCompletionPoint = _instructions.Count;
            if (whileStmt.ElseClause != null && whileStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in whileStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            // Patch the false jump to point to else clause (normal completion)
            var loopEnd = _instructions.Count;
            _instructions[jumpIfFalse] = new ByteCodeInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, normalCompletionPoint);
            
            // Note: Break statements need to jump past else clause to loopEnd
        }
        
        /// <summary>
        /// CPython-style for loop compilation - uses FOR_ITER opcode with proper StopIteration handling
        /// </summary>
        private void CompileFor(ForStatement forStmt)
        {
            // CPython approach with loop-else support
            
            // 1. Get iterator from iterable
            CompileExpression(forStmt.Iter);  // Push iterable on stack
            EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator
            
            // 2. Loop start - FOR_ITER will handle next() and StopIteration
            var forIterInstruction = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // Jump target will be patched later
            
            // 3. FOR_ITER pushes the next value on stack, store it in loop variable
            EmitStoreName(forStmt.Target);
            
            // 4. Execute loop body
            foreach (var stmt in forStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // 5. Jump back to FOR_ITER (not GET_ITER) - CPython 3.12 style relative offset
            // JUMP_BACKWARD argument = number of instructions to jump backward
            int currentPos = _instructions.Count;
            int relativeOffset = currentPos - forIterInstruction;
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, relativeOffset);
            
            // 6. CPython 3.12 방식: END_FOR 추가 (통합 구조)
            var endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            
            // 7. Loop completed normally - execute else clause if present
            if (forStmt.ElseClause != null && forStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            // 8. Patch FOR_ITER to jump to END_FOR (CPython 3.12 통합 방식)
            var relativeJump = endForPosition - forIterInstruction - 1;
            _instructions[forIterInstruction] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER, relativeJump);
            Console.WriteLine($"    → FOR_ITER 패치 (일반 루프): loop start {forIterInstruction}, jump offset {relativeJump}, END_FOR at {endForPosition}");
            
            // Note: Break statements will need to jump past the else clause to loopEnd
            // This requires break handling to be aware of loop-else structure
        }
        
        /// <summary>
        /// Compile for loop with tuple unpacking (e.g., for key, value in items:)
        /// </summary>
        private void CompileForTuple(ForTupleStatement forTupleStmt)
        {
            // CPython approach with tuple unpacking support
            
            // 1. Get iterator from iterable
            CompileExpression(forTupleStmt.Iter);  // Push iterable on stack
            EmitInstruction(ByteCodeOp.GET_ITER); // Convert to iterator
            
            // 2. Loop start - FOR_ITER will handle next() and StopIteration
            var forIterInstruction = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // Jump target will be patched later
            
            // 3. FOR_ITER pushes the next value on stack, unpack it into target variables
            // The value is a tuple/list, we need to unpack it
            EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, forTupleStmt.Targets.Count);
            
            // 4. Store each unpacked value in the target variables (in correct order)
            // UNPACK_SEQUENCE pushes items in reverse order, so we store them in forward order
            for (int i = 0; i < forTupleStmt.Targets.Count; i++)
            {
                EmitStoreName(forTupleStmt.Targets[i]);
            }
            
            // 5. Execute loop body
            foreach (var stmt in forTupleStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // 6. Jump back to FOR_ITER - CPython 3.12 style relative offset
            // JUMP_BACKWARD argument = number of instructions to jump backward
            int currentPos = _instructions.Count;
            int relativeOffset = currentPos - forIterInstruction;
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, relativeOffset);
            
            // 7. CPython 3.12 방식: END_FOR 추가 (통합 구조)
            var endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            
            // 8. Loop completed normally - execute else clause if present
            if (forTupleStmt.ElseClause != null && forTupleStmt.ElseClause.Count > 0)
            {
                foreach (var stmt in forTupleStmt.ElseClause)
                {
                    CompileStatement(stmt);
                }
            }
            
            // 9. Patch FOR_ITER to jump to END_FOR (CPython 3.12 통합 방식)
            var relativeJump = endForPosition - forIterInstruction - 1;
            _instructions[forIterInstruction] = new ByteCodeInstruction(ByteCodeOp.FOR_ITER, relativeJump);
            Console.WriteLine($"    → FOR_ITER 패치 (튜플 루프): loop start {forIterInstruction}, jump offset {relativeJump}, END_FOR at {endForPosition}");
        }
        
        /// <summary>
        /// CPython-style exception handling compilation - fixed implementation
        /// </summary>
        private void CompileTry(TryStatement tryStmt)
        {
            // CPython-style exception handling implementation
            
            var endLabel = CreateLabel("try_end");
            var handlersStartLabel = CreateLabel("handlers_start");
            
            // Setup single exception handler for all except clauses (CPython style)
            EmitInstruction(ByteCodeOp.SETUP_EXCEPT, 0);
            handlersStartLabel.References.Add(_instructions.Count - 1);
            
            // Compile try body
            foreach (var stmt in tryStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            // Pop exception handler and jump to end (no exception case)
            EmitInstruction(ByteCodeOp.POP_EXCEPT);
            EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
            endLabel.References.Add(_instructions.Count - 1);
            
            // Mark start of exception handlers (CPython style)
            MarkLabel(handlersStartLabel);
            
            // Compile exception handlers sequentially
            for (int i = 0; i < tryStmt.Handlers.Count; i++)
            {
                var handler = tryStmt.Handlers[i];
                var nextHandlerLabel = (i < tryStmt.Handlers.Count - 1) 
                    ? CreateLabel($"handler_{i+1}")
                    : CreateLabel("reraise"); // Last handler - reraise if no match
                
                // Exception is on stack - check if it matches handler type
                if (handler.Type != null)
                {
                    // Duplicate exception for matching
                    EmitInstruction(ByteCodeOp.DUP_TOP);
                    CompileExpression(handler.Type);
                    
                    if (handler.IsStar)
                    {
                        // PEP 654: Exception group matching
                        EmitInstruction(ByteCodeOp.CHECK_EG_MATCH);
                        // Stack: [matched, remainder]
                        
                        // Store matched group if handler has name
                        if (handler.Name != null)
                        {
                            EmitInstruction(ByteCodeOp.STORE_FAST, GetOrAddVarName(handler.Name));
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.POP_TOP); // Discard matched
                        }
                        
                        // Check if there was a match
                        EmitInstruction(ByteCodeOp.DUP_TOP);
                        EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(PyNone.Instance));
                        EmitInstruction(ByteCodeOp.COMPARE_OP, 3); // IS_NOT
                        
                        // If no match, try next handler
                        EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0);
                        nextHandlerLabel.References.Add(_instructions.Count - 1);
                        
                        // Execute handler body
                        foreach (var stmt in handler.Body)
                        {
                            CompileStatement(stmt);
                        }
                        
                        EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
                        endLabel.References.Add(_instructions.Count - 1);
                    }
                    else
                    {
                        // Regular exception matching
                        EmitInstruction(ByteCodeOp.EXCEPT_MATCH);
                        
                        // If no match, try next handler
                        EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0);
                        nextHandlerLabel.References.Add(_instructions.Count - 1);
                        
                        // Store exception if handler has name
                        if (handler.Name != null)
                        {
                            EmitInstruction(ByteCodeOp.STORE_NAME, AddName(handler.Name));
                        }
                        else
                        {
                            EmitInstruction(ByteCodeOp.POP_TOP); // Discard exception
                        }
                        
                        // Execute handler body
                        foreach (var stmt in handler.Body)
                        {
                            CompileStatement(stmt);
                        }
                        
                        EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
                        endLabel.References.Add(_instructions.Count - 1);
                    }
                }
                else
                {
                    // Bare except - catches everything (no need to check next handler)
                    if (handler.Name != null)
                    {
                        EmitInstruction(ByteCodeOp.STORE_NAME, AddName(handler.Name));
                    }
                    else
                    {
                        EmitInstruction(ByteCodeOp.POP_TOP); // Discard exception
                    }
                    
                    // Execute handler body
                    foreach (var stmt in handler.Body)
                    {
                        CompileStatement(stmt);
                    }
                    
                    EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
                    endLabel.References.Add(_instructions.Count - 1);
                }
                
                // Mark next handler label (for sequential checking)
                if (i < tryStmt.Handlers.Count - 1)
                {
                    MarkLabel(nextHandlerLabel);
                }
            }
            
            // If no handler matched, reraise the exception
            if (tryStmt.Handlers.Count > 0)
            {
                var reraiseLabel = CreateLabel("reraise");
                MarkLabel(reraiseLabel);
                EmitInstruction(ByteCodeOp.RAISE_VARARGS, 0); // Reraise current exception
            }
            
            // End of try-except
            MarkLabel(endLabel);
        }
        private void CompileWith(WithStatement withStmt)
        {
            // CPython 3.12 compatible implementation
            // Support both single and multiple context managers
            if (withStmt.Items.Count == 1)
            {
                // Single context manager - original implementation
                CompileSingleWith(withStmt);
            }
            else if (withStmt.Items.Count > 1)
            {
                // Multiple context managers - transform to nested with statements
                CompileMultipleWith(withStmt);
            }
            else
            {
                throw PySyntaxError.Create("with statement requires at least one context manager");
            }
        }
        
        /// <summary>
        /// Compile single context manager (original implementation)
        /// </summary>
        private void CompileSingleWith(WithStatement withStmt)
        {
            var item = withStmt.Items[0];
            
            // CPython 3.12 approach with proper exception handling:
            // 1. Load context manager
            CompileExpression(item.ContextExpr);
            
            // 2. BEFORE_WITH: Load __exit__ to stack, call __enter__(), push result
            EmitInstruction(ByteCodeOp.BEFORE_WITH);
            
            // 3. CPython 3.12: Handle __enter__ result immediately after BEFORE_WITH
            if (item.OptionalVars != null)
            {
                // For simple variables, just emit STORE_NAME
                if (item.OptionalVars is NameExpression varExpr)
                {
                    EmitInstruction(ByteCodeOp.STORE_NAME, AddName(varExpr.Name));
                }
                else
                {
                    // Complex assignment target not supported yet
                    EmitInstruction(ByteCodeOp.POP_TOP);
                }
            }
            else
            {
                // Discard __enter__ result if no 'as' clause - CPython 3.12 does this immediately
                EmitInstruction(ByteCodeOp.POP_TOP);
            }
            
            // 4. Setup Exception Table entry (CPython 3.12 compatible)
            var withCleanupLabel = CreateLabel("with_cleanup");
            var bodyStartOffset = _instructions.Count;
            
            // 5. Execute body
            foreach (var stmt in withStmt.Body)
            {
                CompileStatement(stmt);
            }
            
            var bodyEndOffset = _instructions.Count;
            
            // 6. Register Exception Table entry (CPython 3.12 style)
            var handlerOffset = _instructions.Count + 7; // handler starts after normal path
            var entry = new ExceptionTableEntry(
                start: bodyStartOffset,
                end: bodyEndOffset, 
                handler: handlerOffset,
                depth: 1,
                lasti: true
            );
            _exceptionTable.Add(entry);
            
            Console.WriteLine($"🔧 Exception Table Entry Created:");
            Console.WriteLine($"   Start: {bodyStartOffset}, End: {bodyEndOffset}");
            Console.WriteLine($"   Handler: {handlerOffset}, Depth: 1");
            
            // 7. Normal exit: call __exit__(None, None, None) - no POP_EXCEPT needed
            EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(PyNone.Instance));
            EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(PyNone.Instance)); 
            EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(PyNone.Instance));
            EmitInstruction(ByteCodeOp.CALL_FUNCTION, 3); // call __exit__(None, None, None)
            EmitInstruction(ByteCodeOp.POP_TOP); // discard __exit__ return value
            
            var endLabel = CreateLabel("with_end");
            EmitInstruction(ByteCodeOp.JUMP_FORWARD, 0);
            endLabel.References.Add(_instructions.Count - 1);
            
            // 7. Exception handler (CPython 3.12: PUSH_EXC_INFO → WITH_EXCEPT_START)
            MarkLabel(withCleanupLabel);
            EmitInstruction(ByteCodeOp.PUSH_EXC_INFO);
            EmitInstruction(ByteCodeOp.WITH_EXCEPT_START);
            EmitInstruction(ByteCodeOp.POP_JUMP_IF_TRUE, 0);
            var suppressLabel = CreateLabel("suppress_exception");
            suppressLabel.References.Add(_instructions.Count - 1);
            
            // Re-raise exception if not suppressed
            EmitInstruction(ByteCodeOp.RAISE_VARARGS, 0);
            
            // Exception suppressed - continue normally (CPython 3.12 compatible)
            MarkLabel(suppressLabel);
            EmitInstruction(ByteCodeOp.POP_TOP);     // First cleanup - remove True from WITH_EXCEPT_START
            
            MarkLabel(endLabel);
            
            // CPython 3.12 uses Exception Table instead of SETUP_EXCEPT
            // SharpPy's existing exception handling should work correctly
        }
        
        /// <summary>
        /// Compile multiple context managers: with a, b, c: body
        /// CPython 3.12 approach: Transform to nested with statements recursively
        /// with a, b: body -> with a: (with b: body)
        /// </summary>
        private void CompileMultipleWith(WithStatement withStmt)
        {
            // CPython 3.12: Convert "with a, b, c: body" to nested structure
            // Create nested WithStatement objects and compile recursively
            
            if (withStmt.Items.Count < 2)
            {
                throw new InvalidOperationException("CompileMultipleWith requires at least 2 context managers");
            }
            
            // Take the first context manager
            var outerItem = withStmt.Items[0];
            
            // Create inner with statement with remaining context managers
            var remainingItems = withStmt.Items.Skip(1).ToList();
            WithStatement innerWith;
            
            if (remainingItems.Count == 1)
            {
                // Base case: create simple with statement for the last context manager
                innerWith = new WithStatement(remainingItems, withStmt.Body);
            }
            else
            {
                // Recursive case: create nested with statement
                innerWith = new WithStatement(remainingItems, withStmt.Body);
            }
            
            // Create outer with statement that wraps the inner one
            var outerWith = new WithStatement(
                new List<WithItem> { outerItem },
                new List<Statement> { innerWith }
            );
            
            // Compile the outer with statement (which will recursively compile inner ones)
            CompileSingleWith(outerWith);
        }
        private void CompileMatch(MatchStatement matchStmt)
        {
            // CPython 3.12: Match statement compilation using dedicated opcodes
            
            // Compile and keep the subject on stack
            CompileExpression(matchStmt.Subject);
            
            // Create labels for control flow
            var endLabel = CreateLabel($"match_end_{_labelCounter++}");
            var noMatchLabel = CreateLabel($"no_match_{_labelCounter++}");
            var caseLabels = new List<Label>();
            
            // Generate labels for each case
            for (int i = 0; i < matchStmt.Cases.Count; i++)
            {
                caseLabels.Add(CreateLabel($"case_{i}_{_labelCounter++}"));
            }
            
            // Compile each case
            for (int i = 0; i < matchStmt.Cases.Count; i++)
            {
                var matchCase = matchStmt.Cases[i];
                var caseLabel = caseLabels[i];
                var nextLabel = (i + 1 < caseLabels.Count) ? caseLabels[i + 1] : noMatchLabel;
                
                Console.WriteLine($"🔍 Compiling case {i}: Pattern={matchCase.Pattern?.GetType().Name} - {matchCase.Pattern}");
                
                // Place case label
                PlaceLabel(caseLabel);
                
                // CPython 3.12: Copy subject only if stack has content
                // Stack should have: [original_subject]
                
                // Compile pattern matching - this will handle its own subject duplication
                if (!CompilePatternMatch(matchCase.Pattern, nextLabel))
                {
                    // If pattern compilation failed, skip to next case
                    EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, nextLabel);
                    continue;
                }
                
                // Check guard condition if present
                if (matchCase.Guard != null)
                {
                    // At this point we have [original_subject] on stack
                    // Guard should not consume the subject
                    EmitInstruction(ByteCodeOp.DUP_TOP); // [subject, subject] 
                    CompileExpression(matchCase.Guard);
                    // Guard uses second subject, leaves first one
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, nextLabel);
                    // Stack: [original_subject]
                }
                
                // Pattern matched and guard passed - execute case body
                // Pop the subject since we don't need it anymore for this case
                EmitInstruction(ByteCodeOp.POP_TOP);
                
                // Compile case body
                foreach (var stmt in matchCase.Body)
                {
                    CompileStatement(stmt);
                }
                
                // Jump to end after successful case
                EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, endLabel);
            }
            
            // Place no match label - clean up subject when no case matched
            PlaceLabel(noMatchLabel);
            EmitInstruction(ByteCodeOp.POP_TOP); // Clean up subject
            
            // Place end label - control flow joins here after match or no match
            PlaceLabel(endLabel);
        }
        
        
        /// <summary>
        /// CPython 3.12: Compile pattern matching using proper opcodes
        /// Returns true if pattern was compiled successfully, false otherwise
        /// </summary>
        private bool CompilePatternMatch(Expression pattern, Label failLabel)
        {
            Console.WriteLine($"🔍 CompilePatternMatch: {pattern?.GetType().Name} - {pattern}");
            switch (pattern)
            {
                case ConstantExpression constExpr:
                    // CPython 3.12: Direct constant comparison
                    // Stack: [subject] -> [subject, constant] -> [subject, result]
                    EmitInstruction(ByteCodeOp.DUP_TOP); // Duplicate subject for comparison
                    CompileExpression(constExpr);
                    EmitComparison(CompareOp.EQ);
                    // Stack: [subject, comparison_result]
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                    // Stack: [subject] (comparison result popped, subject preserved)
                    return true;
                
                case NameExpression nameExpr when nameExpr.Name == "_":
                    // Wildcard pattern - always matches, no binding
                    return true;
                    
                case NameExpression nameExpr:
                    // Variable binding pattern - always matches, binds subject to variable
                    // Stack: [subject] -> [] (subject consumed by store)
                    EmitStoreName(nameExpr.Name);
                    return true;
                
                case BinaryOpExpression binaryOp when binaryOp.Operator == "|":
                    // Handle BinaryOpExpression with OR operator as OrPattern
                    Console.WriteLine($"🔍 BinaryOpExpression OR converted to OrPattern: {binaryOp.Left} | {binaryOp.Right}");
                    var binaryPatterns = new List<Expression> { binaryOp.Left, binaryOp.Right };
                    return CompileOrPatternLogic(binaryPatterns, failLabel);
                    
                case OrPattern orPattern:
                    // CPython 3.12: Or patterns (PEP 634)
                    Console.WriteLine($"🔍 OrPattern detected with {orPattern.Patterns.Count} patterns");
                    return CompileOrPatternLogic(orPattern.Patterns, failLabel);
                    
                    // For simple constant or patterns like: case 1 | 2 | 3:
                    // Generate: subject == 1 or subject == 2 or subject == 3
                    var constantPatterns = new List<ConstantExpression>();
                    
                    // Check if all patterns are constants using more flexible approach
                    bool allConstants = true;
                    foreach (var subPattern in orPattern.Patterns)
                    {
                        // Try multiple ways to identify constant patterns
                        if (subPattern is ConstantExpression constPattern)
                        {
                            constantPatterns.Add(constPattern);
                        }
                        else if (subPattern.GetType().Name.Contains("Constant"))
                        {
                            // Type name contains "Constant" - try cast
                            try 
                            {
                                var castPattern = (ConstantExpression)subPattern;
                                constantPatterns.Add(castPattern);
                            }
                            catch
                            {
                                allConstants = false;
                                break;
                            }
                        }
                        else
                        {
                            allConstants = false;
                            break;
                        }
                    }
                    
                    if (allConstants && constantPatterns.Count > 0)
                    {
                        // CPython 3.12: Use multiple comparisons with OR short-circuiting
                        // Stack: subject
                        var successLabel = CreateLabel("or_match_success");
                        
                        for (int i = 0; i < constantPatterns.Count; i++)
                        {
                            var isLast = (i == constantPatterns.Count - 1);
                            
                            // Duplicate subject for comparison (except for last one)
                            if (!isLast)
                            {
                                EmitInstruction(ByteCodeOp.DUP_TOP);
                            }
                            
                            // Compare with constant
                            CompileExpression(constantPatterns[i]);
                            EmitComparison(CompareOp.EQ);
                            
                            // If match, jump to success
                            if (!isLast)
                            {
                                EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, successLabel);
                            }
                            else
                            {
                                // Last comparison - if false, jump to fail
                                EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                            }
                        }
                        
                        PlaceLabel(successLabel);
                        // Pop subject since pattern matched
                        EmitInstruction(ByteCodeOp.POP_TOP);
                        return true;
                    }
                    else
                    {
                        // Fallback: Handle any number of constant patterns using type inspection
                        var fallbackConstants = new List<ConstantExpression>();
                        bool allFallbackConstants = true;
                        
                        foreach (var fallbackPattern in orPattern.Patterns)
                        {
                            if (fallbackPattern.GetType().Name.Contains("Constant"))
                            {
                                try
                                {
                                    var constPattern = (ConstantExpression)fallbackPattern;
                                    fallbackConstants.Add(constPattern);
                                }
                                catch
                                {
                                    allFallbackConstants = false;
                                    break;
                                }
                            }
                            else
                            {
                                allFallbackConstants = false;
                                break;
                            }
                        }
                        
                        if (allFallbackConstants && fallbackConstants.Count > 0)
                        {
                            // CPython 3.12: Generate comparisons for all patterns
                            var successLabel = CreateLabel("or_match_success");
                            
                            for (int i = 0; i < fallbackConstants.Count; i++)
                            {
                                var isLast = (i == fallbackConstants.Count - 1);
                                
                                // Duplicate subject for comparison (except for last one)
                                if (!isLast)
                                {
                                    EmitInstruction(ByteCodeOp.DUP_TOP);
                                }
                                
                                // Compare with constant
                                CompileExpression(fallbackConstants[i]);
                                EmitComparison(CompareOp.EQ);
                                
                                // If match, jump to success
                                if (!isLast)
                                {
                                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, successLabel);
                                }
                                else
                                {
                                    // Last comparison - if false, jump to fail
                                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel);
                                }
                            }
                            
                            PlaceLabel(successLabel);
                            // Pop subject since pattern matched
                            EmitInstruction(ByteCodeOp.POP_TOP);
                            return true;
                        }
                        
                        // Complex or patterns not yet supported
                        return false;
                    }
                    
                default:
                    // Unsupported pattern type for now - fallback to old system
                    return false;
            }
        }
        
        private bool CompileOrPatternLogic(List<Expression> patterns, Label failLabel)
        {
            Console.WriteLine($"🔍 CompileOrPatternLogic: {patterns.Count} patterns");
            for (int i = 0; i < patterns.Count; i++)
            {
                Console.WriteLine($"  Pattern {i}: {patterns[i]}");
            }
            
            if (patterns.Count == 1)
            {
                // Single pattern - just delegate
                return CompilePatternMatch(patterns[0], failLabel);
            }
            
            // CPython 3.12: Use multiple comparisons with OR short-circuiting
            // Stack: [subject] - we need to preserve this throughout
            var successLabel = CreateLabel("or_match_success");
            
            for (int i = 0; i < patterns.Count; i++)
            {
                var isLast = (i == patterns.Count - 1);
                
                // For each pattern, duplicate the subject for comparison
                EmitInstruction(ByteCodeOp.DUP_TOP); // [subject, subject]
                
                // Handle constants and nested OR expressions
                if (patterns[i] is ConstantExpression constExpr)
                {
                    CompileExpression(constExpr); // [subject, subject, constant]
                    EmitComparison(CompareOp.EQ);  // [subject, comparison_result]
                    
                    // If match, jump to success - this consumes the comparison result
                    if (!isLast)
                    {
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, successLabel); // [subject]
                        // Continue to next pattern if no match
                    }
                    else
                    {
                        // Last comparison - if false, jump to fail
                        EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, failLabel); // [subject] if true
                        // Fall through to success if last pattern matches
                    }
                }
                else if (patterns[i] is BinaryOpExpression binaryExpr && binaryExpr.Operator == "|")
                {
                    // Nested OR expression - recursively compile it
                    // We need to pop the duplicate we made and let the nested OR handle subject duplication
                    EmitInstruction(ByteCodeOp.POP_TOP); // [subject] - remove our duplicate
                    
                    // Create nested OR patterns
                    var nestedPatterns = new List<Expression> { binaryExpr.Left, binaryExpr.Right };
                    
                    // Compile nested OR - if it fails, jump to next pattern (or fail if last)
                    var localFailLabel = isLast ? failLabel : CreateLabel($"nested_fail_{i}");
                    
                    if (CompileOrPatternLogic(nestedPatterns, localFailLabel))
                    {
                        if (!isLast)
                        {
                            // If nested OR succeeded, jump to overall success
                            EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, successLabel);
                        }
                        // If it's the last pattern, just fall through to success
                    }
                    else
                    {
                        return false; // Compilation failed
                    }
                    
                    if (!isLast)
                    {
                        PlaceLabel(localFailLabel);
                        // Continue with next pattern
                    }
                }
                else
                {
                    // For other non-constant patterns, pop the duplicate and fail for now
                    EmitInstruction(ByteCodeOp.POP_TOP); // [subject]
                    EmitJumpToLabel(ByteCodeOp.JUMP_FORWARD, failLabel);
                    return false;
                }
            }
            
            PlaceLabel(successLabel);
            // Stack should have [subject] here - the successful match consumes comparison result but leaves subject
            return true;
        }
        
        private void CompileAssert(AssertStatement assert) { /* TODO */ }
        private void CompileRaise(RaiseStatement raise)
        {
            if (raise.Exc != null)
            {
                // raise Exception(...) - compile the exception expression
                CompileExpression(raise.Exc);
                EmitInstruction(ByteCodeOp.RAISE_VARARGS, 1);
            }
            else
            {
                // bare raise - re-raise current exception
                EmitInstruction(ByteCodeOp.RERAISE);
            }
        }
        private void CompileDelete(DeleteStatement delete) { /* TODO */ }
        private void CompileGlobal(GlobalStatement global) { /* TODO */ }
        private void CompileNonlocal(NonlocalStatement nonlocal) { /* TODO */ }
        private void CompileBoolOp(BoolOpExpression boolOp)
        {
            // CPython 3.12: Boolean operations with short-circuiting
            // For 'and': use POP_JUMP_IF_FALSE to skip rest if falsy
            // For 'or': use POP_JUMP_IF_TRUE to skip rest if truthy
            
            if (boolOp.Values.Count < 2)
            {
                // Single operand, just compile it
                if (boolOp.Values.Count == 1)
                {
                    CompileExpression(boolOp.Values[0]);
                }
                return;
            }
            
            var endLabel = CreateLabel($"bool_end_{_labelCounter++}");
            
            // Compile all operands except the last with short-circuit logic
            for (int i = 0; i < boolOp.Values.Count - 1; i++)
            {
                CompileExpression(boolOp.Values[i]);
                
                // CPython 3.12: DUP_TOP (similar to COPY 1)
                EmitInstruction(ByteCodeOp.DUP_TOP);
                
                if (boolOp.Op == "and")
                {
                    // For 'and': if current value is falsy, jump to end (short-circuit)
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_FALSE, endLabel);
                }
                else if (boolOp.Op == "or")
                {
                    // For 'or': if current value is truthy, jump to end (short-circuit)
                    EmitJumpToLabel(ByteCodeOp.POP_JUMP_IF_TRUE, endLabel);
                }
                
                // If we didn't short-circuit, pop the duplicate and continue
                EmitInstruction(ByteCodeOp.POP_TOP);
            }
            
            // Compile the last operand (no short-circuit needed)
            CompileExpression(boolOp.Values[boolOp.Values.Count - 1]);
            
            // Place end label
            PlaceLabel(endLabel);
        }
        private void CompileLambda(LambdaExpression lambda)
        {
            // CPython 3.12 compatible lambda compilation
            // Lambda creates an anonymous function object with proper parameter scope
            
            // Create a unique name for the lambda function
            string lambdaName = $"<lambda_{_lambdaCounter++}>";
            
            // Phase 1: Free variable analysis
            var analyzer = new FreeVariableAnalyzer();
            var (freeVars, cellVars) = analyzer.AnalyzeScope(lambda.Body, lambda.Args);
            
            Console.WriteLine($"\n🔍 Lambda analysis: {lambdaName}");
            Console.WriteLine($"  Parameters: [{string.Join(", ", lambda.Args)}]");
            Console.WriteLine($"  Free variables: [{string.Join(", ", freeVars)}]");
            Console.WriteLine($"  Cell variables: [{string.Join(", ", cellVars)}]");
            
            // Compile lambda body in a separate compiler context
            var lambdaInstructions = new List<ByteCodeInstruction>();
            var lambdaConstants = new List<PyObject>();
            var lambdaNames = new List<string>();
            
            // Save current compiler state
            var tempInstructions = _instructions;
            var tempConstants = _constants;
            var tempNames = _names;
            var tempVarNames = _varNames; // Save current VarNames
            
            // Set up lambda compiler context
            _instructions = lambdaInstructions;
            _constants = lambdaConstants;
            _names = lambdaNames;
            _varNames = new List<string>(); // Fresh VarNames for lambda
            
            // CPython 3.12: Parameters must be first in VarNames for LOAD_FAST to work
            foreach (var arg in lambda.Args)
            {
                _varNames.Add(arg);
                Console.WriteLine($"  → Added parameter '{arg}' as FAST variable at index {_varNames.Count - 1}");
            }
            
            // Set up closure compilation if there are free variables
            if (freeVars.Count > 0)
            {
                SetupClosureCompilation(cellVars, freeVars);
            }
            
            // Phase 2: Cell 변수들을 위한 MAKE_CELL 명령어 발행 (람다 파라미터용)
            foreach (var cellVar in cellVars)
            {
                var paramIndex = lambda.Args.IndexOf(cellVar);
                if (paramIndex >= 0)
                {
                    Console.WriteLine($"  → Making cell for lambda parameter: {cellVar}");
                    EmitInstruction(ByteCodeOp.MAKE_CELL, paramIndex);
                }
            }
            
            // Compile the lambda body expression - parameters will now be recognized as FAST variables
            CompileExpression(lambda.Body);
            EmitInstruction(ByteCodeOp.RETURN_VALUE);
            
            // Restore original compiler context
            _instructions = tempInstructions;
            _constants = tempConstants;
            _names = tempNames;
            _varNames = tempVarNames; // Restore original VarNames
            
            // CPython 3.12: Create function code object with correct VarNames order
            // VarNames = parameters first, then any local variables used in lambda body
            var lambdaVarNames = new List<string>(lambda.Args);
            
            // Add any additional local variables that were used (beyond parameters)
            foreach (var name in lambdaNames)
            {
                if (!lambdaVarNames.Contains(name))
                {
                    lambdaVarNames.Add(name);
                }
            }
            
            var functionCode = new PyCodeObject(
                lambdaName,
                lambdaInstructions,
                lambdaConstants,
                lambdaNames,
                lambdaVarNames, // VarNames with parameters first
                lambda.Args.Count,
                freeVars, // Set FreeVars for closure support
                cellVars  // Set CellVars for closure support
            );
            
            Console.WriteLine($"  → Lambda code object created: {lambdaVarNames.Count} variables, {lambda.Args.Count} parameters");
            
            // Handle closure creation if there are free variables
            if (freeVars.Count > 0)
            {
                Console.WriteLine($"  → Creating closure with {freeVars.Count} free variables");
                
                // Load closure cells for free variables
                foreach (var freeVar in freeVars)
                {
                    // Emit LOAD_CLOSURE for each free variable
                    // For Phase 1, we'll emit a placeholder (Phase 2 will implement proper cell loading)
                    EmitInstruction(ByteCodeOp.LOAD_CLOSURE, 0); // TODO: proper cell index
                }
                
                // Build tuple of closure cells
                EmitInstruction(ByteCodeOp.BUILD_TUPLE, freeVars.Count);
            }
            
            // Load the function code object
            EmitInstruction(ByteCodeOp.LOAD_CONST, AddConstant(functionCode));
            
            // Create function with or without closure
            int flags = freeVars.Count > 0 ? 8 : 0; // MAKE_FUNCTION_CLOSURE flag
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, flags);
        }
        
        // Lambda counter for unique names
        private static int _lambdaCounter = 0;
        private void CompileConditional(ConditionalExpression conditional) { /* TODO */ }
        private void CompileFString(FStringExpression fstring)
        {
            // f-string은 여러 파트로 구성됨: 문자열과 표현식이 번갈아 나타남
            // 각 파트를 컴파일하고 FORMAT_VALUE로 포매팅한 후 BUILD_STRING으로 합침
            
            var values = fstring.Values;
            if (values == null || values.Count == 0)
            {
                // 빈 f-string은 빈 문자열
                EmitLoadConst(new PyString(""));
                return;
            }
            
            // 각 value를 컴파일
            foreach (var value in values)
            {
                CompileExpression(value);
                
                // 상수 문자열이 아닌 경우 FORMAT_VALUE 적용
                if (!(value is ConstantExpression constant && constant.Value is PyString))
                {
                    EmitInstruction(ByteCodeOp.FORMAT_VALUE);
                }
            }
            
            // 모든 부분을 문자열로 연결
            if (values.Count > 1)
            {
                EmitInstruction(ByteCodeOp.BUILD_STRING, values.Count);
            }
        }
        
        /// <summary>
        /// FormattedValue 컴파일 - 포맷 지정자를 지원하는 f-string 값
        /// </summary>
        private void CompileFormattedValue(FormattedValue formatted)
        {
            // 1. 값 표현식을 컴파일
            CompileExpression(formatted.Value);
            
            // 2. 포맷 지정자가 있으면 포맷팅 적용
            if (!string.IsNullOrEmpty(formatted.FormatSpec))
            {
                // 포맷 지정자를 상수로 스택에 푸시
                EmitLoadConst(new PyString(formatted.FormatSpec));
                
                // FORMAT_VALUE_WITH_SPEC 명령어 (또는 기본 FORMAT_VALUE)
                // CPython에서는 FORMAT_VALUE 명령어가 포맷 옵션을 받음
                EmitInstruction(ByteCodeOp.FORMAT_VALUE, 4); // 4 = format spec 있음
            }
            else
            {
                // 포맷 지정자 없음 - 기본 FORMAT_VALUE
                EmitInstruction(ByteCodeOp.FORMAT_VALUE, 0);
            }
        }
        
        // Evaluate 메서드 - 나중에 구현
        public PyObject Evaluate(PyScope scope)
        {
            throw new NotImplementedException("PythonCompiler.Evaluate() - 나중에 구현예정");
        }
        
        #region CPython-style Compiler Helper Methods
        
        /// <summary>
        /// Label management for jumps (CPython style)
        /// </summary>
        private class Label
        {
            public string Name { get; }
            public int Offset { get; set; } = -1;
            public bool IsMarked => Offset >= 0;
            public List<int> References { get; } = new();
            
            public Label(string name)
            {
                Name = name;
            }
        }
        
        private Dictionary<string, Label> _labels = new();
        private int _labelCounter = 0;
        
        private Label CreateLabel(string prefix)
        {
            var name = $"{prefix}_{_labelCounter++}";
            var label = new Label(name);
            _labels[name] = label;
            return label;
        }
        
        private void MarkLabel(Label label)
        {
            label.Offset = _instructions.Count;
            
            // Update all references to this label
            foreach (var refIndex in label.References)
            {
                var oldInstruction = _instructions[refIndex];
                _instructions[refIndex] = new ByteCodeInstruction(oldInstruction.OpCode, label.Offset);
            }
        }
        
        /// <summary>
        /// CPython 3.12 style: Emit jump instruction to a label
        /// </summary>
        private void EmitJumpToLabel(ByteCodeOp jumpOp, Label label)
        {
            EmitInstruction(jumpOp, 0);
            label.References.Add(_instructions.Count - 1);
        }
        
        /// <summary>
        /// CPython 3.12 style: Place/mark a label (alias for MarkLabel for consistency)
        /// </summary>
        private void PlaceLabel(Label label)
        {
            MarkLabel(label);
        }
        
        /// <summary>
        /// CPython 3.12 style: Emit comparison operation
        /// </summary>
        private void EmitComparison(CompareOp compareOp)
        {
            EmitInstruction(ByteCodeOp.COMPARE_OP, (int)compareOp);
        }
        
        /// <summary>
        /// CPython 3.12 style aliases for consistency
        /// </summary>
        private void EmitLoadConstant(PyObject value) => EmitLoadConst(value);
        private void EmitLoadVariable(string name) => EmitLoadName(name);
        private void EmitStoreVariable(string name) => EmitStoreName(name);
        private void EmitLoadAttribute(string attrName) => EmitLoadAttr(attrName);
        private void EmitStoreAttribute(string attrName) => EmitStoreAttr(attrName);
        
        
        /// <summary>
        /// Loop context management for break/continue (CPython style)
        /// </summary>
        private class LoopContext
        {
            public Label BreakLabel { get; }
            public Label ContinueLabel { get; }
            
            public LoopContext(Label breakLabel, Label continueLabel)
            {
                BreakLabel = breakLabel;
                ContinueLabel = continueLabel;
            }
        }
        
        private Stack<LoopContext> _loopStack = new();
        
        private void PushLoopContext(Label breakLabel, Label continueLabel)
        {
            _loopStack.Push(new LoopContext(breakLabel, continueLabel));
        }
        
        private void PopLoopContext()
        {
            if (_loopStack.Count > 0)
                _loopStack.Pop();
        }
        
        private LoopContext? GetCurrentLoop()
        {
            return _loopStack.Count > 0 ? _loopStack.Peek() : null;
        }
        
        /// <summary>
        /// Enhanced bytecode emission with proper name/constant management
        /// </summary>
        
        private int GetOrAddVarName(string name)
        {
            var index = _varNames.IndexOf(name);
            if (index == -1)
            {
                _varNames.Add(name);
                index = _varNames.Count - 1;
            }
            return index;
        }
        
        private int GetOrAddName(string name)
        {
            var index = _names.IndexOf(name);
            if (index == -1)
            {
                _names.Add(name);
                index = _names.Count - 1;
            }
            return index;
        }
        
        private int GetOrAddConstant(PyObject constant)
        {
            // Use object reference equality for constants
            for (int i = 0; i < _constants.Count; i++)
            {
                if (ReferenceEquals(_constants[i], constant) || 
                    (constant is PyInt intConst && _constants[i] is PyInt existingInt && intConst.Value == existingInt.Value) ||
                    (constant is PyString strConst && _constants[i] is PyString existingStr && strConst.Value == existingStr.Value))
                {
                    return i;
                }
            }
            
            _constants.Add(constant);
            return _constants.Count - 1;
        }
        
        #endregion
        
        #region PEP 709 Comprehension Optimization
        
        /// <summary>
        /// PEP 709 - List comprehension 바이트코드 인라인 최적화
        /// [expr for var in iterable if condition] → 직접 바이트코드 생성
        /// CPython 호환 방식: GET_ITER는 한 번만, 루프는 FOR_ITER부터 시작
        /// </summary>
        private void CompileListComprehension(ListComprehension listComp)
        {
            Console.WriteLine("🚀 PEP 709: List comprehension 바이트코드 인라인 컴파일 (중첩 Generator 지원)");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;
            
            // 1. 빈 리스트 생성
            EmitInstruction(ByteCodeOp.BUILD_LIST, 0);
            
            // 2. 임시 변수 저장을 위한 리스트 - 컴프리헨션 스코프 isolation
            var comprehensionVars = new List<string>();
            
            // 3. 중첩된 루프 컴파일 - CPython 3.12 방식
            CompileNestedGenerators(listComp.Generators, 0, comprehensionVars, () =>
            {
                // 모든 generator 루프가 완료된 후 실행되는 내부 블록
                CompileExpression(listComp.Element);
                EmitInstruction(ByteCodeOp.LIST_APPEND, 1); // 리스트는 항상 스택의 맨 아래(1)에 위치
            });
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            
            Console.WriteLine($"✅ List comprehension 바이트코드 인라인 완료 ({listComp.Generators.Count}개 중첩 generator)");
        }
        
        /// <summary>
        /// CPython 3.12 호환 중첩 Generator 컴파일
        /// 재귀적으로 각 generator에 대해 FOR_ITER 루프를 생성
        /// </summary>
        private void CompileNestedGenerators(List<Comprehension> generators, int currentIndex, 
                                           List<string> comprehensionVars, Action innerBlock)
        {
            if (currentIndex >= generators.Count)
            {
                // 모든 generator 처리 완료 - 내부 블록 실행
                innerBlock();
                return;
            }
            
            var generator = generators[currentIndex];
            Console.WriteLine($"  🔄 Generator [{currentIndex}]: {generator.Target} in {generator.Iter}");
            
            // 이터레이터 준비
            CompileExpression(generator.Iter);
            EmitInstruction(ByteCodeOp.GET_ITER);
            
            // 루프 시작 라벨
            var loopStart = _instructions.Count;
            EmitInstruction(ByteCodeOp.FOR_ITER, 0); // 패치 대상
            
            // 루프 변수 저장 - 컴프리헨션 변수로 관리
            if (generator.Target is NameExpression nameExpr)
            {
                // CPython 3.12: 컴프리헨션 변수는 임시 스코프에 저장
                EmitStoreComprehensionVar(nameExpr.Name, comprehensionVars);
            }
            else
            {
                throw new NotImplementedException("Complex target patterns not yet supported");
            }
            
            // 조건 검사 (if문이 있는 경우)
            List<int> conditionJumps = new List<int>();
            foreach (var condition in generator.Ifs)
            {
                CompileExpression(condition);
                conditionJumps.Add(_instructions.Count);
                EmitInstruction(ByteCodeOp.POP_JUMP_IF_FALSE, 0); // 조건이 거짓이면 루프 재시작으로 점프
            }
            
            // 다음 generator 재귀 호출
            CompileNestedGenerators(generators, currentIndex + 1, comprehensionVars, innerBlock);
            
            // 다음 generator 재귀 호출 후 JUMP_BACKWARD - CPython 3.12 style relative offset
            // JUMP_BACKWARD argument = number of instructions to jump backward
            int currentPos = _instructions.Count;
            int relativeOffset = currentPos - loopStart;
            EmitInstruction(ByteCodeOp.JUMP_BACKWARD, relativeOffset);
            
            // 조건 점프 대상 패치 - 조건이 거짓이면 FOR_ITER로 점프하여 다음 iteration
            foreach (var jumpIndex in conditionJumps)
            {
                _instructions[jumpIndex] = new ByteCodeInstruction(
                    ByteCodeOp.POP_JUMP_IF_FALSE, 
                    loopStart  // FOR_ITER 위치로 점프
                );
            }
            
            // CPython 3.12 방식: FOR_ITER → END_FOR 점프 구조
            // END_FOR에서 루프 종료 시 정리 작업 수행
            
            Console.WriteLine($"🔧 FOR_ITER 패치 전 상태:");
            Console.WriteLine($"    Generator[{currentIndex}]: {generator.Target} in {generator.Iter}");
            Console.WriteLine($"    현재 바이트코드 길이: {_instructions.Count}");
            Console.WriteLine($"    FOR_ITER 위치: {loopStart}");
            
            // END_FOR 명령어 추가 (CPython 3.12 패턴)
            int endForPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.END_FOR, 0);
            Console.WriteLine($"    END_FOR 추가 위치: {endForPosition}");
            
            // CPython 3.12와 동일한 오프셋 계산
            // FOR_ITER 실행 시: InstructionPointer += argument, 그 후 메인 루프 +1
            // 따라서 END_FOR에 도달하려면: endForPosition - loopStart - 1
            var relativeJump = endForPosition - loopStart - 1;
            var originalInstruction = _instructions[loopStart];
            _instructions[loopStart] = new ByteCodeInstruction(
                ByteCodeOp.FOR_ITER, 
                relativeJump
            );
            Console.WriteLine($"🔧 FOR_ITER 패치 완료:");
            Console.WriteLine($"    위치 {loopStart}: 원래 인수 {originalInstruction.Argument} → 새 인수 {relativeJump}");
            Console.WriteLine($"    점프 계산: END_FOR({endForPosition}) - FOR_ITER({loopStart}) - 1 = {relativeJump}");
            Console.WriteLine($"    VM 실행 시 점프될 위치: {loopStart + 1 + relativeJump}");
            
            // Dict comprehension의 경우 STORE_GLOBAL이 건너뛰어지는 문제 디버깅
            if (_isInComprehension)
            {
                Console.WriteLine($"📋 Comprehension 컨텍스트에서 FOR_ITER 패치:");
                Console.WriteLine($"    다음 명령어들 위치 예상:");
                for (int i = endForPosition + 1; i < Math.Min(endForPosition + 5, _instructions.Count); i++)
                {
                    if (i < _instructions.Count)
                    {
                        Console.WriteLine($"    위치 {i}: {_instructions[i].OpCode} {_instructions[i].Argument}");
                    }
                }
            }
        }
        
        /// <summary>
        /// CPython 3.12 호환 컴프리헨션 변수 저장
        /// 컴프리헨션 내부 변수는 격리된 스코프에서 관리
        /// </summary>
        private void EmitStoreComprehensionVar(string name, List<string> comprehensionVars)
        {
            // 컴프리헨션 변수 목록에 추가 (중복 제거)
            if (!comprehensionVars.Contains(name))
            {
                comprehensionVars.Add(name);
            }
            
            // CPython 3.12 호환: 컴프리헨션 변수는 항상 STORE_FAST로 처리
            // 모듈 레벨에서도 컴프리헨션은 별도의 지역 스코프를 가짐
            var varIndex = GetOrAddVarName(name);
            EmitInstruction(ByteCodeOp.STORE_FAST, varIndex);
            Console.WriteLine($"    → 컴프리헨션 변수 저장: {name} (STORE_FAST index {varIndex})");
        }
        
        /// <summary>
        /// 컴프리헨션 변수 로드 (CPython 3.12 호환)
        /// </summary>
        private void EmitLoadComprehensionVar(string name, List<string> comprehensionVars)
        {
            if (comprehensionVars.Contains(name))
            {
                // 컴프리헨션 변수: LOAD_FAST 사용
                var varIndex = _varNames.IndexOf(name);
                if (varIndex >= 0)
                {
                    EmitInstruction(ByteCodeOp.LOAD_FAST, varIndex);
                    Console.WriteLine($"    → 컴프리헨션 변수 로드: {name} (LOAD_FAST index {varIndex})");
                    return;
                }
            }
            
            // 일반 변수: 기존 로직 사용
            EmitLoadName(name);
        }
        
        /// <summary>
        /// PEP 709 - Dict comprehension 바이트코드 인라인 최적화
        /// {key: value for var in iterable if condition} → 직접 바이트코드 생성
        /// CPython 호환 방식: GET_ITER는 한 번만, 루프는 FOR_ITER부터 시작
        /// </summary>
        private void CompileDictComprehension(DictComprehension dictComp)
        {
            Console.WriteLine("🚀 PEP 709: Dict comprehension 바이트코드 인라인 컴파일 (중첩 Generator 지원)");
            Console.WriteLine($"📊 Dict comprehension 시작 위치: {_instructions.Count}");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;
            
            // 1. 빈 딕셔너리 생성
            var buildMapPosition = _instructions.Count;
            EmitInstruction(ByteCodeOp.BUILD_MAP, 0);
            Console.WriteLine($"🔧 BUILD_MAP 위치: {buildMapPosition}");
            
            // 2. 임시 변수 저장을 위한 리스트 - 컴프리헨션 스코프 isolation
            var comprehensionVars = new List<string>();
            
            // 3. 중첩된 루프 컴파일 - CPython 3.12 방식
            Console.WriteLine($"🔄 CompileNestedGenerators 호출 전 위치: {_instructions.Count}");
            CompileNestedGenerators(dictComp.Generators, 0, comprehensionVars, () =>
            {
                // 모든 generator 루프가 완료된 후 실행되는 내부 블록
                var innerBlockStart = _instructions.Count;
                Console.WriteLine($"🎯 Dict comprehension 내부 블록 시작: {innerBlockStart}");
                
                CompileExpression(dictComp.Key);
                CompileExpression(dictComp.Value);
                
                var mapAddPosition = _instructions.Count;
                EmitInstruction(ByteCodeOp.MAP_ADD, 1); // 딕셔너리는 항상 스택의 맨 아래(1)에 위치
                Console.WriteLine($"🗝️ MAP_ADD 위치: {mapAddPosition}");
            });
            
            var afterNestedGenerators = _instructions.Count;
            Console.WriteLine($"🔄 CompileNestedGenerators 완료 후 위치: {afterNestedGenerators}");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            
            Console.WriteLine($"✅ Dict comprehension 바이트코드 인라인 완료 ({dictComp.Generators.Count}개 중첩 generator)");
            Console.WriteLine($"📊 Dict comprehension 최종 위치: {_instructions.Count}");
        }
        
        /// <summary>
        /// PEP 709 - Set comprehension 바이트코드 인라인 최적화
        /// {expr for var in iterable if condition} → 직접 바이트코드 생성
        /// CPython 호환 방식: GET_ITER는 한 번만, 루프는 FOR_ITER부터 시작
        /// </summary>
        private void CompileSetComprehension(SetComprehension setComp)
        {
            Console.WriteLine("🚀 PEP 709: Set comprehension 바이트코드 인라인 컴파일 (중첩 Generator 지원)");
            
            // CPython 3.12: 컴프리헨션 컨텍스트 시작
            var savedIsInComprehension = _isInComprehension;
            _isInComprehension = true;
            
            // 1. 빈 셋 생성
            EmitInstruction(ByteCodeOp.BUILD_SET, 0);
            
            // 2. 임시 변수 저장을 위한 리스트 - 컴프리헨션 스코프 isolation
            var comprehensionVars = new List<string>();
            
            // 3. 중첩된 루프 컴파일 - CPython 3.12 방식
            CompileNestedGenerators(setComp.Generators, 0, comprehensionVars, () =>
            {
                // 모든 generator 루프가 완료된 후 실행되는 내부 블록
                CompileExpression(setComp.Element);
                EmitInstruction(ByteCodeOp.SET_ADD, 1); // 셋은 항상 스택의 맨 아래(1)에 위치
            });
            
            // CPython 3.12: 컴프리헨션 컨텍스트 종료
            _isInComprehension = savedIsInComprehension;
            
            Console.WriteLine($"✅ Set comprehension 바이트코드 인라인 완료 ({setComp.Generators.Count}개 중첩 generator)");
        }
        
        /// <summary>
        /// PEP 709 - Generator expression 바이트코드 인라인 최적화
        /// (expr for var in iterable if condition) → 제너레이터 함수 생성
        /// </summary>
        private void CompileGeneratorExpression(GeneratorExpression genExp)
        {
            Console.WriteLine("🚀 PEP 709: Generator expression 바이트코드 인라인 컴파일");
            
            // 제너레이터는 별도 함수로 컴파일 필요
            var genCompiler = new PythonCompiler();
            var generator = genExp.Generators[0];
            
            // 제너레이터 바디 컴파일 - 단순화된 구현
            var targetName = generator.Target is NameExpression nameExpr ? nameExpr.Name : "x";
            
            // 제너레이터 바디: for문 + yield
            var forBody = new List<Statement>();
            
            // 조건이 있으면 if문으로 감싸기
            if (generator.Ifs.Count > 0)
            {
                // 모든 조건을 AND로 연결
                Expression combinedCondition = generator.Ifs[0];
                for (int i = 1; i < generator.Ifs.Count; i++)
                {
                    combinedCondition = new BoolOpExpression(
                        "and", 
                        new List<Expression> { combinedCondition, generator.Ifs[i] }
                    );
                }
                
                forBody.Add(new IfStatement(
                    combinedCondition,
                    new List<Statement> { new YieldStatement(genExp.Element) },
                    null
                ));
            }
            else
            {
                forBody.Add(new YieldStatement(genExp.Element));
            }
            
            var genStatements = new List<Statement>
            {
                new ForStatement(targetName, generator.Iter, forBody)
            };
            
            var genCode = genCompiler.Compile(genStatements, "<genexpr>");
            
            // 제너레이터 함수 객체 생성
            EmitLoadConst(genCode);
            EmitInstruction(ByteCodeOp.MAKE_FUNCTION, 0);
            EmitInstruction(ByteCodeOp.CALL_FUNCTION, 0);
            
            Console.WriteLine("✅ Generator expression 바이트코드 인라인 완료");
        }
        
        // CPython 3.12: Assignment target compilation
        private void CompileAssignTarget(AssignTargetStatement assignTarget)
        {
            // Compile the value first
            CompileExpression(assignTarget.Value);
            
            // Handle different assignment targets
            switch (assignTarget.Target)
            {
                case NameExpression name:
                    EmitStoreName(name.Name);
                    break;
                    
                case AttributeExpression attr:
                    CompileExpression(attr.Value);
                    EmitStoreAttr(attr.Attr);
                    break;
                    
                case SubscriptExpression subscript:
                    CompileExpression(subscript.Value);
                    CompileExpression(subscript.Slice);
                    EmitInstruction(ByteCodeOp.STORE_SUBSCR);
                    break;
                    
                case TupleExpression tuple:
                    // CPython 3.12 compatible tuple unpacking assignment: x, y = (1, 2)
                    // Value is already on stack, unpack it
                    EmitInstruction(ByteCodeOp.UNPACK_SEQUENCE, tuple.Elements.Count);
                    
                    // CPython: UNPACK_SEQUENCE pushes elements in reverse order on stack
                    // When we pop for STORE operations, we get them in forward order
                    // So we store in forward order (x first, then y)
                    for (int i = 0; i < tuple.Elements.Count; i++)
                    {
                        var element = tuple.Elements[i];
                        if (element is NameExpression nameExpr)
                        {
                            EmitStoreName(nameExpr.Name);
                        }
                        else
                        {
                            throw new Exception($"Invalid tuple unpacking target: {element.GetType().Name}");
                        }
                    }
                    break;
                    
                default:
                    throw new Exception($"Invalid assignment target: {assignTarget.Target.GetType().Name}");
            }
        }
        
        private void EmitStoreAttr(string attrName)
        {
            var index = AddName(attrName);
            EmitInstruction(ByteCodeOp.STORE_ATTR, index);
        }
        
        /// <summary>
        /// CPython 3.12: Check if we're compiling at module level
        /// </summary>
        private bool IsModuleLevel()
        {
            return _currentFunctionName == null || _currentFunctionName == "<module>";
        }
        
        #endregion
    }

    #endregion
}