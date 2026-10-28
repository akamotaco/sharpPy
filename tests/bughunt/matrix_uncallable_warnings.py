# bughunt matrix: CPython check_caller SyntaxWarning battery
# (stderr warnings are compared by tools/run_bughunt.py via the STDERR section)
def go(tag, src):
    try:
        exec(compile(src, "<s>", "exec"))
        print(tag, "done")
    except Exception as e:
        print(tag, type(e).__name__, str(e))
go("c1", "(1)()")
go("c2", "(1)()")
go("c3", "x = 1\nx()")
go("c4", "[1]()")
go("c5", "(2,)()")
go("c6", "'s'()")
go("c7", "None()")
go("c8", "(lambda: 1)()")
go("c9", "{1:2}()")
go("c10", "{1}()")
go("c11", "(1)()\n(1)()")
go("c12", "f = (1)()")
go("c13", "(x for x in [])()")
