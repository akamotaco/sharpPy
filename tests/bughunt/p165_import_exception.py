# _load_unlocked: what a module raises reaches the importer unchanged.
import os
import sys
sys.dont_write_bytecode = True


folder = os.path.abspath("p165_import_exception_modules")
os.makedirs(folder, exist_ok=True)
sys.path.insert(0, folder)


def write(name, text):
    with open(os.path.join(folder, name + ".py"), "w") as f:
        f.write(text)


write("raises_value", "x = 1\nraise ValueError('from the module')\n")
write("raises_import", "import does_not_exist_anywhere\n")
write("raises_custom", "class Mine(Exception):\n    pass\nraise Mine(1, 2)\n")
write("raises_keyboard", "raise KeyboardInterrupt\n")
write("raises_exit", "import sys\nsys.exit(3)\n")
write("raises_nested", "import raises_value\n")
write("raises_syntax", "def\n")
write("raises_zero", "1 / 0\n")

for name in ("raises_value", "raises_import", "raises_custom", "raises_keyboard", "raises_exit", "raises_nested", "raises_syntax", "raises_zero"):
    try:
        __import__(name)
    except BaseException as e:
        print(name, type(e).__name__, e.args if name != "raises_syntax" else "", name in sys.modules)
    else:
        print(name, "imported")

try:
    import raises_value
except ValueError as e:
    tb = e.__traceback__
    names = []
    while tb:
        names.append(os.path.basename(tb.tb_frame.f_code.co_filename))
        tb = tb.tb_next
    print(names[0], names[-1])

try:
    import raises_import
except ImportError as e:
    print(type(e).__name__, e.name, e.msg)

sys.path.remove(folder)
for name in list(os.listdir(folder)):
    path = os.path.join(folder, name)
    if os.path.isfile(path):
        os.remove(path)
os.rmdir(folder)
