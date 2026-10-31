# L2.3 acceptance. Lib/weakref.py WeakMethod rebuilds a method with self._meth_type(func, obj), so the
# method type has to be callable. It is also the only way to reach the __func__ == branch of
# method_richcompare with two different functions.
class H:
    def f(self):
        return "f", self is h


h = H()
Method = type(h.f)
print(Method.__name__)
m = Method(H.f, h)
print(m(), m.__self__ is h, m.__func__ is H.f, m == h.f, hash(m) == hash(h.f))

for args in [(), (H.f,), (H.f, h, 1), (1, h), (H.f, None)]:
    try:
        Method(*args)
        print("built", len(args))
    except TypeError as e:
        print("TypeError:", e)


class CallEq:
    def __call__(self, *a):
        return len(a)

    def __eq__(self, o):
        print("function __eq__")
        return True

    def __hash__(self):
        return 1


class Raises(CallEq):
    def __eq__(self, o):
        raise ValueError("eq")

    __hash__ = CallEq.__hash__


a = CallEq()
b = CallEq()
print(Method(a, h)(), Method(a, h) == Method(b, h), hash(Method(a, h)) == hash(Method(b, h)))
print(Method(a, h) == Method(b, H()))
try:
    Method(Raises(), h) == Method(b, h)
except ValueError as e:
    print("ValueError:", e)
print(dict.fromkeys == dict.fromkeys, hash(dict.fromkeys) == hash(dict.fromkeys))
