"""Class free variables use the namespace first, then their closure cell."""
def observed(n):
    class Body:
        total = 0
        for i in range(n):
            total += locals()['i']
        assert 'n' not in locals()
    return Body.total
assert observed(5) == 10

n = 100
def local_name(n):
    class Body:
        before = n
        n = 3
    return Body.before, Body.n
assert local_name(7) == (100, 3)

class Meta(type):
    @classmethod
    def __prepare__(mcls, name, bases):
        return {'n': 8}

def prepared(n):
    class Body(metaclass=Meta):
        value = n
    return Body.value
assert prepared(7) == 8
print('class closure mapping regression: PASS')
