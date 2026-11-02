# __reduce__, __setstate__ and __length_hint__ of the builtin iterators, codecs of Lib/encodings.
import pickle, copy, re


def plain(value):
    return re.sub(r" at 0x[0-9a-fA-F]+", "", repr(value))


print("Пітон".encode("cp1251"), b"\xcf\xb3".decode("cp1251"), "a€\n\\".encode("raw-unicode-escape"), b"a\\u20ac".decode("raw-unicode-escape"))
print("\ud800".encode("utf-8", "surrogatepass"), ascii(b"\xed\xa0\x80".decode("utf-8", "surrogatepass")))
print(b"\xe1".decode("iso8859-7"), "é".encode("cp437"))
for make in [lambda: iter([1, 2, 3]), lambda: iter((1, 2, 3)), lambda: iter("abc"), lambda: iter(b"abc"), lambda: iter(range(3)), lambda: reversed([1, 2, 3]),
             lambda: reversed(range(3)), lambda: iter({1: 2, 3: 4}), lambda: iter({1: 2}.values()), lambda: iter({1: 2}.items()), lambda: iter({1, 2}),
             lambda: enumerate([1, 2]), lambda: map(str, [1, 2]), lambda: filter(None, [1, 0, 2]), lambda: zip([1, 2], "ab"), lambda: reversed((1, 2, 3))]:
    for proto in range(pickle.HIGHEST_PROTOCOL + 1):
        it = make()
        first = next(it)
        rebuilt = pickle.loads(pickle.dumps(it, proto))
        print(proto, type(it).__name__, first, list(it), list(rebuilt))
    it = make()
    print(it.__length_hint__() if hasattr(it, '__length_hint__') else None, it.__reduce__()[0].__name__ if hasattr(it, '__reduce__') else None)
    list(it)
    print(plain(it.__reduce__()))
print(pickle.loads(pickle.dumps(range(1, 9, 2))), pickle.loads(pickle.dumps(slice(1, 2))), pickle.loads(pickle.dumps(len)), copy.copy(iter([1])).__next__())
class S:
    def __getitem__(self, i):
        if i > 2:
            raise IndexError
        return i
    def __len__(self):
        return 3
it = iter(S())
print(type(it).__name__, it.__length_hint__(), next(it), it.__length_hint__())
s = {1, 2}
it = iter(s)
next(it)
s.add(3)
try:
    next(it)
except RuntimeError as e:
    print(e)
