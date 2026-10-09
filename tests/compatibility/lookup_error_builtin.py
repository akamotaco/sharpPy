"""LookupError registration, inheritance and exception matching regression."""
import builtins

assert builtins.LookupError is LookupError
assert issubclass(LookupError, Exception)
assert issubclass(KeyError, LookupError)
assert issubclass(IndexError, LookupError)
assert isinstance(KeyError('key'), LookupError)
assert isinstance(IndexError('index'), LookupError)
assert isinstance(LookupError('lookup'), Exception)
assert str(LookupError('lookup')) == 'lookup'

def catch_lookup(operation):
    try:
        operation()
    except LookupError:
        return True
    return False

def missing_key():
    return {}['missing']

def missing_index():
    return [][0]

def explicit_lookup():
    raise LookupError('explicit')

assert catch_lookup(missing_key)
assert catch_lookup(missing_index)
assert catch_lookup(explicit_lookup)
print('lookup error builtin: PASS')
