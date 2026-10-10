"""A called function shares the caller's handled exception state."""
def reraiser():
    raise

original = ValueError('caller')
try:
    raise original
except ValueError:
    try:
        reraiser()
        assert False, "bare raise returned normally"
    except ValueError as caught:
        assert caught is original
print('caller exception state: PASS')
