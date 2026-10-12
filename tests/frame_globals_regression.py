"""Frame globals are the live module namespace, including retained mappings."""
import sys

def capture(): return sys._getframe().f_globals
namespace = capture()
print('identity', namespace is globals(), capture() is namespace)
namespace['injected'] = 17
def read(): return injected
print('injected', read())
def change():
    global injected
    injected = 23
change()
print('stored', namespace['injected'])
del injected
print('deleted', 'injected' in namespace)
print('frame globals: PASS')
