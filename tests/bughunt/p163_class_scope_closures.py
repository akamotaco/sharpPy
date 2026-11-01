def shadowed():
    value = "outer"

    class Test:
        def value(self):
            return "method"

        def read(self):
            return value

    return Test().value(), Test().read(), Test.value.__name__


print(shadowed())


def stored():
    x = 1

    class C:
        x = 2
        y = x

        def get(self):
            return x

        def own(self):
            return self.x

    return C.x, C.y, C().get(), C().own()


print(stored())


def read_outer():
    x = 1

    class C:
        y = x
        x = 3
        z = x

        def get(self):
            return x

    return C.y, C.x, C.z, C().get()


try:
    print(read_outer())
except NameError as e:
    print(e)


def nested():
    v = "f"

    class A:
        v = "a"

        class B:
            def get(self):
                return v

        def m(self):
            class D:
                w = v
            return D.w

    return A.B().get(), A().m(), A.v


print(nested())
GLOBAL = None


def walrus_global():
    global GLOBAL
    [GLOBAL := i for i in range(3)]
    return GLOBAL


print(walrus_global(), GLOBAL)


def walrus_nonlocal():
    total = 0

    def inner():
        nonlocal total
        [total := total + i for i in range(4)]
        return total
    return inner(), total


print(walrus_nonlocal())
