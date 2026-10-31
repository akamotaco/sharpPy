# L2.4 acceptance. code and args are slots of the object, not names in its __dict__, so storing them
# goes through neither __setattr__ nor a property, and a class attribute in a subclass shadows the
# member the ordinary way. SystemExit_init with no arguments leaves code alone
# (Objects/exceptions.c:640). Expected status 5.
class Hook(SystemExit):
    def __setattr__(self, name, value):
        print("set", name, value)
        object.__setattr__(self, name, value)


h = Hook(3)
print(h.code, h.args)
h.code = 4
print(h.code)


class Prop(SystemExit):
    code = property(lambda self: 9)


print(Prop(3).code, Prop(3).args)
try:
    Prop(3).code = 1
except AttributeError as e:
    print(e)


class Plain(SystemExit):
    pass


p = Plain(3)
print(p.__dict__, vars(p))
p.__dict__["code"] = 9
print(p.code, p.__dict__)


class Err(Exception):
    pass


print(Err(1).__dict__)

e = SystemExit(1)
SystemExit.__init__(e)
print(e.args, e.code)
SystemExit.__init__(e, 7, 8)
print(e.args, e.code)
SystemExit.__init__(e, 2)
print(e.args, e.code)


class Attr(SystemExit):
    code = 5


print(Attr(3).code, Attr(3).args)
raise Attr(3)
