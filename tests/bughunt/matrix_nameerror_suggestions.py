import traceback
try: print(prtin("x"))
except NameError as e: print("c1:", e, "| name:", repr(e.name))
try: print(absl(1))
except NameError as e: print("c2:", e, "| name:", repr(e.name))
def f():
    result1 = 2
    return resullt1
try: f()
except NameError as e: print("c3:", e, "| name:", repr(e.name))
try: print(undefined_name)
except NameError as e: print("c4:", e, "| name:", repr(e.name))
try: print(prtin("y"))
except NameError as e:
    print("fmt:", "Did you mean" in "".join(traceback.format_exception(e)))
class K:
    def __init__(self): self.alpha = 1
    def m(self): return alpha2
try: K().m()
except NameError as e:
    print("self fmt:", [l for l in "".join(traceback.format_exception(e)).splitlines() if "Did you" in l])
try: copy
except NameError as e:
    print("stdlib fmt:", [l for l in "".join(traceback.format_exception(e)).splitlines() if l.startswith("NameError")])
import sys
print("stdlib set:", "json" in sys.stdlib_module_names, "numpy" in sys.stdlib_module_names)
print("NE ctor:", repr(NameError("x", name="q").name))
