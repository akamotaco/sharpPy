# L2.4 acceptance. _Py_HandleSystemExit: when PyObject_GetAttr(exc, "code") fails, the exception itself
# goes to the else branch, so it is written and the status is 1 (Python/pythonrun.c:732-:744).
# Expected status 1, not 0.
import sys

sys.stderr = sys.stdout


class E(SystemExit):
    def __getattribute__(self, name):
        if name == "code":
            raise ValueError("no code")
        return super().__getattribute__(name)


raise E(3)
