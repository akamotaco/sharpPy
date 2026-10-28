# p28: setattr/delattr on native (immutable) types + no-dict instances.
def err(label, fn):
    try:
        fn()
        print(label, "ok")
    except Exception as e:
        print(label, type(e).__name__ + ":", e)

def a(): int.foo = 1
def b(): del int.__len__
def c(): list.bar = 1
def d():
    x = 5
    x.foo = 1
def e():
    x = 5
    del x.foo
class C:
    z = 1
def f(): C.z = 2
def g():
    del C.z
def h(): C.new = 5
def i(): int.__module__ = "x"
err("int-attr-set", a)
err("int-dunder-del", b)
err("list-attr-set", c)
err("int-instance-set", d)
err("int-instance-del", e)
err("class-attr-set", f)
err("class-attr-del", g)
err("class-new-attr", h)
print("class-read-back", C.new)
err("int-module-set", i)
