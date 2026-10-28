# p20: isinstance/issubclass __instancecheck__/__subclasscheck__ protocol (ABCs)
import abc
import io
import os


def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


# --- ABCMeta custom hooks ---
class Meta(type):
    def __instancecheck__(cls, instance):
        return "ic"

    def __subclasscheck__(cls, sub):
        return "sc"


class C(metaclass=Meta):
    pass


show("custom ic", lambda: isinstance(5, C))
show("custom sc", lambda: issubclass(int, C))

# --- ABC virtual registration of native + user types ---
class A(metaclass=abc.ABCMeta):
    pass


class User:
    pass


A.register(int)
A.register(User)
show("reg native isinstance", lambda: isinstance(5, A))
show("reg native issubclass", lambda: issubclass(int, A))
show("reg user isinstance", lambda: isinstance(User(), A))
show("unreg isinstance", lambda: isinstance("s", A))
show("unreg issubclass", lambda: issubclass(str, A))

# --- builtin subclass relationships (not ABC-based) ---
show("int issubclass(object)", lambda: issubclass(int, object))
show("bool issubclass int", lambda: issubclass(bool, int))
show("int not subclass str", lambda: issubclass(int, str))

# --- custom ABC subclass + hook ---
class MyIterable(metaclass=abc.ABCMeta):
    @abc.abstractmethod
    def foo(self):
        pass


class Concrete(MyIterable):
    def foo(self):
        return 1


show("abstract instantiate", lambda: MyIterable())
show("concrete isinstance", lambda: isinstance(Concrete(), MyIterable))

# --- __subclasshook__ (structural) ---
class HasLen(metaclass=abc.ABCMeta):
    @classmethod
    def __subclasshook__(cls, sub):
        return hasattr(sub, "__len__")


show("hook list", lambda: issubclass(list, HasLen))
show("hook int", lambda: issubclass(int, HasLen))
show("hook isinstance list", lambda: isinstance([1], HasLen))

# --- io.IOBase virtual (deferred from p19) ---
sc = (os.environ.get("BUGHUNT_SCRATCH") or ".").replace("\\", "/")
p = sc + "/p20io.txt"
f = open(p, "w")
f.close()
tf = open(p, "r")
show("file isinstance IOBase", lambda: isinstance(tf, io.IOBase))
show("file isinstance TextIOBase", lambda: isinstance(tf, io.TextIOBase))
show("file not BufferedReader", lambda: isinstance(tf, io.BufferedReader))
tf.close()
bf = open(p, "rb")
show("rb isinstance BufferedReader", lambda: isinstance(bf, io.BufferedReader))
show("rb isinstance IOBase", lambda: isinstance(bf, io.IOBase))
bf.close()
os.remove(p)

# --- error paths ---
show("isinstance non-type", lambda: isinstance(1, 42))
show("issubclass non-type", lambda: issubclass(1, 42))
show("issubclass arg1 non-type", lambda: issubclass(1, int))
