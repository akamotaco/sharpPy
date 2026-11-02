"""Compare CPython's language tests under SharpPy with the same tests under CPython.

One module at a time, each with a time and a memory limit; everything stops if the system runs short of
memory. Nothing from Lib/test is copied: the tests are read from the CPython checkout.

usage: run_cpython_tests.py [module ...]
"""
import ctypes
import ctypes.wintypes as w
import json
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
CPYTHON_LIB = os.path.abspath(os.path.join(ROOT, "..", "cpython-v3.12.15", "Lib"))
DOTNET = os.path.expanduser("~/AppData/Local/SharpPy/dotnet/dotnet.exe")
DLL = os.path.join(ROOT, "bin", "Release", "net8.0", "sharppy.dll")
DRIVER = os.path.join(ROOT, "tools", "cpython_test_driver.py")
OUT = os.path.join(ROOT, ".artifacts", "cpython_tests")
LIMIT_MB, TIME_LIMIT, SYSTEM_FLOOR_MB = 6000, 120, 8000

MODULES = """test_grammar test_class test_scope test_unpack test_unpack_ex test_extcall test_keywordonlyarg
test_positional_only_arg test_named_expressions test_opcodes test_augassign test_binop test_compare test_richcmp
test_contains test_decorators test_funcattrs test_metaclass test_super test_property test_slice test_enumerate
test_sort test_with test_generators test_yield_from test_iter test_exceptions test_except_star
test_exception_group test_patma test_string_literals test_fstring test_format test_type_params test_abc
test_bool test_int test_float test_complex test_list test_tuple test_dict test_dictviews test_set test_range
test_bytes test_builtin test_descr test_coroutines test_asyncgen test_marshal test_code""".split()


class MemStatus(ctypes.Structure):
    _fields_ = [("dwLength", w.DWORD), ("dwMemoryLoad", w.DWORD), ("ullTotalPhys", ctypes.c_ulonglong),
                ("ullAvailPhys", ctypes.c_ulonglong), ("ullTotalPageFile", ctypes.c_ulonglong),
                ("ullAvailPageFile", ctypes.c_ulonglong), ("ullTotalVirtual", ctypes.c_ulonglong),
                ("ullAvailVirtual", ctypes.c_ulonglong), ("ullAvailExtendedVirtual", ctypes.c_ulonglong)]


class Counters(ctypes.Structure):
    _fields_ = [("cb", w.DWORD), ("PageFaultCount", w.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t), ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t), ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t), ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t)]


kernel32 = ctypes.WinDLL("kernel32")
psapi = ctypes.WinDLL("psapi")
kernel32.GlobalMemoryStatusEx.argtypes = [ctypes.POINTER(MemStatus)]
psapi.GetProcessMemoryInfo.argtypes = [w.HANDLE, ctypes.POINTER(Counters), w.DWORD]


def available_mb():
    status = MemStatus()
    status.dwLength = ctypes.sizeof(MemStatus)
    kernel32.GlobalMemoryStatusEx(ctypes.byref(status))
    return status.ullAvailPhys // 1048576


def peak_mb(handle):
    counters = Counters()
    counters.cb = ctypes.sizeof(Counters)
    psapi.GetProcessMemoryInfo(handle, ctypes.byref(counters), counters.cb)
    return counters.PeakPagefileUsage // 1048576


def guarded(command, env):
    """Returns (how it ended, seconds, peak MB)."""
    started = time.time()
    p = subprocess.Popen(command, cwd=ROOT, env=env, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                         stderr=subprocess.DEVNULL)
    state, peak = "done", 0
    while p.poll() is None:
        peak = max(peak, peak_mb(int(p._handle)))
        if peak > LIMIT_MB:
            state = "KILLED_MEMORY"
        elif time.time() - started > TIME_LIMIT:
            state = "KILLED_TIME"
        elif available_mb() < SYSTEM_FLOOR_MB:
            state = "ABORT_SYSTEM"
        if state != "done":
            p.kill()
            break
        time.sleep(0.05)
    p.wait()
    return state, round(time.time() - started, 1), peak


def read(path):
    results, last = {}, None
    if os.path.exists(path):
        for line in open(path, encoding="utf-8", errors="replace"):
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 2:
                continue
            if parts[0] == "START":
                last = parts[1]
            elif parts[0] == "FAIL" and results.get(parts[1], ("", ""))[0] == "FAIL":
                pass
            else:
                results[parts[1]] = (parts[0], parts[2] if len(parts) > 2 else "")
                if parts[1] == last:
                    last = None
    return results, last


def main():
    os.makedirs(OUT, exist_ok=True)
    env = dict(os.environ, CPYTHON_LIB=CPYTHON_LIB, PYTHONIOENCODING="utf-8", DOTNET_DbgEnableMiniDump="0")
    summary = []
    for module in sys.argv[1:] or MODULES:
        reference = os.path.join(OUT, module + ".cpython.txt")
        mine = os.path.join(OUT, module + ".sharppy.txt")
        if not os.path.exists(reference) or not read(reference)[0]:
            guarded([sys.executable, DRIVER, module, reference], env)
        state, seconds, peak = guarded([DOTNET, DLL, DRIVER, module, mine], env)
        wanted, _ = read(reference)
        got, running = read(mine)
        passed = [t for t, r in wanted.items() if r[0] == "PASS"]
        same = sum(got.get(t, ("",))[0] == "PASS" for t in passed)
        failing = [(t, got[t][0], got[t][1]) for t in passed if t in got and got[t][0] != "PASS"]
        missing = sum(t not in got for t in passed)
        entry = dict(module=module, state=state, seconds=seconds, peak_mb=peak, cpython=len(passed), sharppy=same,
                     failing=len(failing), not_reached=missing, stuck_in=running,
                     import_error=next((r[1] for t, r in got.items() if r[0] == "IMPORT"), None))
        summary.append(entry)
        print(json.dumps(entry, ensure_ascii=False)[:300], flush=True)
        with open(os.path.join(OUT, module + ".diff.txt"), "w", encoding="utf-8") as report:
            for test, status, detail in failing:
                report.write(f"{status}\t{test}\t{detail}\n")
        if state == "ABORT_SYSTEM":
            print("stopped: the system is short of memory")
            break
    total = sum(e["cpython"] for e in summary)
    print("TOTAL", sum(e["sharppy"] for e in summary), "of", total)


main()
