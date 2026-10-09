# Bound methods expose custom attributes of their underlying function.
class Example:
    def method(self):
        pass


Example.method.__override__ = True
print(getattr(Example.method, "__override__", False))
print(getattr(Example().method, "__override__", False))
assert getattr(Example().method, "__override__", False) is True
