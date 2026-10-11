"""KeyError constructor, formatting and mapping-generated key identity."""
for args in ((), ('',), ('key',), (17,), (None,), ((1, 2),), ('a', 2)):
    error = KeyError(*args)
    print('construct', error.args, str(error), repr(error))
    assert error.args == args
    error.args = ('changed',)
    print('changed', str(error), repr(error))
try:
    KeyError(key=1)
except TypeError as error:
    print('keyword', str(error))

events = []
class Key:
    def __hash__(self): return 17
    def __repr__(self):
        events.append('repr')
        return 'KEY'
    def __str__(self):
        events.append('str')
        return 'wrong'
key = Key()
for operation in ('get', 'delete', 'pop', 'set-remove'):
    events.clear()
    try:
        if operation == 'get': {}[key]
        elif operation == 'delete': del {}[key]
        elif operation == 'pop': {}.pop(key)
        else: set().remove(key)
    except KeyError as error:
        print(operation, error.args[0] is key, events)
        print('format', str(error), events)

for key in ('key', 17, None, (1, 2)):
    for operation in ('get', 'delete', 'pop'):
        try:
            if operation == 'get': {}[key]
            elif operation == 'delete': del {}[key]
            else: {}.pop(key)
        except KeyError as error:
            print(operation, error.args, str(error))
for operation in ('dict-popitem', 'set-pop'):
    try:
        if operation == 'dict-popitem': {}.popitem()
        else: set().pop()
    except KeyError as error:
        print(operation, error.args, str(error))

# Merely raising/catching an exception must not format its arguments.
events.clear()
for cls in (KeyError, ValueError):
    error = cls(Key())
    try:
        raise error
    except cls as caught:
        print('raise', cls.__name__, caught is error, events)

# Attribute dictionaries and module globals must retain the original missing key.
class Object: pass
obj = Object()
def function(): pass
for mapping in (obj.__dict__, function.__dict__, globals()):
    try:
        mapping.pop('missing-key-unique')
    except KeyError as error:
        print('mapping-pop', error.args, str(error))

class BadRepr:
    def __hash__(self): return 27
    def __repr__(self): raise ValueError('repr failed')
class WrongRepr:
    def __repr__(self): return 17
for key in (BadRepr(), WrongRepr()):
    error = KeyError(key)
    print('repr-deferred', error.args[0] is key)
    try:
        str(error)
    except (ValueError, TypeError) as failure:
        print('repr-error', type(failure).__name__, str(failure))

from _collections import defaultdict
for mapping in (defaultdict(None), Object.__dict__):
    try:
        mapping['SHARPPY_MISSING_KEY_76_9C038F1B']
    except KeyError as error:
        print('mapping-get', error.args, str(error))
