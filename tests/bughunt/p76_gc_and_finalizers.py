# P0-37 probe: the gc module's surface and refusals, and where __del__ runs.
# gc is a built-in C module in CPython (Modules/gcmodule.c), so there is no Lib/gc.py to copy;
# every line here is measured from CPython 3.12.15. Values that depend on how many objects happen
# to exist in a process (get_count, get_freeze_count, the contents of get_objects) are checked for
# type only, and SharpPy's answer for their contents is registered in docs/known_gaps.md.
import gc

print('names:', sorted(n for n in dir(gc) if not n.startswith('__')))
counts = gc.get_count()
print('get_count:', type(counts).__name__, len(counts),
      all(type(c) is int for c in counts))
print('threshold:', gc.get_threshold(), 'enabled:', gc.isenabled())
print('collect():', gc.collect(), 'collect(1):', gc.collect(1))

for label, call in [
    ("collect('x')", lambda: gc.collect('x')),
    ('collect(3)', lambda: gc.collect(3)),
    ('collect(-1)', lambda: gc.collect(-1)),
    ('collect(None)', lambda: gc.collect(None)),
    ("get_objects('x')", lambda: gc.get_objects('x')),
    ('get_objects(5)', lambda: gc.get_objects(5)),
    ('set_threshold(1,2,3,4)', lambda: gc.set_threshold(1, 2, 3, 4)),
    ("set_threshold('x')", lambda: gc.set_threshold('x')),
    ("set_debug('x')", lambda: gc.set_debug('x')),
    ('enable(1)', lambda: gc.enable(1)),
    ('is_tracked()', lambda: gc.is_tracked()),
    ('get_stats(1)', lambda: gc.get_stats(1)),
    ('collect(1, 2)', lambda: gc.collect(1, 2)),
    ('get_objects(1, 2)', lambda: gc.get_objects(1, 2)),
    ('is_finalized(1, 2)', lambda: gc.is_finalized(1, 2)),
    ('set_debug()', lambda: gc.set_debug()),
    ('freeze(1)', lambda: gc.freeze(1)),
    ('get_count(1)', lambda: gc.get_count(1)),
    ('set_threshold()', lambda: gc.set_threshold()),
]:
    try:
        call()
        print(label, '-> no error')
    except Exception as exc:
        print(label, '->', type(exc).__name__ + ': ' + str(exc))

print('stats:', len(gc.get_stats()), sorted(gc.get_stats()[0]),
      all(type(v) is int for v in gc.get_stats()[0].values()))
print('flags:', gc.DEBUG_STATS, gc.DEBUG_COLLECTABLE, gc.DEBUG_UNCOLLECTABLE, gc.DEBUG_SAVEALL,
      gc.DEBUG_LEAK, gc.get_debug())
print('freeze:', gc.freeze(), gc.unfreeze(), type(gc.get_freeze_count()).__name__, gc.garbage)
print('callbacks type:', type(gc.callbacks).__name__, gc.callbacks)

# The tracking rule: a type is tracked when it can hold a reference that closes a cycle, and a
# tuple or dict leaves the tracked set while everything inside it is untracked.
class Plain:
    pass


def a_function():
    pass


tracked = [
    ([], True), (set(), True), (frozenset(), True), ({}, False), ({'a': 1}, False),
    ({'a': []}, True), ((1,), True), ((), False), (([],), True), (bytearray(), False),
    ('a', False), (b'a', False), (1.5, False), (None, False), (int, False),
    (Plain, True), (Plain(), True), (a_function, True), (len, True), (gc, True),
]
for obj, _ in tracked:
    print('is_tracked', repr(obj)[:22].ljust(24), gc.is_tracked(obj))

# Referents follow CPython's traverse order: dict yields values, tuple yields items in reverse,
# an instance yields its class, and an atomic object yields nothing.
holder = Plain()
holder.value = 7
print('referents int:', gc.get_referents(1))
print('referents dict:', gc.get_referents({'a': 1}))
print('referents tuple:', gc.get_referents((1, 2)))
print('referents instance:', [type(o).__name__ for o in gc.get_referents(holder)],
      7 in gc.get_referents(holder))
print('referents list:', gc.get_referents([3, 4]))
print('referrers:', type(gc.get_referrers(1)).__name__)

phases = []
gc.callbacks.append(lambda phase, info: phases.append((phase, type(info).__name__, sorted(info))))
gc.collect(0)
gc.callbacks.clear()
print('callback calls:', phases)

# __del__ is a special method: a subclass override wins, super() reaches the base, and a subclass
# that does not declare one is still finalized. CPython runs these when it drops module globals at
# shutdown, in creation order, after the program's own output.
class Base:
    def __del__(self):
        print('del base')


class Child(Base):
    def __del__(self):
        print('del child start')
        super().__del__()
        print('del child end')


class Inherits(Base):
    pass


first = Base()
second = Child()
third = Inherits()


# The "only once" decision is reference identity in CPython - a flag in the GC header
# (Objects/object.c:362 _PyGC_FINALIZED), not a hash lookup. SharpPy's PyObject.GetHashCode returns
# the Python hash, so a set without a reference comparer would consult user-defined __hash__ while
# registering an instance. A class whose __eq__ accepts anything and whose __hash__ is constant must
# still get one __del__ per instance, and a __hash__ that raises must not disturb construction.
class Degenerate:
    def __del__(self):
        print('del degenerate')

    def __eq__(self, other):
        return True

    def __hash__(self):
        return 0


class Exploding:
    def __del__(self):
        print('del exploding')

    def __hash__(self):
        raise RuntimeError('user hash ran')


class Unhashable:
    __hash__ = None

    def __del__(self):
        print('del unhashable')


same1 = Degenerate()
same2 = Degenerate()
boom = Exploding()
unhashable = Unhashable()
print('degenerate equal', same1 == same2, hash(same1) == hash(same2))
print('constructed all')


print('main start')
print('main end')
