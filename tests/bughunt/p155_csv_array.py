import array
import copy
import csv
import io

rows = [["a", "b c", 'q"x', "l1\nl2", ""], [1, 2.5, None, True, "x,y"]]
for kw in ({}, {"quoting": csv.QUOTE_ALL}, {"quoting": csv.QUOTE_NONNUMERIC}, {"delimiter": ";"}, {"lineterminator": "\n"},
           {"quoting": csv.QUOTE_STRINGS}, {"quoting": csv.QUOTE_NOTNULL}, {"doublequote": False, "escapechar": "\\"}):
    out = io.StringIO()
    w = csv.writer(out, **kw)
    w.writerows(rows)
    print(kw, repr(out.getvalue()))
    if "quoting" not in kw or kw["quoting"] == csv.QUOTE_ALL:
        print(list(csv.reader(io.StringIO(out.getvalue(), newline=""), **kw)))
print(list(csv.reader(["a,b", '"c,d",e', '"f""g",h', "", " i, j"])), list(csv.reader([" i, j"], skipinitialspace=True)))
print(list(csv.reader(["1,2.5,'x'"], quoting=csv.QUOTE_NONNUMERIC, quotechar="'")))
print(list(csv.reader(['"multi', 'line",x'])))
r = csv.reader(["a,b", "c,d"])
print(next(r), r.line_num, r.dialect.delimiter)
out = io.StringIO()
d = csv.DictWriter(out, fieldnames=["x", "y"], restval="-", extrasaction="ignore")
d.writeheader()
d.writerow({"x": 1, "z": 3})
d.writerows([{"x": 2, "y": 3}])
print(repr(out.getvalue()))
for row in csv.DictReader(io.StringIO("x,y\n1,2\n3\n4,5,6\n"), restval="R", restkey="K"):
    print(row)


class Semi(csv.Dialect):
    delimiter = ";"
    quotechar = '"'
    doublequote = True
    skipinitialspace = False
    lineterminator = "\n"
    quoting = csv.QUOTE_MINIMAL


csv.register_dialect("semi", Semi)
print(csv.list_dialects(), csv.get_dialect("semi").delimiter, list(csv.reader(["a;b"], "semi")), list(csv.reader(["a|b"], delimiter="|")))
csv.unregister_dialect("semi")
for call in (lambda: csv.get_dialect("nope"), lambda: csv.writer(1), lambda: list(csv.reader(['a"b"c,"d'], strict=True)),
             lambda: csv.reader([], delimiter="ab"), lambda: csv.writer(io.StringIO(), quoting=csv.QUOTE_NONE).writerow(["a,b"]),
             lambda: csv.writer(io.StringIO()).writerow(1)):
    try:
        print(call())
    except (csv.Error, TypeError) as e:
        print(type(e).__name__, e)
print(csv.field_size_limit())

a = array.array("i", [1, 2, 3])
print(a, a.typecode, a.itemsize, len(a), a[0], a[-1], a[1:], list(a), 2 in a, a.tolist(), a.tobytes(), a == array.array("i", [1, 2, 3]))
a.append(4)
a.extend([5])
a.extend(array.array("i", [6]))
a.insert(0, 0)
print(a, a.pop(), a.pop(0), a.count(2), a.index(3), a)
a.remove(2)
a.reverse()
a[0] = 9
a[1:2] = array.array("i", [7, 7])
del a[0]
print(a, a + array.array("i", [1]), a * 2, a.buffer_info()[1], bool(array.array("i")), sum(a))
a += array.array("i", [8])
a *= 2
print(a, [x for x in a], sorted(a), max(a), a < array.array("i", [100]))
for t, v in (("b", -128), ("B", 255), ("h", -32768), ("H", 65535), ("i", 2**31 - 1), ("I", 2**32 - 1), ("q", -(2**63)), ("Q", 2**64 - 1), ("f", 1.5), ("d", 0.1)):
    x = array.array(t, [v])
    print(t, x, x.itemsize, x.tobytes(), array.array(t, x.tobytes()))
print(array.array("u", "abc"), array.array("u", "abc").tounicode(), array.array("d"), array.array("B", b"ab"), array.array("B", bytearray(b"c")), array.array("i", (1, 2)), array.array("i", range(2)))
b = array.array("H", [1, 2])
b.byteswap()
print(b, array.array("i", [1]).__copy__(), copy.copy(array.array("i", [1])), copy.deepcopy(array.array("d", [1])), array.typecodes)
b.frombytes(b"\x01\x00")
b.fromlist([3])
print(b)
for call in (lambda: array.array("z"), lambda: array.array("b", [200]), lambda: array.array("B", [-1]), lambda: array.array("i", ["x"]),
             lambda: array.array("d", ["x"]), lambda: array.array("i", [1])[5], lambda: array.array("i", []).pop(), lambda: array.array("i", [1]).remove(2),
             lambda: array.array("i", [1]).index(2), lambda: array.array("i", [1]).extend(array.array("d")), lambda: array.array("i", [1]) + [1],
             lambda: array.array("H").frombytes(b"a")):
    try:
        print(call())
    except (ValueError, TypeError, IndexError, OverflowError) as e:
        print(type(e).__name__, e)
