# L2.3 acceptance. The message goes to the current sys.stderr, and before atexit runs, because
# _Py_HandleSystemExit writes it and only then Py_Exit finalizes. Expected status 1.
import atexit
import sys

atexit.register(print, "atexit after the message", flush=True)
sys.stderr = sys.stdout
sys.exit("to stdout")
