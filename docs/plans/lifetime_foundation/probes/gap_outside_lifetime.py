# NOT acceptance. Defects found in review that predate the lifetime work (present at d95c129),
# recorded in docs/known_gaps.md P0-52 to P0-56. The last two end the process, so they run by argument:
#   gap_outside_lifetime.py            identity and surface differences
#   gap_outside_lifetime.py depth 100  recursion: exit 127 at depth 100, depth 90 is fine
#   gap_outside_lifetime.py exit 3     exit status: SharpPy reports 1
import sys

mode = sys.argv[1] if len(sys.argv) > 1 else "surface"

if mode == "depth":
    def down(n):
        return 0 if n == 0 else down(n - 1) + 1

    print(down(int(sys.argv[2])))
    raise SystemExit(0)

if mode == "exit":
    sys.exit(int(sys.argv[2]))


class C:
    def m(self):
        pass


class T1:
    def who(self):
        return "T1"


class T2:
    def who(self):
        return "T2"


o = C()
print("bound method eq:", o.m == o.m)
x = T1()
x.__class__ = T2
print("__class__ assignment:", type(x).__name__, x.who())
print("KeyboardInterrupt:", "KeyboardInterrupt" in dir(__builtins__))

a = 200
t = (1000, 2.5)
s = "abc"
print("small int cache:", a + 56 is 256, a - 205 is -5)
print("empty and single char str:", "" is "a"[1:], "a" is "abc"[0], "a" is chr(97))
print("empty bytes:", b"" is b"a"[1:])
print("identity conversions:", tuple(t) is t, str(s) is s, () is tuple())
print("function __name__:", C.m.__name__ is C.m.__name__)
