// Python/symtable.c of CPython v3.12.15, on the ASDL tree. Function names follow the C file.
using System;
using System.Collections.Generic;
using SharpPy.Generated;

namespace SharpPy
{
    public enum BlockType
    {
        FunctionBlock, ClassBlock, ModuleBlock, AnnotationBlock,
        TypeVarBoundBlock, TypeAliasBlock, TypeParamBlock,
    }

    public enum ComprehensionKind
    {
        NoComprehension = 0, ListComprehension = 1, DictComprehension = 2,
        SetComprehension = 3, GeneratorExpression = 4,
    }

    public sealed class PySTEntry
    {
        public object Id = null!;
        public readonly Dictionary<string, long> Symbols = new();
        public string Name = "";
        public readonly List<string> Varnames = new();
        public List<PySTEntry> Children = new();
        public List<(string Name, int Line, int Column, int EndLine, int EndColumn)>? Directives;
        public BlockType Type;
        public bool Nested, Free, ChildFree, Generator, Coroutine, Varargs, Varkeywords, ReturnsValue;
        public bool NeedsClassClosure, NeedsClassdict, CompInlined, CompIterTarget, CanSeeClassScope;
        public ComprehensionKind Comprehension;
        public int CompIterExpr, Lineno, ColOffset, EndLineno, EndColOffset;
        public PySymtable Table = null!;
        public HashSet<string>? MangledNames;

        public long GetSymbol(string name) => Symbols.TryGetValue(name, out var flags) ? flags : 0;
        public int GetScope(string name) => (int)((GetSymbol(name) >> PySymtable.SCOPE_OFFSET) & PySymtable.SCOPE_MASK);
        public bool IsFunctionLike => Type is BlockType.FunctionBlock or BlockType.TypeVarBoundBlock
            or BlockType.TypeAliasBlock or BlockType.TypeParamBlock;
    }

    public sealed class PySymtable
    {
        public const int DEF_GLOBAL = 1, DEF_LOCAL = 2, DEF_PARAM = 2 << 1, DEF_NONLOCAL = 2 << 2, USE = 2 << 3,
            DEF_FREE = 2 << 4, DEF_FREE_CLASS = 2 << 5, DEF_IMPORT = 2 << 6, DEF_ANNOT = 2 << 7,
            DEF_COMP_ITER = 2 << 8, DEF_TYPE_PARAM = 2 << 9, DEF_COMP_CELL = 2 << 10;
        public const int DEF_BOUND = DEF_LOCAL | DEF_PARAM | DEF_IMPORT;
        public const int SCOPE_OFFSET = 12;
        public const int SCOPE_MASK = DEF_GLOBAL | DEF_LOCAL | DEF_PARAM | DEF_NONLOCAL;
        public const int LOCAL = 1, GLOBAL_EXPLICIT = 2, GLOBAL_IMPLICIT = 3, FREE = 4, CELL = 5;

        private const string NAMED_EXPR_COMP_IN_CLASS = "assignment expression within a comprehension cannot be used in a class body";
        private const string NAMED_EXPR_COMP_IN_TYPEVAR_BOUND = "assignment expression within a comprehension cannot be used in a TypeVar bound";
        private const string NAMED_EXPR_COMP_IN_TYPEALIAS = "assignment expression within a comprehension cannot be used in a type alias";
        private const string NAMED_EXPR_COMP_IN_TYPEPARAM = "assignment expression within a comprehension cannot be used within the definition of a generic";

        private readonly string _filename;
        private readonly bool _futureAnnotations;
        private PySTEntry _cur = null!;
        private readonly Dictionary<object, PySTEntry> _blocks = new(ReferenceEqualityComparer.Instance);
        private readonly List<PySTEntry> _stack = new();
        private Dictionary<string, long> _global = null!;
        private string? _private;
        public PySTEntry Top { get; private set; } = null!;

        private PySymtable(string filename, bool futureAnnotations)
        {
            _filename = filename;
            _futureAnnotations = futureAnnotations;
        }

        // _PySymtable_Build
        public static PySymtable Build(GeneratedMod mod, string filename, bool futureAnnotations)
        {
            var st = new PySymtable(filename, futureAnnotations);
            st.EnterBlock("top", BlockType.ModuleBlock, mod, 0, 0, 0, 0);
            st.Top = st._cur;
            switch (mod)
            {
                case GeneratedModule module:
                    st.VisitStmts(module.Body);
                    break;
                case GeneratedExpression expression:
                    st.VisitExpr(expression.Body);
                    break;
                case GeneratedInteractive interactive:
                    st.VisitStmts(interactive.Body);
                    break;
                default:
                    throw new InvalidOperationException("this compiler does not handle FunctionTypes");
            }
            st.ExitBlock();
            st.Analyze();
            return st;
        }

        public PySTEntry Lookup(object key) => _blocks[key];

        private Exception Error(string message, int line, int column, int endLine, int endColumn) =>
            new SyntaxErrorException(message, _filename, line, column, endLine, endColumn);

        private Exception Error(string message, GeneratedAstNode at) =>
            Error(message, at.LineNo, at.ColOffset + 1, at.EndLineNo, at.EndColOffset + 1);

        private PySTEntry NewEntry(string name, BlockType block, object key, int lineno, int col, int endLineno, int endCol)
        {
            var ste = new PySTEntry
            {
                Table = this, Id = key, Name = name, Type = block,
                Lineno = lineno, ColOffset = col, EndLineno = endLineno, EndColOffset = endCol,
            };
            if (_cur != null && (_cur.Nested || _cur.IsFunctionLike))
                ste.Nested = true;
            _blocks[key] = ste;
            return ste;
        }

