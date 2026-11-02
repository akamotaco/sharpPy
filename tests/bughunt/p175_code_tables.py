# dis, tokenize, inspect and traceback from CPython on the tables of the code objects.
import dis
def f(a, b=1):
    for i in range(a):
        try:
            print(i + b)
        except ValueError as e:
            raise
    return [x for x in a]
dis.dis(f)
print(list(f.__code__.co_lines())[:3], list(f.__code__.co_positions())[:3])
import tokenize, io, inspect, dataclasses, types, traceback
src = 'def f(a):\n    x = f"{a!r:>3}é" + 1\n    return "é"  # c\n\nclass C: pass'
for t in tokenize.generate_tokens(io.StringIO(src).readline):
    print(t)
@dataclasses.dataclass
class D:
    x: int = 1
print(inspect.signature(D), inspect.signature(f), f.__code__.co_stacksize, f.__code__.co_linetable.hex(), f.__code__.co_exceptiontable.hex())
print(types.MappingProxyType({1: 2}), repr(types.MappingProxyType({1: 2})))
try:
    (1 + 2) / 0 + [][1]
except ZeroDivisionError as error:
    print(traceback.format_exc().split("\n", 1)[1])
