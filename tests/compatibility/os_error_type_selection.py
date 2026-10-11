"""Pending: errno subtypes not yet registered in SharpPy."""
for code in (3, 4, 10, 32):
    error = OSError(code, 'message')
    print('errno', code, type(error).__name__, error.errno, str(error))
if hasattr(OSError(), 'winerror'):
    for code in (109, 10054):
        error = OSError(999, 'message', 'file', code)
        print('winerror', code, type(error).__name__, error.errno, str(error))
