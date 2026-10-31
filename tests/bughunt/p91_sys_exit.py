# P0-53 acceptance. sys.exit carries its argument and the process ends with its code.
# The harness compares return code as well as stdout, so the last line pins the status.
# Shape anchors: SystemExit_init in Objects/exceptions.c (the argument is stored, not decoded)
# and _Py_HandleSystemExit in Python/pythonrun.c:708 (None -> 0, int -> that value,
# anything else -> written to stderr and 1).
import sys

print("mro:", [t.__name__ for t in SystemExit.__mro__])
print("keyboard mro:", [t.__name__ for t in KeyboardInterrupt.__mro__])

print("repr:", repr(SystemExit(3)), repr(SystemExit()), repr(SystemExit("m")))
print("str:", str(SystemExit(3)), "|", str(SystemExit()), "|", str(SystemExit("m")))
print("args:", SystemExit(3).args, SystemExit().args, SystemExit(None).args)

print("except Exception catches SystemExit:", end=" ")
try:
    sys.exit(7)
except Exception:
    print(True)
except BaseException:
    print(False)

for value in (3, 0, -1, "bye", 1.5, [1, 2], None, True):
    try:
        sys.exit(value)
        print("returned", end=" ")
    except SystemExit as exc:
        print("code:", ascii(exc.code), "args:", exc.args, "str:", ascii(str(exc)))

# sys.exit is positional-only (Modules/sysmodule.c _sys_exit).
for label, call in (("two args", lambda: sys.exit(1, 2)), ("keyword", lambda: sys.exit(code=3))):
    try:
        call()
        print(label, "no error")
    except TypeError as exc:
        print(label, "TypeError:", exc)

# exit() comes from Lib/_sitebuiltins.py and passes None through, so its args are (None,).
for label, call in (("exit()", lambda: exit()), ("exit(2)", lambda: exit(2))):
    try:
        call()
        print(label, "no error")
    except SystemExit as exc:
        print(label, "args:", exc.args, "code:", ascii(exc.code))

# An int subclass is an integer to PyLong_Check, so it becomes the status itself.
try:
    sys.exit(type("I", (int,), {})(5))
except SystemExit as exc:
    print("int subclass code:", ascii(exc.code))

# A tuple given to sys.exit IS the argument list (Python/errors.c:46), while raising an
# already constructed instance keeps the tuple as a single argument.
try:
    sys.exit((1, 2))
except SystemExit as exc:
    print("tuple args:", exc.args, "code:", ascii(exc.code), "str:", ascii(str(exc)))
try:
    raise SystemExit((3, 4))
except SystemExit as exc:
    print("raised tuple args:", exc.args, "code:", ascii(exc.code))

print("the status of this process comes from the next line")
sys.exit(4)
