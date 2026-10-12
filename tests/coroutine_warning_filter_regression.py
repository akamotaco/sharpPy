import warnings

async def worker(): return 1
warnings.simplefilter('ignore', DeprecationWarning)
c = worker()
try: c.throw(ValueError, 'legacy')
except ValueError as error: print('ignore', str(error))
finally: c.close()

warnings.simplefilter('error', DeprecationWarning)
c = worker()
try: c.throw(ValueError, 'legacy')
except DeprecationWarning as error: print('error', str(error))
try: c.send(None)
except StopIteration as end: print('still-created', end.value)

c = worker()
try: c.throw(ValueError('single'))
except ValueError as error: print('single', str(error))
finally: c.close()

c = worker()
wrapper = c.__await__()
try: wrapper.throw(ValueError, 'wrapped')
except DeprecationWarning as error: print('wrapper', str(error))
finally: c.close()
warnings.resetwarnings()

import sys
class Capture:
    def __init__(self): self.text = ''
    def write(self, value): self.text += value
capture = Capture()
saved = sys.stderr
sys.stderr = capture
try:
    warnings.simplefilter('always', DeprecationWarning)
    c = worker()
    try: c.throw(ValueError, 'captured')
    except ValueError: pass
    finally: c.close()
finally: sys.stderr = saved
print('captured', 'DeprecationWarning: the (type, exc, tb)' in capture.text)
