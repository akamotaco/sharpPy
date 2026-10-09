"""Special operators use type-level lookup, descriptor binding and live MRO changes."""


class OperatorDescriptor:
    def __get__(self, instance, owner):
        assert instance is not None
        return lambda other: 10 + other


class RaisingOperatorDescriptor:
    def __get__(self, instance, owner):
        raise ValueError("descriptor error")


def instance_attribute_is_ignored():
    class Number:
        def __iadd__(self, other):
            return 10 + other
    value = Number()
    value.__iadd__ = lambda other: 999
    value += 2
    assert value == 12


def getattribute_is_bypassed():
    class Number:
        def __getattribute__(self, name):
            if name == "__iadd__":
                raise AssertionError("operator invoked __getattribute__")
            return object.__getattribute__(self, name)
        def __iadd__(self, other):
            return 10 + other
    value = Number()
    value += 2
    assert value == 12


def getattr_cannot_invent_operator():
    class Number:
        def __getattr__(self, name):
            if name == "__iadd__":
                return lambda other: 999
            raise AttributeError(name)
        def __add__(self, other):
            return 10 + other
    value = Number()
    value += 2
    assert value == 12


def operator_error_is_propagated():
    class Number:
        def __iadd__(self, other):
            raise ValueError("operator error")
        def __add__(self, other):
            return 999
    value = Number()
    try:
        value += 2
    except ValueError as error:
        assert str(error) == "operator error"
    else:
        raise AssertionError("operator exception was swallowed")


def attribute_error_is_propagated():
    class Number:
        def __iadd__(self, other):
            raise AttributeError("operator error")
        def __add__(self, other):
            return 999
    value = Number()
    try:
        value += 2
    except AttributeError as error:
        assert str(error) == "operator error"
    else:
        raise AssertionError("operator AttributeError was swallowed")


def static_operator():
    class Number:
        @staticmethod
        def __iadd__(other):
            return 10 + other
    value = Number()
    value += 2
    assert value == 12


def class_operator():
    class Number:
        @classmethod
        def __iadd__(cls, other):
            assert cls.__name__ == "Number"
            return 10 + other
    value = Number()
    value += 2
    assert value == 12


def property_operator():
    class Number:
        @property
        def __iadd__(self):
            return lambda other: 10 + other
    value = Number()
    value += 2
    assert value == 12


def descriptor_operator():
    class Number:
        __iadd__ = OperatorDescriptor()
    value = Number()
    value += 2
    assert value == 12


def descriptor_error_is_propagated():
    class Number:
        __iadd__ = RaisingOperatorDescriptor()
        def __add__(self, other):
            return 999
    value = Number()
    try:
        value += 2
    except ValueError as error:
        assert str(error) == "descriptor error"
    else:
        raise AssertionError("descriptor exception was swallowed")


def noncallable_operator():
    class Number:
        __iadd__ = None
        def __add__(self, other):
            return 999
    value = Number()
    try:
        value += 2
    except TypeError:
        pass
    else:
        raise AssertionError("non-callable operator did not raise TypeError")


def inherited_operator_changes_are_visible():
    class Base:
        def __init__(self):
            self.total = 0
        def __iadd__(self, other):
            self.total += other
            return self
    class Child(Base):
        pass
    value = Child()
    value += 2
    def replacement(self, other):
        self.total += other * 10
        return self
    Base.__iadd__ = replacement
    value += 3
    assert value.total == 32


def deleting_inherited_operator_uses_fallback():
    class Base:
        def __iadd__(self, other):
            return self
    class Child(Base):
        def __add__(self, other):
            return other + 10
    value = Child()
    value += 1
    del Base.__iadd__
    value += 2
    assert value == 12


def int_subclass_operator():
    class Number(int):
        def __iadd__(self, other):
            return 10 + other
    value = Number(7)
    value += 2
    assert value == 12


cases = [instance_attribute_is_ignored, getattribute_is_bypassed,
         getattr_cannot_invent_operator, operator_error_is_propagated,
         attribute_error_is_propagated, static_operator, class_operator,
         property_operator, descriptor_operator, descriptor_error_is_propagated,
         noncallable_operator, inherited_operator_changes_are_visible,
         deleting_inherited_operator_uses_fallback, int_subclass_operator]
failed = 0
for case in cases:
    try:
        case()
        print("PASS:", case.__name__)
    except Exception as error:
        failed += 1
        print("FAIL:", case.__name__, type(error).__name__, str(error))
print("Failed:", failed)
if failed:
    raise AssertionError("special operator lookup regression")