        private Exception ErrorAtDirective(PySTEntry ste, string message, string name)
        {
            foreach (var data in ste.Directives!)
            {
                if (data.Name == name)
                    return Error(message, data.Line, data.Column + 1, data.EndLine, data.EndColumn + 1);
            }
            return new InvalidOperationException("BUG: internal directive bookkeeping broken");
        }

        private void AnalyzeName(PySTEntry ste, Dictionary<string, int> scopes, string name, long flags,
            HashSet<string>? bound, HashSet<string> local, HashSet<string> free, HashSet<string> global,
            HashSet<string> typeParams, PySTEntry? classEntry)
        {
            if ((flags & DEF_GLOBAL) != 0)
            {
                if ((flags & DEF_NONLOCAL) != 0)
                    throw ErrorAtDirective(ste, $"name '{name}' is nonlocal and global", name);
                scopes[name] = GLOBAL_EXPLICIT;
                global.Add(name);
                bound?.Remove(name);
                return;
            }
            if ((flags & DEF_NONLOCAL) != 0)
            {
                if (bound == null)
                    throw ErrorAtDirective(ste, "nonlocal declaration not allowed at module level", name);
                if (!bound.Contains(name))
                    throw ErrorAtDirective(ste, $"no binding for nonlocal '{name}' found", name);
                if (typeParams.Contains(name))
                    throw ErrorAtDirective(ste, $"nonlocal binding not allowed for type parameter '{name}'", name);
                scopes[name] = FREE;
                ste.Free = true;
                free.Add(name);
                return;
            }
            if ((flags & DEF_BOUND) != 0)
            {
                scopes[name] = LOCAL;
                local.Add(name);
                global.Remove(name);
                if ((flags & DEF_TYPE_PARAM) != 0)
                    typeParams.Add(name);
                else
                    typeParams.Remove(name);
                return;
            }
            if (classEntry != null)
            {
                long classFlags = classEntry.GetSymbol(name);
                if ((classFlags & DEF_GLOBAL) != 0)
                {
                    scopes[name] = GLOBAL_EXPLICIT;
                    return;
                }
                if ((classFlags & DEF_BOUND) != 0 && (classFlags & DEF_NONLOCAL) == 0)
                {
                    scopes[name] = GLOBAL_IMPLICIT;
                    return;
                }
            }
            if (bound != null && bound.Contains(name))
            {
                scopes[name] = FREE;
                ste.Free = true;
                free.Add(name);
                return;
            }
            if (global.Contains(name))
            {
                scopes[name] = GLOBAL_IMPLICIT;
                return;
            }
            if (ste.Nested)
                ste.Free = true;
            scopes[name] = GLOBAL_IMPLICIT;
        }

        private static bool IsFreeInAnyChild(PySTEntry entry, string key)
        {
            foreach (var child in entry.Children)
            {
                if (child.GetScope(key) == FREE)
                    return true;
            }
            return false;
        }

        private static void InlineComprehension(PySTEntry ste, PySTEntry comp, Dictionary<string, int> scopes,
            HashSet<string> compFree, HashSet<string> inlinedCells)
        {
            bool removeDunderClass = false;
            foreach (var (k, compFlags) in comp.Symbols)
            {
                if ((compFlags & DEF_PARAM) != 0)
                    continue;
                int scope = (int)((compFlags >> SCOPE_OFFSET) & SCOPE_MASK);
                long onlyFlags = compFlags & ((1 << SCOPE_OFFSET) - 1);
                if (scope == CELL || (onlyFlags & DEF_COMP_CELL) != 0)
                    inlinedCells.Add(k);
                bool exists = ste.Symbols.TryGetValue(k, out long existing);
                if (scope == FREE && ste.Type == BlockType.ClassBlock && k == "__class__")
                {
                    scope = GLOBAL_IMPLICIT;
                    compFree.Remove(k);
                    removeDunderClass = true;
                }
                if (!exists)
                {
                    ste.Symbols[k] = onlyFlags;
                    scopes[k] = scope;
                }
                else if ((existing & DEF_BOUND) != 0)
                {
                    if (!IsFreeInAnyChild(comp, k) && ste.Type != BlockType.ClassBlock)
                        compFree.Remove(k);
                }
            }
            comp.Free = compFree.Count > 0;
            if (removeDunderClass)
                comp.Symbols.Remove("__class__");
        }

        private static void AnalyzeCells(Dictionary<string, int> scopes, HashSet<string> free, HashSet<string> inlinedCells)
        {
            foreach (var name in new List<string>(scopes.Keys))
            {
                if (scopes[name] != LOCAL)
                    continue;
                if (!free.Contains(name) && !inlinedCells.Contains(name))
                    continue;
                scopes[name] = CELL;
                free.Remove(name);
            }
        }

        private static void DropClassFree(PySTEntry ste, HashSet<string> free)
        {
            if (free.Remove("__class__"))
                ste.NeedsClassClosure = true;
            if (free.Remove("__classdict__"))
                ste.NeedsClassdict = true;
        }

        private static void UpdateSymbols(Dictionary<string, long> symbols, Dictionary<string, int> scopes,
            HashSet<string>? bound, HashSet<string> free, HashSet<string> inlinedCells, bool classflag)
        {
            foreach (var name in new List<string>(symbols.Keys))
            {
                long flags = symbols[name];
                if (inlinedCells.Contains(name))
                    flags |= DEF_COMP_CELL;
                flags |= (long)scopes[name] << SCOPE_OFFSET;
                symbols[name] = flags;
            }

            foreach (var name in free)
            {
                if (symbols.TryGetValue(name, out long existing))
                {
                    if (classflag)
                        symbols[name] = existing | DEF_FREE_CLASS;
                    continue;
                }
                if (bound != null && !bound.Contains(name))
                    continue;
                symbols[name] = (long)FREE << SCOPE_OFFSET;
            }
        }

