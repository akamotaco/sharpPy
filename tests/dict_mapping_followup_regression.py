for kind in ('keys', 'values', 'items'):
    d = dict.fromkeys(range(5), 10)
    it = iter(getattr(d, kind)())
    next(it)
    del d[0]
    d[5] = 20
    print(kind, list(it))

a = {'a': 1, 'b': 2}
b = {'b': 2, 'c': 3}
for kind in ('keys', 'items'):
    left, right = getattr(a, kind)(), getattr(b, kind)()
    print(kind, left == right, left != right, left <= right, left < right,
          left >= right, left > right)
    print(sorted(left & right), sorted(left | right), sorted(left - right), sorted(left ^ right))
print({'a': []}.items() == {'a': []}.items())
print(sorted(a.keys() & ['a', 'z']))

def namespace_check():
    global mapping_order_first, mapping_order_second
    ns = globals()
    assert globals() is ns
    mapping_order_first = 1
    mapping_order_second = 2
    view = ns.items()
    del mapping_order_first
    mapping_order_first = 3
    print([k for k in ns.copy() if k.startswith('mapping_order_')])
    print(('mapping_order_first', 3) in view)
    assert ns.popitem() == ('mapping_order_first', 3)
    del mapping_order_second
namespace_check()
