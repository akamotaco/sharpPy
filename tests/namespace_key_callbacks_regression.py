"""Generic namespace keys must use Python hashing/equality and stored-key identity."""
events = []
namespace = globals()
class Key:
    def __hash__(self):
        events.append('hash')
        return hash('__tracked_probe')
    def __eq__(self, other):
        events.append(('eq', other))
        return other == '__tracked_probe'
key = Key()
namespace[key] = 1
events.clear()
print('generic global load', __tracked_probe, events)
events.clear()
namespace['__tracked_probe'] = 2
print('replace generic', namespace[key], events)
events.clear()
del namespace['__tracked_probe']
print('delete generic', events, key in namespace)
namespace['__tracked_probe'] = 3
events.clear()
namespace[key] = 4
print('replace string', __tracked_probe, events)
events.clear()
print('pop string via generic', namespace.pop(key), events)
print('gone', '__tracked_probe' in namespace)

class Reentry:
    def __hash__(self):
        return hash('__tracked_probe')
    def __eq__(self, other):
        namespace.update({'inner_update': 7})
        return False
guard = Reentry()
namespace[guard] = 5
namespace.update({'__tracked_probe': 6, 'after_update': 8})
print('reentrant update', __tracked_probe, inner_update, after_update)
del namespace[guard]
for name in ('__tracked_probe', 'inner_update', 'after_update'):
    del namespace[name]
