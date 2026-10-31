# L2.4 acceptance. PyLong_AsLong leaves OverflowError set and the status is -1, the same pending-error
# path as l24_write_bad_str.py. Without a hook the report is the default one, four writes.
# Expected status -1 as the platform reports it.
import sys


class W:
    def write(self, s):
        sys.stdout.write("W:%r\n" % (s,))

    def flush(self):
        pass


sys.stderr = W()
sys.exit(2**70)
