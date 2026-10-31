import sys
class W:
    def write(self, s):
        print("captured:", repr(s), flush=True)
    def flush(self):
        pass
sys.stderr = W()
sys.exit("boom")
