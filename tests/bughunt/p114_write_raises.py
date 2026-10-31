# L2.4 acceptance. A stream whose write raises: the message is lost, the newline is still attempted,
# the status stays 1 and the error is reported once. The stream has a flush, so this is not the
# rc 120 of a failing flush_std_files. Expected status 1.
import sys


class W:
    def write(self, s):
        print("write", repr(s))
        raise ValueError("w")

    def flush(self):
        pass


def hook(u):
    print("hook", u.exc_type.__name__, u.exc_value, u.err_msg, u.object)


sys.stderr = W()
sys.unraisablehook = hook
sys.exit("m")
