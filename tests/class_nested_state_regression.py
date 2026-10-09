"""Nested class compilation must restore the outer class compilation context."""
class Meta(type):
    @classmethod
    def __prepare__(mcls, name, bases):
        return {'value': 9}

def captured(value):
    class Outer(metaclass=Meta):
        class Inner:
            marker = True
        observed = value
    return Outer.observed
assert captured(7) == 9
print('class nested state regression: PASS')
