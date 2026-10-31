# L2.4 acceptance. A failed PyFile_WriteObject leaves the error set: the "\n" is still written, and
# wait_for_thread_shutdown reports it with PyErr_WriteUnraisable(NULL), so it has no "Exception
# ignored in" heading and comes before atexit (Python/pylifecycle.c:1830, :1845, :3045).
# Expected status 1.
import atexit
import sys


class W:
    def write(self, s):
        sys.stdout.write("W:%r\n" % (s,))

    def flush(self):
        pass


class B:
    def __str__(self):
        return 1


def hook(u):
    print("hook", u.exc_type.__name__, u.exc_value, u.err_msg, u.object)


atexit.register(print, "atexit")
sys.stderr = W()
sys.unraisablehook = hook
sys.exit(B())
