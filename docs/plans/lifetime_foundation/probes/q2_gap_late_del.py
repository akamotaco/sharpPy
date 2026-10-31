# NOT an acceptance probe. A recorded gap: do not make Q2 fail on this and do not add a guard to every
# object to pass it. CPython re-fills tp_finalize when __del__ is assigned (update_one_slot), so an
# instance that already exists is finalized too. SharpPy attaches the guard when the instance is
# created, and a plain instance has no guard to find later.
import gc

log = []


class Late:
    pass


o = Late()
Late.__del__ = lambda self: log.append("Late.__del__")
del o
gc.collect()
print(log)
