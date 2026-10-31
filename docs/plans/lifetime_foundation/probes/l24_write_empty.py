# L2.4 acceptance. PyFile_WriteObject and PySys_WriteStderr("\n") are two calls whatever the text is
# (Python/pythonrun.c:755, :761). Expected status 1.
import sys


class W:
    def write(self, s):
        sys.stdout.write("W:%r\n" % (s,))

    def flush(self):
        pass


sys.stderr = W()
sys.exit("")
