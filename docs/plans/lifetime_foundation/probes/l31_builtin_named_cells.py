def a(type, id, max=1):
    def b():
        return type, id, max
    class C:
        v = type
        def m(self):
            return id
    return b(), C.v, C().m(), [type for _ in (1,)], (lambda: max)()
print(a(1, 2))
def c():
    len = 5
    def d():
        return len
    return d(), [len for _ in (1,)]
print(c())
def e():
    def f():
        return len("ab")
    return f()
print(e())
class K:
    print = 3
    def p(self):
        return print
print(K().p() is print)
