# L1 acceptance. Becomes tests/bughunt/p92_live_slots.py when it passes.
# __del__ of a dropped object creates a finalizable survivor. Every survivor owes one __del__ at exit.
# Measured on 85bd172: CPython 2050 lines, SharpPy 2048, in three runs out of three.
import gc

keep = []
finalized = [0]


class Survivor:
    def __del__(self):
        finalized[0] += 1
        print("survivor", finalized[0], flush=True)


class Dropper:
    def __del__(self):
        keep.append(Survivor())


def drop(n):
    for i in range(n):
        Dropper()


held = [Survivor() for i in range(50)]
drop(1000)
gc.collect()
print("kept", len(keep), flush=True)
drop(1000)
gc.collect()
print("kept", len(keep), flush=True)
print("end")
