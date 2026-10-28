# p15: modules, imports, sys/os surface
def show(label, fn):
    try:
        print(label, repr(fn()))
    except BaseException as e:
        print(label, "EXC", type(e).__name__, str(e)[:90])

# --- sys surface ---
show("sys.version_info", lambda: sys.version_info[:2] if False else __import__("sys").version_info.major)
import sys as _s
show("sys.maxsize", lambda: _s.maxsize > 2**62)
show("sys.platform.type", lambda: type(_s.platform).__name__)
show("sys.path.list", lambda: type(_s.path).__name__)
show("sys.argv.list", lambda: type(_s.argv).__name__)
show("sys.modules.dict", lambda: type(_s.modules).__name__)
show("sys.getrecursionlimit", lambda: type(_s.getrecursionlimit()).__name__)
show("sys.intern", lambda: type(_s.intern("abc")).__name__)
show("sys.getsizeof.int", lambda: type(_s.getsizeof(1)).__name__)
show("sys.setrecursionlimit.roundtrip", lambda: (lambda n: (_s.setrecursionlimit(n), _s.getrecursionlimit() == n)[1])(2000))
show("sys.implementation.name", lambda: _s.implementation.name)
show("sys.byteorder", lambda: _s.byteorder)
show("sys.flags.type", lambda: type(_s.flags).__name__)
show("sys.stdout.write", lambda: _s.stdout.write("") is None)
show("sys.exc_info.none", lambda: _s.exc_info())
try:
    1 / 0
except ZeroDivisionError:
    show("sys.exc_info.in_except", lambda: (_s.exc_info()[0].__name__, type(_s.exc_info()[1]).__name__, type(_s.exc_info()[2]).__name__))
show("sys.builtin_module_names", lambda: type(_s.builtin_module_names).__name__)
show("sys.api_version.tuple", lambda: type(_s.api_version_info).__name__)

# --- os.path ---
import os
show("os.sep", lambda: repr(os.sep))
show("os.name", lambda: os.name)
show("os.path.basename", lambda: os.path.basename(r"C:\a\b\c.py"))
show("os.path.dirname", lambda: os.path.dirname(r"C:\a\b\c.py"))
show("os.path.splitext", lambda: os.path.splitext("a.tar.gz"))
show("os.path.splitext.dot", lambda: os.path.splitext(".bashrc"))
show("os.path.join.rel", lambda: os.path.join("a", "b"))
show("os.path.join.abs", lambda: os.path.join("a", "C:\\x", "b"))
show("os.path.splitdrive", lambda: os.path.splitdrive("C:\\a")[0] != "")
show("os.path.abspath.type", lambda: os.path.isabs(os.path.abspath(".")))
show("os.path.normpath", lambda: os.path.normpath("a\\b\\..\\c"))
show("os.path.exists.false", lambda: os.path.exists("Z:\\no_such_file_xyz_12345"))
show("os.path.join.empty", lambda: os.path.join("", ""))
show("os.listdir.type", lambda: type(os.listdir(".")).__name__)
show("os.getcwd.type", lambda: type(os.getcwd()).__name__)
show("os.environ.type", lambda: type(os.environ).__name__)
show("os.environ.get.miss", lambda: os.environ.get("NO_SUCH_VAR_XYZ_99"))
show("os.getpid.int", lambda: type(os.getpid()).__name__)

# --- import semantics ---
import json as _json
show("import.json.dumps", lambda: _json.dumps({"a": [1, 2]}, sort_keys=True))
show("import.json.loads", lambda: _json.loads('{"b": 1.5}'))
show("import.re.match", lambda: __import__("re").match(r"a+b", "aaab").group())
show("import.math.pi", lambda: round(__import__("math").pi, 5))
show("import.math.isnan", lambda: __import__("math").isnan(float("nan")))
show("import.reuse", lambda: __import__("json") is _json)
import os.path as _osp
show("import.submodule", lambda: _osp.basename("x/y.py"))
show("import.missing", lambda: __import__("no_such_module_xyz"))
from math import sqrt as _sqrt
show("from.import", lambda: _sqrt(16))
from collections import OrderedDict as _OD
show("from.pkg.class", lambda: list(_OD([("a", 1), ("b", 2)])))
try:
    from no_such_pkg import thing
except ModuleNotFoundError as e:
    print("modnotfound", type(e).__name__, "name" in dir(e), getattr(e, "name", None))
show("importlib.import_module", lambda: __import__("importlib").import_module("json") is _json)
show("dir.module", lambda: "__name__" in dir(_json))
show("module.file", lambda: type(getattr(_json, "__file__", None)).__name__)
show("module.name", lambda: _json.__name__)
show("module.package", lambda: _json.__package__)
show("reload", lambda: __import__("importlib").reload(_json) is _json)

# --- json detail ---
show("json.true", lambda: _json.dumps([True, None, False]))
show("json.escape", lambda: _json.dumps("a\"b\\c\nd"))
show("json.unicode", lambda: _json.dumps("é", ensure_ascii=True))
show("json.unicode.raw", lambda: _json.dumps("é", ensure_ascii=False))
show("json.float", lambda: _json.dumps(1.5) + _json.dumps(1e300))
show("json.indent", lambda: _json.dumps({"a": 1}, indent=2))
show("json.tuple", lambda: _json.dumps((1, 2)))
show("json.set.err", lambda: _json.dumps({1, 2}))
show("json.nan.err", lambda: _json.dumps(float("nan")))
show("json.loads.bad", lambda: _json.loads("{"))
show("json.loads.num", lambda: repr(_json.loads("1e2")))
show("json.loads.keyint", lambda: _json.loads('{"1": 2}'))
