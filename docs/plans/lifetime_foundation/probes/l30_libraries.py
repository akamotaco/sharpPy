import collections, functools, string, textwrap, reprlib, fnmatch, queue, threading, pprint, logging, sys
c = collections.Counter("aabbbc")
print(c, c.most_common(1), c + c, c - collections.Counter("a"), sorted(c.elements()), c["z"], c.total())
print(collections.UserDict(a=1), collections.UserList([1]), collections.UserString("x"), collections.ChainMap({"a": 1}, {"b": 2})["b"])
@functools.lru_cache(maxsize=2)
def f(x):
    return x * 2
f(1); f(1); f(2); f(3)
print(f.cache_info(), f(3)); f.cache_clear(); print(f.cache_info())
@functools.cache
def g(x):
    return x
g(1); print(g.cache_info(), g.__name__)
print(string.ascii_uppercase[:2], string.digits, string.Template("$a-${b}").substitute(a=1, b=2), string.capwords("a b"), string.Formatter().format("{0:>3}|{x}", 1, x=2))
print(textwrap.fill("hello world foo bar", 8), textwrap.dedent("  a\n  b"), textwrap.shorten("hello world foo", 10), textwrap.wrap("aa bb cc", 3), textwrap.indent("a", "  "))
print(reprlib.repr(list(range(20))), fnmatch.fnmatch("a.py", "*.py"), fnmatch.filter(["a.py", "b.c"], "*.c"))
q = queue.Queue(); q.put(1); q.put(2); print(q.get(), q.qsize(), q.empty())
pq = queue.PriorityQueue(); pq.put((2, "b")); pq.put((1, "a")); print(pq.get())
e = threading.Event(); e.set(); print(e.is_set(), threading.current_thread().name)
l = threading.Lock()
with l:
    print(l.locked())
r = threading.RLock()
with r:
    with r:
        print("r")
pprint.pprint({"a": [1, 2], "b": {"c": 3}}, width=10)
print(pprint.pformat([1, 2]))
logging.basicConfig(level=logging.INFO, format="%(levelname)s:%(name)s:%(message)s", stream=sys.stdout)
log = logging.getLogger("game")
log.info("hello %d", 1); log.debug("no"); log.warning("w")
