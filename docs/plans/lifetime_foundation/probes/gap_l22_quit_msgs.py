import sys

cases = {
    "exit(x=1)": lambda: exit(x=1),
    "exit(1, code=2)": lambda: exit(1, code=2),
    "exit(1, 2, 3)": lambda: exit(1, 2, 3),
    "quit(1, 2)": lambda: quit(1, 2),
    "method keywords": lambda: type((lambda self: 1).__get__(object()))(f=1),
}
for label, call in cases.items():
    try:
        call()
        print(label, "-> no error")
    except SystemExit as exc:
        print(label, "-> SystemExit", exc.args)
    except TypeError as exc:
        print(label, "-> TypeError:", exc)
