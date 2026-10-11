"""Pending: deprecated throw signatures must emit the CPython 3.12 warning."""
async def worker(): return 1
c = worker()
try:
    c.throw(ValueError, 'legacy')
except ValueError as error:
    print(type(error).__name__, str(error))
finally:
    c.close()
