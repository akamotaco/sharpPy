# L2.4 acceptance. A chained SystemExit is still handled by _Py_HandleSystemExit: no traceback, the
# message on sys.stderr. Expected status 1.
import sys

sys.stderr = sys.stdout
raise SystemExit("m") from ValueError(1)
