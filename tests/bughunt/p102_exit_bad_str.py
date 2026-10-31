# L2.3 acceptance. A code whose __str__ raises must not take the process down. Expected status 1.
import atexit
import sys


class Bad:
    def __str__(self):
        raise ValueError("no")


atexit.register(print, "atexit still runs", flush=True)
sys.exit(Bad())
