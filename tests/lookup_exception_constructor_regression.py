"""Lookup exception constructor args and writable member attributes."""
for cls in (NameError, UnboundLocalError, AttributeError):
    for args in ((), ('',), ('message',), (17,), (None,), ('a', 2)):
        error = cls(*args)
        print(cls.__name__, error.args, str(error), repr(error), error.name)
        assert error.args == args
        if cls is AttributeError: print('default-obj', error.obj)
    marker = []
    error = cls('message', name=marker)
    print('name-identity', error.name is marker, error.args)
    error.name = 17
    print('name-set', error.name)
    del error.name
    print('name-deleted', error.name)
    del error.name
    print('name-deleted-again', error.name)
    for kwargs in ({'wrong': 1}, {'name': 1, 'wrong': 2}, {'name': 1, 'obj': 2, 'wrong': 3}):
        try:
            cls(**kwargs)
        except TypeError as error:
            print('invalid', cls.__name__, str(error))
        else:
            raise AssertionError('invalid keyword accepted')

marker = []
error = AttributeError(1, 2, obj=marker, name=marker)
print('both-identities', error.name is marker, error.obj is marker, error.args)
error.obj = 19
print('obj-set', error.obj)
del error.obj
print('obj-deleted', error.obj)
del error.obj
print('obj-deleted-again', error.obj)
error.args = ('changed',)
print('args-independent', error.name is marker, error.obj, str(error))
for cls in (NameError, UnboundLocalError, AttributeError):
    error = cls(name=None)
    print('explicit-none', error.args, error.name)
    try:
        raise error
    except cls as caught:
        print('caught-identity', caught is error, caught.name, caught.args)

for cls in (NameError, UnboundLocalError, AttributeError):
    for key in ("bad'name", 'bad"name', 'line\nname'):
        try:
            cls(**{key: 1})
        except TypeError as error:
            print('keyword-text', repr(str(error)))
