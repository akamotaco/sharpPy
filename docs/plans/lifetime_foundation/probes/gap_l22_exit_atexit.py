"""P0-53 x L2: the exit code when atexit callbacks are involved.

  C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe .artifacts/tmp/l22_exit_atexit.py

CPython decides the status in _Py_HandleSystemExit (Python/pythonrun.c:708) before the
interpreter tears down, and atexit_callfuncs reports whatever a callback raises
(Modules/atexitmodule.c:139) - so a sys.exit() inside a callback cannot change the code.
stderr is compared too here, because "Exception ignored in atexit callback" is the only
witness that the inner SystemExit was dropped rather than run.
"""
import subprocess
import sys
import os

ROOT = r"C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy"
DOTNET = r"C:/Users/AMD-MAX395-128G-2T/AppData/Local/SharpPy/dotnet/dotnet.exe"
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")

HEAD = "import atexit, sys\n"

CASES = {
    "exit then callback": """
atexit.register(print, 'bye')
print('body')
sys.exit(3)
""",
    "callback raises SystemExit": """
atexit.register(sys.exit, 5)
print('body')
""",
    "exit 3, callback also exits": """
atexit.register(sys.exit, 5)
print('body')
sys.exit(3)
""",
    "callback exits, program raises": """
atexit.register(sys.exit, 5)
print('body')
raise ValueError('boom')
""",
    "callback prints then exits inside": """
def cb():
    print('cb')
    sys.exit(9)
atexit.register(cb)
atexit.register(print, 'older')
print('body')
""",
}

for name, src in CASES.items():
    path = os.path.join(ROOT, ".artifacts", "tmp", "l22_ea.py")
    open(path, "w", encoding="utf-8").write(HEAD + src)
    ref = subprocess.run([sys.executable, path], capture_output=True, text=True, encoding="utf-8")
    sp = subprocess.run([DOTNET, DLL, path], capture_output=True, text=True, encoding="utf-8")
    same = (ref.returncode == sp.returncode and ref.stdout == sp.stdout)
    print("== %-30s %s rc ref=%-4d sp=%-4d" % (name, "MATCH" if same else "DIFF", ref.returncode, sp.returncode))
    print("   ref out=%s" % ascii(ref.stdout.strip()))
    print("   sp  out=%s" % ascii(sp.stdout.strip()))
    if not same or True:
        print("   ref err=%s" % ascii(ref.stderr.strip()[:90]))
        print("   sp  err=%s" % ascii(sp.stderr.strip()[:90]))
