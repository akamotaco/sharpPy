# p78: sys.argv. The harness starts both engines the same way, with the probe path as the only
# argument, so this pins what the harness itself can pin: argv[0] is the script path as given, its
# length is 1, and argv is a real mutable list. CPython rule: sys.argv is a copy of PyConfig.argv
# (Python/sysmodule.c:3612) built by config_update_argv, which slices from the first non-option
# argument (Python/initconfig.c:2723). Command-mode and multi-argument shapes are checked by
# tools/argv_matrix.py, because the script harness cannot express them.
import sys

print('argv count', len(sys.argv))
print('argv0 basename', sys.argv[0].split('/')[-1].split('\\')[-1])
print('argv0 is str', type(sys.argv).__name__, type(sys.argv[0]).__name__)

sys.argv.append('extra')
print('after append', len(sys.argv), sys.argv[-1])
del sys.argv[-1]
print('after delete', len(sys.argv))
print('same list object', sys.argv is sys.argv)
print('iterable', [type(a).__name__ for a in sys.argv])