        private void AnalyzeBlock(PySTEntry ste, HashSet<string>? bound, HashSet<string> free, HashSet<string> global,
            HashSet<string> typeParams, PySTEntry? classEntry)
        {
            var local = new HashSet<string>();
            var scopes = new Dictionary<string, int>();
            var newglobal = new HashSet<string>();
            var newfree = new HashSet<string>();
            var newbound = new HashSet<string>();
            var inlinedCells = new HashSet<string>();

            if (ste.Type == BlockType.ClassBlock)
            {
                newglobal.UnionWith(global);
                if (bound != null)
                    newbound.UnionWith(bound);
            }

            foreach (var (name, flags) in ste.Symbols)
                AnalyzeName(ste, scopes, name, flags, bound, local, free, global, typeParams, classEntry);

            if (ste.Type != BlockType.ClassBlock)
            {
                if (ste.IsFunctionLike)
                    newbound.UnionWith(local);
                if (bound != null)
                    newbound.UnionWith(bound);
                newglobal.UnionWith(global);
            }
            else
            {
                newbound.Add("__class__");
                newbound.Add("__classdict__");
            }

            foreach (var entry in ste.Children)
            {
                PySTEntry? newClassEntry = null;
                if (entry.CanSeeClassScope)
                {
                    if (ste.Type == BlockType.ClassBlock)
                        newClassEntry = ste;
                    else if (classEntry != null)
                        newClassEntry = classEntry;
                }

                bool inlineComp = entry.Comprehension != ComprehensionKind.NoComprehension && !entry.Generator;
                var childFree = AnalyzeChildBlock(entry, newbound, newfree, newglobal, typeParams, newClassEntry);
                if (inlineComp)
                {
                    InlineComprehension(ste, entry, scopes, childFree, inlinedCells);
                    entry.CompInlined = true;
                }
                newfree.UnionWith(childFree);
                if (entry.Free || entry.ChildFree)
                    ste.ChildFree = true;
            }

            for (int i = ste.Children.Count - 1; i >= 0; i--)
            {
                var entry = ste.Children[i];
                if (entry.CompInlined)
                {
                    ste.Children.RemoveAt(i);
                    ste.Children.InsertRange(i, entry.Children);
                }
            }

            if (ste.IsFunctionLike)
                AnalyzeCells(scopes, newfree, inlinedCells);
            else if (ste.Type == BlockType.ClassBlock)
                DropClassFree(ste, newfree);
            UpdateSymbols(ste.Symbols, scopes, bound, newfree, inlinedCells,
                ste.Type == BlockType.ClassBlock || ste.CanSeeClassScope);

            free.UnionWith(newfree);
        }

        private HashSet<string> AnalyzeChildBlock(PySTEntry entry, HashSet<string> bound, HashSet<string> free,
            HashSet<string> global, HashSet<string> typeParams, PySTEntry? classEntry)
        {
            var tempFree = new HashSet<string>(free);
            AnalyzeBlock(entry, new HashSet<string>(bound), tempFree, new HashSet<string>(global),
                new HashSet<string>(typeParams), classEntry);
            return tempFree;
        }

        private void Analyze() =>
            AnalyzeBlock(Top, null, new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), null);

        private void ExitBlock()
        {
            _cur = null!;
            int size = _stack.Count;
            if (size != 0)
            {
                _stack.RemoveAt(size - 1);
                if (--size != 0)
                    _cur = _stack[size - 1];
            }
        }

        private void EnterBlock(string name, BlockType block, object ast, int lineno, int col, int endLineno, int endCol)
        {
            var ste = NewEntry(name, block, ast, lineno, col, endLineno, endCol);
            _stack.Add(ste);
            var prev = _cur;
            if (prev != null)
                ste.CompIterExpr = prev.CompIterExpr;
            if (prev?.MangledNames != null && block != BlockType.ClassBlock)
                ste.MangledNames = prev.MangledNames;
            _cur = ste;

            if (block == BlockType.AnnotationBlock)
                return;
            if (block == BlockType.ModuleBlock)
                _global = _cur.Symbols;
            prev?.Children.Add(ste);
        }

        private void EnterBlock(string name, BlockType block, object ast, GeneratedAstNode at) =>
            EnterBlock(name, block, ast, at.LineNo, at.ColOffset, at.EndLineNo, at.EndColOffset);

        private long LookupEntry(PySTEntry ste, string name) => ste.GetSymbol(MaybeMangle(_private, ste, name));

        private long LookupName(string name) => LookupEntry(_cur, name);

        private void AddDefHelper(string name, int flag, PySTEntry ste, int lineno, int col, int endLineno, int endCol)
        {
            string mangled = MaybeMangle(_private, _cur, name);
            var dict = ste.Symbols;
            long val;
            if (dict.TryGetValue(mangled, out long existing))
            {
                val = existing;
                if ((flag & DEF_PARAM) != 0 && (val & DEF_PARAM) != 0)
                    throw Error($"duplicate argument '{name}' in function definition", lineno, col + 1, endLineno, endCol + 1);
                if ((flag & DEF_TYPE_PARAM) != 0 && (val & DEF_TYPE_PARAM) != 0)
                    throw Error($"duplicate type parameter '{name}'", lineno, col + 1, endLineno, endCol + 1);
                val |= (long)flag;
            }
            else
            {
                val = flag;
            }
            if (ste.CompIterTarget)
            {
                if ((val & (DEF_GLOBAL | DEF_NONLOCAL)) != 0)
                    throw Error($"comprehension inner loop cannot rebind assignment expression target '{name}'",
                        lineno, col + 1, endLineno, endCol + 1);
                val |= DEF_COMP_ITER;
            }
            dict[mangled] = val;

            if ((flag & DEF_PARAM) != 0)
            {
                ste.Varnames.Add(mangled);
            }
            else if ((flag & DEF_GLOBAL) != 0)
            {
                val = flag;
                if (_global.TryGetValue(mangled, out long global))
                    val |= global;
                _global[mangled] = val;
            }
        }

