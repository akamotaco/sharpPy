import ast
t = ast.parse("x = [1, 'a'] + f'{y!r:>3}'\ndef f(a, *b, c=1): return a")
print(ast.dump(t, include_attributes=False)[:500])
print(ast.literal_eval("[1, (2, 'x'), {'a': None}]"))
code = compile(t, "<t>", "exec")
ns = {"y": 5}
try:
    exec(code, ns)
except TypeError as e:
    print(e)
print("f" in ns)
print(eval(compile(ast.parse("1+2", mode="eval"), "<e>", "eval")))
for n in ast.walk(ast.parse("a.b(c)")):
    print(type(n).__name__, end=" ")
print()
print(ast.unparse(ast.parse("if x:\n  y = 1\nelse:\n  z")))
print(ast.get_docstring(ast.parse('"""doc"""')))
n = ast.Name("q", ast.Load(), lineno=1, col_offset=0)
print(n.id, n._fields, n.lineno)
