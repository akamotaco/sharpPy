# L2.4 acceptance. The message is written as it stands, a trailing newline included, and the newline
# of _Py_HandleSystemExit is its own call. Expected status 1.
import sys


class W:
    def write(self, s):
        sys.stdout.write("W:%r\n" % (s,))

    def flush(self):
        pass


sys.stderr = W()
sys.exit("a\n")