        private void AddDef(string name, int flag, int lineno, int col, int endLineno, int endCol)
        {
            if ((flag & DEF_TYPE_PARAM) != 0 && _cur.MangledNames != null)
                _cur.MangledNames.Add(name);
            AddDefHelper(name, flag, _cur, lineno, col, endLineno, endCol);
        }

        private void AddDef(string name, int flag, GeneratedAstNode at) =>
            AddDef(name, flag, at.LineNo, at.ColOffset, at.EndLineNo, at.EndColOffset);

        private void EnterTypeParamBlock(string name, object ast, bool hasDefaults, bool hasKwdefaults, bool isClass,
            GeneratedAstNode at)
        {
            var currentType = _cur.Type;
            EnterBlock(name, BlockType.TypeParamBlock, ast, at);
            if (currentType == BlockType.ClassBlock)
            {
                _cur.CanSeeClassScope = true;
                AddDef("__classdict__", USE, at);
            }
            if (isClass)
            {
                AddDef(".type_params", DEF_LOCAL, at);
                AddDef(".type_params", USE, at);
                AddDef(".generic_base", DEF_LOCAL, at);
                AddDef(".generic_base", USE, at);
            }
            if (hasDefaults)
                AddDef(".defaults", DEF_PARAM, at);
            if (hasKwdefaults)
                AddDef(".kwdefaults", DEF_PARAM, at);
        }

        private void RecordDirective(string name, int lineno, int col, int endLineno, int endCol)
        {
            _cur.Directives ??= new();
            _cur.Directives.Add((MaybeMangle(_private, _cur, name), lineno, col, endLineno, endCol));
        }

        private void RecordDirective(string name, GeneratedAstNode at) =>
            RecordDirective(name, at.LineNo, at.ColOffset, at.EndLineNo, at.EndColOffset);

        private static bool HasKwonlydefaults(GeneratedArguments args)
        {
            for (int i = 0; i < Len(args.Kwonlyargs); i++)
            {
                if (args.KwDefaults[i] != null)
                    return true;
            }
            return false;
        }

        private static int Len(GeneratedSeq? seq) => seq?.Count ?? 0;

        private void VisitStmts(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                VisitStmt((GeneratedStmt)seq![i]);
        }

        private void VisitExprs(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                VisitExpr((GeneratedExpr)seq![i]);
        }

        private void VisitExprsWithNull(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
            {
                if (seq![i] is GeneratedExpr expr)
                    VisitExpr(expr);
            }
        }

        private void VisitTypeParams(GeneratedSeq? seq)
        {
            for (int i = 0; i < Len(seq); i++)
                VisitTypeParam((GeneratedTypeParam)seq![i]);
        }

        private void VisitFunction(GeneratedStmt s, string name, GeneratedArguments args, GeneratedSeq body,
            GeneratedSeq decorators, GeneratedExpr? returns, GeneratedSeq typeParams, bool isAsync)
        {
            AddDef(name, DEF_LOCAL, s);
            VisitExprs(args.Defaults);
            VisitExprsWithNull(args.KwDefaults);
            VisitExprs(decorators);
            bool generic = Len(typeParams) > 0;
            if (generic)
            {
                EnterTypeParamBlock(name, typeParams, args.Defaults != null, HasKwonlydefaults(args), false, s);
                VisitTypeParams(typeParams);
            }
            VisitAnnotations(s, args, returns);
            EnterBlock(name, BlockType.FunctionBlock, s, s);
            if (isAsync)
                _cur.Coroutine = true;
            VisitArguments(args);
            VisitStmts(body);
            ExitBlock();
            if (generic)
                ExitBlock();
        }

        private void VisitGlobalOrNonlocal(GeneratedStmt s, GeneratedIdentifierSeq names, bool isGlobal)
        {
            string kind = isGlobal ? "global" : "nonlocal";
            int flag = isGlobal ? DEF_GLOBAL : DEF_NONLOCAL;
            for (int i = 0; i < names.Count; i++)
            {
                string name = ((GeneratedIdentifier)names[i]).Value;
                long cur = LookupName(name);
                if ((cur & (DEF_PARAM | DEF_LOCAL | USE | DEF_ANNOT)) != 0)
                {
                    string message;
                    if ((cur & DEF_PARAM) != 0)
                        message = $"name '{name}' is parameter and {kind}";
                    else if ((cur & USE) != 0)
                        message = $"name '{name}' is used prior to {kind} declaration";
                    else if ((cur & DEF_ANNOT) != 0)
                        message = $"annotated name '{name}' can't be {kind}";
                    else
                        message = $"name '{name}' is assigned to before {kind} declaration";
                    throw Error(message, s);
                }
                AddDef(name, flag, s);
                RecordDirective(name, s);
            }
        }

