"""Default unraisable traceback rendering, with stable object repr."""
import sys
class Sink:
    def __init__(self): self.parts = []
    def write(self, value): self.parts.append(value)
    def flush(self): pass
class Pause:
    def __await__(self): return self
    def __iter__(self): return self
    def __next__(self): return 1
    def __repr__(self): return "<pause>"
    @property
    def close(self):
        raise ValueError("lookup")
async def worker():
    await Pause()
sink = Sink()
previous = sys.stderr
sys.stderr = sink
try:
    c = worker()
    c.send(None)
    c.close()
finally:
    sys.stderr = previous
print("".join(sink.parts), end="")
