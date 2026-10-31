import gc
import sys

mode = sys.argv[1]
keep = []


class D:
    def __del__(self):
        print("__del__", flush=True)


class R:
    def __del__(self):
        print("__del__ resurrect", flush=True)
        keep.append(self)


def make():
    if mode == "plain":
        D()
    else:
        R()


make()
print("-- collect 1", flush=True)
gc.collect()
print("-- collect 2", flush=True)
gc.collect()
if mode == "res":
    print("-- drop resurrected", flush=True)
    keep.clear()
print("-- collect 3", flush=True)
gc.collect()
print("-- collect 4", flush=True)
gc.collect()
print("end", flush=True)