        private void VisitStmt(GeneratedStmt s)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (s)
            {
                case GeneratedFunctionDef f:
                    VisitFunction(s, f.Name.Value, f.Args, f.Body, f.DecoratorList, f.Returns, f.TypeParams, false);
                    break;
                case GeneratedAsyncFunctionDef f:
                    VisitFunction(s, f.Name.Value, f.Args, f.Body, f.DecoratorList, f.Returns, f.TypeParams, true);
                    break;
                case GeneratedClassDef c:
                {
                    string name = c.Name.Value;
                    AddDef(name, DEF_LOCAL, s);
                    VisitExprs(c.DecoratorList);
                    var saved = _private;
                    bool generic = Len(c.TypeParams) > 0;
                    if (generic)
                    {
                        EnterTypeParamBlock(name, c.TypeParams, false, false, true, s);
                        _private = name;
                        _cur.MangledNames = new HashSet<string>();
                        VisitTypeParams(c.TypeParams);
                    }
                    VisitExprs(c.Bases);
                    for (int i = 0; i < Len(c.Keywords); i++)
                        VisitExpr(((GeneratedKeyword)c.Keywords[i]).Value);
                    EnterBlock(name, BlockType.ClassBlock, s, s);
                    _private = name;
                    if (generic)
                    {
                        AddDef("__type_params__", DEF_LOCAL, s);
                        AddDef(".type_params", USE, s);
                    }
                    VisitStmts(c.Body);
                    ExitBlock();
                    if (generic)
                        ExitBlock();
                    _private = saved;
                    break;
                }
                case GeneratedTypeAlias t:
                {
                    VisitExpr(t.Name);
                    string name = ((GeneratedName)t.Name).Id.Value;
                    bool inClass = _cur.Type == BlockType.ClassBlock;
                    bool generic = Len(t.TypeParams) > 0;
                    if (generic)
                    {
                        EnterTypeParamBlock(name, t.TypeParams, false, false, false, s);
                        VisitTypeParams(t.TypeParams);
                    }
                    EnterBlock(name, BlockType.TypeAliasBlock, s, s);
                    _cur.CanSeeClassScope = inClass;
                    if (inClass)
                        AddDef("__classdict__", USE, t.Value);
                    VisitExpr(t.Value);
                    ExitBlock();
                    if (generic)
                        ExitBlock();
                    break;
                }
                case GeneratedReturn r:
                    if (r.Value != null)
                    {
                        VisitExpr(r.Value);
                        _cur.ReturnsValue = true;
                    }
                    break;
                case GeneratedDelete d:
                    VisitExprs(d.Targets);
                    break;
                case GeneratedAssign a:
                    VisitExprs(a.Targets);
                    VisitExpr(a.Value);
                    break;
                case GeneratedAnnAssign a:
                    if (a.Target is GeneratedName target)
                    {
                        string name = target.Id.Value;
                        long cur = LookupName(name);
                        if ((cur & (DEF_GLOBAL | DEF_NONLOCAL)) != 0 && !ReferenceEquals(_cur.Symbols, _global) && a.Simple != 0)
                            throw Error((cur & DEF_GLOBAL) != 0
                                ? $"annotated name '{name}' can't be global"
                                : $"annotated name '{name}' can't be nonlocal", s);
                        if (a.Simple != 0)
                            AddDef(name, DEF_ANNOT | DEF_LOCAL, target);
                        else if (a.Value != null)
                            AddDef(name, DEF_LOCAL, target);
                    }
                    else
                    {
                        VisitExpr(a.Target);
                    }
                    VisitAnnotation(a.Annotation);
                    if (a.Value != null)
                        VisitExpr(a.Value);
                    break;
                case GeneratedAugAssign a:
                    VisitExpr(a.Target);
                    VisitExpr(a.Value);
                    break;
                case GeneratedFor f:
                    VisitExpr(f.Target);
                    VisitExpr(f.Iter);
                    VisitStmts(f.Body);
                    VisitStmts(f.Orelse);
                    break;
                case GeneratedAsyncFor f:
                    VisitExpr(f.Target);
                    VisitExpr(f.Iter);
                    VisitStmts(f.Body);
                    VisitStmts(f.Orelse);
                    break;
                case GeneratedWhile w:
                    VisitExpr(w.Test);
                    VisitStmts(w.Body);
                    VisitStmts(w.Orelse);
                    break;
                case GeneratedIf i:
                    VisitExpr(i.Test);
                    VisitStmts(i.Body);
                    VisitStmts(i.Orelse);
                    break;
                case GeneratedMatch m:
                    VisitExpr(m.Subject);
                    for (int i = 0; i < Len(m.Cases); i++)
                    {
                        var matchCase = (GeneratedMatchCase)m.Cases[i];
                        VisitPattern(matchCase.Pattern);
                        if (matchCase.Guard != null)
                            VisitExpr(matchCase.Guard);
                        VisitStmts(matchCase.Body);
                    }
                    break;
                case GeneratedRaise r:
                    if (r.Exc != null)
                    {
                        VisitExpr(r.Exc);
                        if (r.Cause != null)
                            VisitExpr(r.Cause);
                    }
                    break;
                case GeneratedTry t:
                    VisitStmts(t.Body);
                    VisitStmts(t.Orelse);
                    VisitHandlers(t.Handlers);
                    VisitStmts(t.Finalbody);
                    break;
                case GeneratedTryStar t:
                    VisitStmts(t.Body);
                    VisitStmts(t.Orelse);
                    VisitHandlers(t.Handlers);
                    VisitStmts(t.Finalbody);
                    break;
                case GeneratedAssert a:
                    VisitExpr(a.Test);
                    if (a.Msg != null)
                        VisitExpr(a.Msg);
                    break;
                case GeneratedImport i:
                    VisitAliases(i.Names);
                    break;
                case GeneratedImportFrom i:
                    VisitAliases(i.Names);
                    break;
                case GeneratedGlobal g:
                    VisitGlobalOrNonlocal(s, g.Names, true);
                    break;
                case GeneratedNonlocal n:
                    VisitGlobalOrNonlocal(s, n.Names, false);
                    break;
                case GeneratedExprStmt e:
                    VisitExpr(e.Value);
                    break;
                case GeneratedWith w:
                    VisitWithitems(w.Items);
                    VisitStmts(w.Body);
                    break;
                case GeneratedAsyncWith w:
                    VisitWithitems(w.Items);
                    VisitStmts(w.Body);
                    break;
            }
        }

        private void VisitHandlers(GeneratedSeq? handlers)
        {
            for (int i = 0; i < Len(handlers); i++)
            {
                var handler = (GeneratedExceptHandler)handlers![i];
                if (handler.Type != null)
                    VisitExpr(handler.Type);
                if (handler.Name != null)
                    AddDef(handler.Name.Value, DEF_LOCAL, handler);
                VisitStmts(handler.Body);
            }
        }

        private void VisitWithitems(GeneratedSeq? items)
        {
            for (int i = 0; i < Len(items); i++)
            {
                var item = (GeneratedWithitem)items![i];
                VisitExpr(item.ContextExpr);
                if (item.OptionalVars != null)
                    VisitExpr(item.OptionalVars);
            }
        }

        private void VisitAliases(GeneratedSeq? aliases)
        {
            for (int i = 0; i < Len(aliases); i++)
                VisitAlias((GeneratedAlias)aliases![i]);
        }

        private void ExtendNamedexprScope(GeneratedName e)
        {
            string target = e.Id.Value;
            for (int i = _stack.Count - 1; i >= 0; i--)
            {
                var ste = _stack[i];
                if (ste.Comprehension != ComprehensionKind.NoComprehension)
                {
                    long inScope = LookupEntry(ste, target);
                    if ((inScope & DEF_COMP_ITER) != 0 && (inScope & DEF_LOCAL) != 0)
                        throw Error($"assignment expression cannot rebind comprehension iteration variable '{target}'", e);
                    continue;
                }
                if (ste.Type == BlockType.FunctionBlock)
                {
                    long inScope = LookupEntry(ste, target);
                    AddDef(target, (inScope & DEF_GLOBAL) != 0 ? DEF_GLOBAL : DEF_NONLOCAL, e);
                    RecordDirective(target, e);
                    AddDefHelper(target, DEF_LOCAL, ste, e.LineNo, e.ColOffset, e.EndLineNo, e.EndColOffset);
                    return;
                }
                if (ste.Type == BlockType.ModuleBlock)
                {
                    AddDef(target, DEF_GLOBAL, e);
                    RecordDirective(target, e);
                    AddDefHelper(target, DEF_GLOBAL, ste, e.LineNo, e.ColOffset, e.EndLineNo, e.EndColOffset);
                    return;
                }
                switch (ste.Type)
                {
                    case BlockType.ClassBlock:
                        throw Error(NAMED_EXPR_COMP_IN_CLASS, e);
                    case BlockType.TypeParamBlock:
                        throw Error(NAMED_EXPR_COMP_IN_TYPEPARAM, e);
                    case BlockType.TypeAliasBlock:
                        throw Error(NAMED_EXPR_COMP_IN_TYPEALIAS, e);
                    case BlockType.TypeVarBoundBlock:
                        throw Error(NAMED_EXPR_COMP_IN_TYPEVAR_BOUND, e);
                }
            }
            throw new InvalidOperationException("unreachable");
        }

        private void HandleNamedexpr(GeneratedNamedExpr e)
        {
            if (_cur.CompIterExpr > 0)
                throw Error("assignment expression cannot be used in a comprehension iterable expression", e);
            if (_cur.Comprehension != ComprehensionKind.NoComprehension)
                ExtendNamedexprScope((GeneratedName)e.Target);
            VisitExpr(e.Value);
            VisitExpr(e.Target);
        }

        private void VisitExpr(GeneratedExpr e)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (e)
            {
                case GeneratedNamedExpr n:
                    RaiseIfAnnotationBlock("named expression", e);
                    HandleNamedexpr(n);
                    break;
                case GeneratedBoolOp b:
                    VisitExprs(b.Values);
                    break;
                case GeneratedBinOp b:
                    VisitExpr(b.Left);
                    VisitExpr(b.Right);
                    break;
                case GeneratedUnaryOp u:
                    VisitExpr(u.Operand);
                    break;
                case GeneratedLambda l:
                    if (_cur.CanSeeClassScope)
                        throw Error("Cannot use lambda in annotation scope within class scope", e);
                    VisitExprs(l.Args.Defaults);
                    VisitExprsWithNull(l.Args.KwDefaults);
                    EnterBlock("lambda", BlockType.FunctionBlock, e, e);
                    VisitArguments(l.Args);
                    VisitExpr(l.Body);
                    ExitBlock();
                    break;
                case GeneratedIfExp i:
                    VisitExpr(i.Test);
                    VisitExpr(i.Body);
                    VisitExpr(i.Orelse);
                    break;
                case GeneratedDict d:
                    VisitExprsWithNull(d.Keys);
                    VisitExprs(d.Values);
                    break;
                case GeneratedSet s:
                    VisitExprs(s.Elts);
                    break;
                case GeneratedGeneratorExp g:
                    HandleComprehension(e, "genexpr", g.Generators, g.Elt, null);
                    break;
                case GeneratedListComp l:
                    HandleComprehension(e, "listcomp", l.Generators, l.Elt, null);
                    break;
                case GeneratedSetComp s:
                    HandleComprehension(e, "setcomp", s.Generators, s.Elt, null);
                    break;
                case GeneratedDictComp d:
                    HandleComprehension(e, "dictcomp", d.Generators, d.Key, d.Value);
                    break;
                case GeneratedYield y:
                    RaiseIfAnnotationBlock("yield expression", e);
                    if (y.Value != null)
                        VisitExpr(y.Value);
                    _cur.Generator = true;
                    if (_cur.Comprehension != ComprehensionKind.NoComprehension)
                        throw ComprehensionBlockError(e);
                    break;
                case GeneratedYieldFrom y:
                    RaiseIfAnnotationBlock("yield expression", e);
                    VisitExpr(y.Value);
                    _cur.Generator = true;
                    if (_cur.Comprehension != ComprehensionKind.NoComprehension)
                        throw ComprehensionBlockError(e);
                    break;
                case GeneratedAwait a:
                    RaiseIfAnnotationBlock("await expression", e);
                    VisitExpr(a.Value);
                    _cur.Coroutine = true;
                    break;
                case GeneratedCompare c:
                    VisitExpr(c.Left);
                    VisitExprs(c.Comparators);
                    break;
                case GeneratedCall c:
                    VisitExpr(c.Func);
                    VisitExprs(c.Args);
                    for (int i = 0; i < Len(c.Keywords); i++)
                    {
                        if (c.Keywords[i] is GeneratedKeyword keyword)
                            VisitExpr(keyword.Value);
                    }
                    break;
                case GeneratedFormattedValue f:
                    VisitExpr(f.Value);
                    if (f.FormatSpec != null)
                        VisitExpr(f.FormatSpec);
                    break;
                case GeneratedJoinedStr j:
                    VisitExprs(j.Values);
                    break;
                case GeneratedAttribute a:
                    VisitExpr(a.Value);
                    break;
                case GeneratedSubscript s:
                    VisitExpr(s.Value);
                    VisitExpr(s.Slice);
                    break;
                case GeneratedStarred s:
                    VisitExpr(s.Value);
                    break;
                case GeneratedSlice s:
                    if (s.Lower != null)
                        VisitExpr(s.Lower);
                    if (s.Upper != null)
                        VisitExpr(s.Upper);
                    if (s.Step != null)
                        VisitExpr(s.Step);
                    break;
                case GeneratedName n:
                {
                    bool load = n.Ctx is GeneratedLoad;
                    AddDef(n.Id.Value, load ? USE : DEF_LOCAL, e);
                    if (load && _cur.IsFunctionLike && n.Id.Value == "super")
                        AddDef("__class__", USE, e);
                    break;
                }
                case GeneratedList l:
                    VisitExprs(l.Elts);
                    break;
                case GeneratedTuple t:
                    VisitExprs(t.Elts);
                    break;
            }
        }

        private void CheckReservedTypeParamName(GeneratedTypeParam tp, string name)
        {
            if (name == "__classdict__")
                throw Error($"reserved name '{name}' cannot be used for type parameter", tp);
        }

        private void VisitTypeParam(GeneratedTypeParam tp)
        {
            switch (tp)
            {
                case GeneratedTypeVar v:
                    CheckReservedTypeParamName(tp, v.Name.Value);
                    AddDef(v.Name.Value, DEF_TYPE_PARAM | DEF_LOCAL, tp);
                    if (v.Bound != null)
                    {
                        bool inClass = _cur.CanSeeClassScope;
                        EnterBlock(v.Name.Value, BlockType.TypeVarBoundBlock, tp, tp);
                        _cur.CanSeeClassScope = inClass;
                        if (inClass)
                            AddDef("__classdict__", USE, v.Bound);
                        VisitExpr(v.Bound);
                        ExitBlock();
                    }
                    break;
                case GeneratedTypeVarTuple v:
                    CheckReservedTypeParamName(tp, v.Name.Value);
                    AddDef(v.Name.Value, DEF_TYPE_PARAM | DEF_LOCAL, tp);
                    break;
                case GeneratedParamSpec v:
                    CheckReservedTypeParamName(tp, v.Name.Value);
                    AddDef(v.Name.Value, DEF_TYPE_PARAM | DEF_LOCAL, tp);
                    break;
            }
        }

        private void VisitPatterns(GeneratedSeq? patterns)
        {
            for (int i = 0; i < Len(patterns); i++)
                VisitPattern((GeneratedPattern)patterns![i]);
        }

        private void VisitPattern(GeneratedPattern p)
        {
            switch (p)
            {
                case GeneratedMatchValue v:
                    VisitExpr(v.Value);
                    break;
                case GeneratedMatchSequence s:
                    VisitPatterns(s.Patterns);
                    break;
                case GeneratedMatchStar s:
                    if (s.Name != null)
                        AddDef(s.Name.Value, DEF_LOCAL, p);
                    break;
                case GeneratedMatchMapping m:
                    VisitExprs(m.Keys);
                    VisitPatterns(m.Patterns);
                    if (m.Rest != null)
                        AddDef(m.Rest.Value, DEF_LOCAL, p);
                    break;
                case GeneratedMatchClass c:
                    VisitExpr(c.Cls);
                    VisitPatterns(c.Patterns);
                    VisitPatterns(c.KwdPatterns);
                    break;
                case GeneratedMatchAs a:
                    if (a.Pattern != null)
                        VisitPattern(a.Pattern);
                    if (a.Name != null)
                        AddDef(a.Name.Value, DEF_LOCAL, p);
                    break;
                case GeneratedMatchOr o:
                    VisitPatterns(o.Patterns);
                    break;
            }
        }

        private void ImplicitArg(int pos) =>
            AddDef($".{pos}", DEF_PARAM, _cur.Lineno, _cur.ColOffset, _cur.EndLineno, _cur.EndColOffset);

        private void VisitParams(GeneratedSeq? args)
        {
            for (int i = 0; i < Len(args); i++)
            {
                var arg = (GeneratedArg)args![i];
                AddDef(arg.Arg.Value, DEF_PARAM, arg);
            }
        }

        private void VisitAnnotation(GeneratedExpr annotation)
        {
            if (_futureAnnotations)
                EnterBlock("_annotation", BlockType.AnnotationBlock, annotation, annotation);
            VisitExpr(annotation);
            if (_futureAnnotations)
                ExitBlock();
        }

        private void VisitArgannotations(GeneratedSeq? args)
        {
            for (int i = 0; i < Len(args); i++)
            {
                var arg = (GeneratedArg)args![i];
                if (arg.Annotation != null)
                    VisitExpr(arg.Annotation);
            }
        }

        private void VisitAnnotations(GeneratedStmt o, GeneratedArguments a, GeneratedExpr? returns)
        {
            if (_futureAnnotations)
                EnterBlock("_annotation", BlockType.AnnotationBlock, new object(), o);
            VisitArgannotations(a.Posonlyargs);
            VisitArgannotations(a.Args);
            if (a.Vararg?.Annotation != null)
                VisitExpr(a.Vararg.Annotation);
            if (a.Kwarg?.Annotation != null)
                VisitExpr(a.Kwarg.Annotation);
            VisitArgannotations(a.Kwonlyargs);
            if (_futureAnnotations)
                ExitBlock();
            if (returns != null)
                VisitAnnotation(returns);
        }

        private void VisitArguments(GeneratedArguments a)
        {
            VisitParams(a.Posonlyargs);
            VisitParams(a.Args);
            VisitParams(a.Kwonlyargs);
            if (a.Vararg != null)
            {
                AddDef(a.Vararg.Arg.Value, DEF_PARAM, a.Vararg);
                _cur.Varargs = true;
            }
            if (a.Kwarg != null)
            {
                AddDef(a.Kwarg.Arg.Value, DEF_PARAM, a.Kwarg);
                _cur.Varkeywords = true;
            }
        }

        private void VisitAlias(GeneratedAlias a)
        {
            string name = a.Asname?.Value ?? a.Name.Value;
            int dot = name.IndexOf('.');
            string storeName = dot >= 0 ? name.Substring(0, dot) : name;
            if (name != "*")
            {
                AddDef(storeName, DEF_IMPORT, a);
            }
            else if (_cur.Type != BlockType.ModuleBlock)
            {
                throw Error("import * only allowed at module level", a);
            }
        }

        private void VisitComprehension(GeneratedComprehension lc)
        {
            _cur.CompIterTarget = true;
            VisitExpr(lc.Target);
            _cur.CompIterTarget = false;
            _cur.CompIterExpr++;
            VisitExpr(lc.Iter);
            _cur.CompIterExpr--;
            VisitExprs(lc.Ifs);
            if (lc.IsAsync != 0)
                _cur.Coroutine = true;
        }

        private void HandleComprehension(GeneratedExpr e, string scopeName, GeneratedSeq generators, GeneratedExpr elt, GeneratedExpr? value)
        {
            if (_cur.CanSeeClassScope)
                throw Error("Cannot use comprehension in annotation scope within class scope", e);

            bool isGenerator = e is GeneratedGeneratorExp;
            var outermost = (GeneratedComprehension)generators[0];
            _cur.CompIterExpr++;
            VisitExpr(outermost.Iter);
            _cur.CompIterExpr--;
            EnterBlock(scopeName, BlockType.FunctionBlock, e, e);
            _cur.Comprehension = e switch
            {
                GeneratedListComp => ComprehensionKind.ListComprehension,
                GeneratedSetComp => ComprehensionKind.SetComprehension,
                GeneratedDictComp => ComprehensionKind.DictComprehension,
                _ => ComprehensionKind.GeneratorExpression,
            };
            if (outermost.IsAsync != 0)
                _cur.Coroutine = true;

            ImplicitArg(0);
            _cur.CompIterTarget = true;
            VisitExpr(outermost.Target);
            _cur.CompIterTarget = false;
            VisitExprs(outermost.Ifs);
            for (int i = 1; i < generators.Count; i++)
                VisitComprehension((GeneratedComprehension)generators[i]);
            if (value != null)
                VisitExpr(value);
            VisitExpr(elt);
            _cur.Generator = isGenerator;
            bool isAsync = _cur.Coroutine && !isGenerator;
            ExitBlock();
            if (isAsync)
                _cur.Coroutine = true;
        }

        private void RaiseIfAnnotationBlock(string name, GeneratedExpr e)
        {
            switch (_cur.Type)
            {
                case BlockType.AnnotationBlock:
                    throw Error($"{name} cannot be used within an annotation", e);
                case BlockType.TypeVarBoundBlock:
                    throw Error($"{name} cannot be used within a TypeVar bound", e);
                case BlockType.TypeAliasBlock:
                    throw Error($"{name} cannot be used within a type alias", e);
                case BlockType.TypeParamBlock:
                    throw Error($"{name} cannot be used within the definition of a generic", e);
            }
        }

        private Exception ComprehensionBlockError(GeneratedExpr e) => Error(_cur.Comprehension switch
        {
            ComprehensionKind.ListComprehension => "'yield' inside list comprehension",
            ComprehensionKind.SetComprehension => "'yield' inside set comprehension",
            ComprehensionKind.DictComprehension => "'yield' inside dict comprehension",
            _ => "'yield' inside generator expression",
        }, e);

        // _Py_MaybeMangle
        public static string MaybeMangle(string? privateName, PySTEntry ste, string name)
        {
            if (ste.MangledNames != null && !ste.MangledNames.Contains(name))
                return name;
            return Mangle(privateName, name);
        }

        // _Py_Mangle
        public static string Mangle(string? privateName, string ident)
        {
            if (privateName == null || ident.Length < 2 || ident[0] != '_' || ident[1] != '_')
                return ident;
            int nlen = ident.Length;
            if ((ident[nlen - 1] == '_' && ident[nlen - 2] == '_') || ident.IndexOf('.') >= 0)
                return ident;
            int ipriv = 0;
            while (ipriv < privateName.Length && privateName[ipriv] == '_')
                ipriv++;
            if (ipriv == privateName.Length)
                return ident;
            return "_" + privateName.Substring(ipriv) + ident;
        }
    }
}
