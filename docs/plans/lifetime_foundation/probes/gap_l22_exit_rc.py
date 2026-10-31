"""P0-53: exit codes per argument shape. Run with the reference python.

  C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe .artifacts/tmp/l22_exit_rc.py

For every shape it runs the same one-line program under the reference interpreter and
under SharpPy and prints rc plus the two streams separately, because CPython writes the
non-integer exit argument to stderr (Python/pythonrun.c:746-762) and nothing else.
"""
import subprocess
import sys
import os

ROOT = r"C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy"
DOTNET = r"C:/Users/AMD-MAX395-128G-2T/AppData/Local/SharpPy/dotnet/dotnet.exe"
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")

SHAPES = [
    ("no arg", "sys.exit()"),
    ("None", "sys.exit(None)"),
    ("0", "sys.exit(0)"),
    ("3", "sys.exit(3)"),
    ("negative", "sys.exit(-1)"),
    ("True", "sys.exit(True)"),
    ("str", "sys.exit('bye')"),
    ("float", "sys.exit(1.5)"),
    ("list", "sys.exit([1, 2])"),
    ("over INT_MAX", "sys.exit(2 ** 40)"),
    ("int subclass", "sys.exit(type('I',(int,),{})(5))"),
    ("INT_MAX", "sys.exit(2 ** 31 - 1)"),
]

for label, code in SHAPES:
    src = "import sys\nprint('body')\n%s\n" % code
    path = os.path.join(ROOT, ".artifacts", "tmp", "l22_shape.py")
    open(path, "w", encoding="utf-8").write(src)
    ref = subprocess.run([sys.executable, path], capture_output=True, text=True, encoding="utf-8")
    sp = subprocess.run([DOTNET, DLL, path], capture_output=True, text=True, encoding="utf-8")
    print("== %-12s rc ref=%-4d sp=%-4d" % (label, ref.returncode, sp.returncode))
    print("   ref out=%s err=%s" % (ascii(ref.stdout.strip()), ascii(ref.stderr.strip()[:70])))
    print("   sp  out=%s err=%s" % (ascii(sp.stdout.strip()[:70]), ascii(sp.stderr.strip()[:70])))
