import atexit, sys
atexit.register(sys.exit, 5)
sys.exit(3)
