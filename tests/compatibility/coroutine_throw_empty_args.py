"""Regression: the inherited exception constructor must retain empty args."""
async def worker(): return 1
c = worker()
try:
    c.throw(ValueError)
except ValueError as error:
    print('arguments', error.args)
finally:
    c.close()
