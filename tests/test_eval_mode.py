# tests/test_eval_mode.py — eval/compile mode 회귀 테스트
# CPython 3.12 동작 검증: eval_input은 단일 표현식만 허용

tests_passed = 0
tests_failed = 0

def test(name, got, expected):
    global tests_passed, tests_failed
    if got == expected:
        tests_passed += 1
    else:
        tests_failed += 1
        print(f"  FAIL: {name} — got {repr(got)}, expected {repr(expected)}")

print("=== 1. eval() return type preservation ===")
test("eval('False') type", type(eval("False")).__name__, "bool")
test("eval('True') type", type(eval("True")).__name__, "bool")
test("eval('0') type", type(eval("0")).__name__, "int")
test("eval('1') type", type(eval("1")).__name__, "int")
test("eval('\"hello\"') type", type(eval("'hello'")).__name__, "str")
test("eval('1.5') type", type(eval("1.5")).__name__, "float")
test("eval('None') type", type(eval("None")).__name__, "NoneType")

print("\n=== 2. eval() bool identity (singleton) ===")
test("eval('False') is False", eval("False") is False, True)
test("eval('True') is True", eval("True") is True, True)

print("\n=== 3. eval() with function returning bool ===")
_flag = False
def get_flag():
    return _flag
test("eval('get_flag()') type", type(eval("get_flag()")).__name__, "bool")
test("eval('get_flag()') value", eval("get_flag()"), False)
_flag = True
test("eval('get_flag()') after set True", eval("get_flag()"), True)
test("eval('get_flag()') type after set True", type(eval("get_flag()")).__name__, "bool")

print("\n=== 4. compile + eval ===")
code = compile("get_flag()", "<test>", "eval")
r = eval(code)
test("compile('get_flag()', eval) type", type(r).__name__, "bool")
test("compile('get_flag()', eval) value", r, True)

print("\n=== 5. eval rejects statements (SyntaxError) ===")

def expect_syntax_error(expr, label):
    try:
        eval(expr)
        test(label, "no error", "SyntaxError")
    except SyntaxError:
        test(label, "SyntaxError", "SyntaxError")
    except Exception as e:
        test(label, type(e).__name__, "SyntaxError")

expect_syntax_error("x = 1", "assignment")
expect_syntax_error("x = 1; x", "assignment + expr")
expect_syntax_error("import os", "import")
expect_syntax_error("import os; os.getcwd()", "import + call")
expect_syntax_error("if True: pass", "if statement")
expect_syntax_error("for x in []: pass", "for loop")
expect_syntax_error("while False: pass", "while loop")
expect_syntax_error("def f(): pass", "def")
expect_syntax_error("class C: pass", "class")

print("\n=== 6. compile() mode='eval' rejects statements ===")

def expect_compile_syntax_error(code_str, label):
    try:
        compile(code_str, "<test>", "eval")
        test(label, "no error", "SyntaxError")
    except SyntaxError:
        test(label, "SyntaxError", "SyntaxError")
    except Exception as e:
        test(label, type(e).__name__, "SyntaxError")

expect_compile_syntax_error("x = 1", "compile eval assignment")
expect_compile_syntax_error("import os; os.getcwd()", "compile eval import+call")
expect_compile_syntax_error("print('hi')\nprint('bye')", "compile eval multi-expr")

# compile exec mode allows everything
code2 = compile("import os; x = 1", "<test>", "exec")
test("compile exec allows statements", code2 is not None, True)

print("\n=== 7. eval() valid expressions ===")
test("eval('[1,2,3]') type", type(eval("[1,2,3]")).__name__, "list")
test("eval('(1,2)') type", type(eval("(1,2)")).__name__, "tuple")
test("eval('{\"a\":1}') type", type(eval('{"a":1}')).__name__, "dict")
test("eval('1+2')", eval("1+2"), 3)
test("eval('True and False')", eval("True and False"), False)
test("eval('not False')", eval("not False"), True)
test("eval lambda", eval("(lambda x: x*2)(3)"), 6)

print("\n=== 8. eval() comparison results ===")
test("eval('1==1') type", type(eval("1==1")).__name__, "bool")
test("eval('1!=1') type", type(eval("1!=1")).__name__, "bool")
test("eval('1<2') type", type(eval("1<2")).__name__, "bool")

print(f"\n{'='*50}")
print(f"TOTAL: {tests_passed}/{tests_passed + tests_failed} passed, {tests_failed} failed")
if tests_failed == 0:
    print("ALL PASSED")
else:
    print("SOME FAILED")
