"""Prepared mapping identity, custom item methods and lookup after deletion."""
import sys

events = []
prepared = {}
seed = 100

class Namespace(dict):
    def __getitem__(self, key):
        if key == 'explode':
            raise ValueError('read failure')
        return dict.__getitem__(self, key)

    def __setitem__(self, key, value):
        if key == 'injected':
            events.append('write')
        dict.__setitem__(self, key, value)

    def __delitem__(self, key):
        if key == 'blocked':
            raise ValueError('delete failure')
        if key == 'seed':
            events.append('delete')
        dict.__delitem__(self, key)

class Meta(type):
    @classmethod
    def __prepare__(mcls, name, bases):
        namespace = Namespace()
        namespace['seed'] = 5
        prepared['namespace'] = namespace
        return namespace

    def __new__(mcls, name, bases, namespace):
        assert namespace is prepared['namespace']
        return type.__new__(mcls, name, bases, namespace)

class Example(metaclass=Meta):
    namespace = locals()
    assert namespace is prepared['namespace']
    assert namespace is sys._getframe().f_locals
    assert seed == 5
    namespace['injected'] = 8
    assert injected == 8
    del seed
    assert seed == 100
    read_error = False
    delete_error = False

try:
    class ReadError(metaclass=Meta):
        explode
except ValueError:
    read_error = True
else:
    raise AssertionError('mapping read exception was swallowed')

try:
    class DeleteError(metaclass=Meta):
        del blocked
except NameError:
    delete_error = True
else:
    raise AssertionError('DELETE_NAME error must become NameError')

assert events == ['write', 'delete']
assert Example.injected == 8 and read_error and delete_error
assert not hasattr(Example, 'seed')
print('prepared namespace regression: PASS')
