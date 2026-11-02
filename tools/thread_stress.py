"""Threads change containers while others walk them, with Python code running in the middle.

Under the GIL a switch comes only at a safe point, but a user __eq__ or __hash__ is one, so a C#
walk over a list or a dict can see another thread's change. Python exceptions are expected here.
A SystemError is a C# exception that got out, and a wrong count is a lost update.

usage: sharppy tools/thread_stress.py [seconds]
"""
import collections
import sys
import threading
import time

seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 3
sys.setswitchinterval(1e-6)
stop = time.time() + seconds
leaks = collections.Counter()
runs = collections.Counter()
expected = collections.Counter()
allowed = (RuntimeError, ValueError, KeyError, IndexError, TypeError, StopIteration)


class Key:
    def __init__(self, value):
        self.value = value

    def __hash__(self):
        return self.value % 7

    def __eq__(self, other):
        for _ in range(3):
            pass
        return isinstance(other, Key) and self.value == other.value

    def __lt__(self, other):
        return self.value < other.value


shared_list = [Key(i) for i in range(64)]
shared_dict = {Key(i): i for i in range(64)}
shared_set = {Key(i) for i in range(64)}
shared_deque = collections.deque(range(64), maxlen=128)
shared_text = {}


def guarded(action):
    def run():
        while time.time() < stop:
            try:
                action()
                runs[action.__name__] += 1
            except allowed as error:
                expected[f"{action.__name__}: {type(error).__name__}: {str(error)[:60]}"] += 1
            except BaseException as error:
                leaks[f"{action.__name__}: {type(error).__name__}: {error}"] += 1
    return run


def walk_list():
    for item in list(shared_list):
        item in shared_list
    sorted(shared_list)
    shared_list.index(shared_list[0])
    shared_list.count(shared_list[0])
    [x for x in shared_list]
    shared_list == list(shared_list)


def change_list():
    shared_list.append(Key(1))
    shared_list.insert(0, Key(2))
    if len(shared_list) > 80:
        del shared_list[10:30]
    shared_list.reverse()
    try:
        shared_list.sort()
    except ValueError:
        pass


def walk_dict():
    for key, value in shared_dict.items():
        shared_dict.get(key)
    list(shared_dict)
    dict(shared_dict)
    shared_dict == dict(shared_dict)
    {**shared_dict}
    Key(3) in shared_dict


def change_dict():
    shared_dict[Key(int(time.time() * 1000) % 256)] = 1
    if len(shared_dict) > 80:
        shared_dict.popitem()
        shared_dict.pop(next(iter(shared_dict)), None)
    shared_dict.update({Key(1): 2})


def walk_set():
    for key in shared_set:
        key in shared_set
    set(shared_set) | shared_set
    shared_set & {Key(1)}
    shared_set == set(shared_set)
    sorted(shared_set)


def change_set():
    shared_set.add(Key(int(time.time() * 1000) % 256))
    if len(shared_set) > 80:
        shared_set.pop()
    shared_set.discard(Key(1))


def walk_deque():
    for item in shared_deque:
        pass
    list(shared_deque)
    5 in shared_deque


def change_deque():
    shared_deque.append(1)
    shared_deque.appendleft(2)
    shared_deque.rotate(1)
    if len(shared_deque) > 100:
        shared_deque.pop()


def text():
    shared_text[threading.get_ident()] = "".join(str(i) for i in range(20))
    repr(shared_text)
    getattr(shared_list, "append")
    str(shared_list[:3])


class Counted:
    total = 0


def lifetime():
    class Local:
        def __del__(self):
            Counted.total += 0
    Local()
    type("Made", (object,), {"x": 1})


counter = [0]
lock = threading.Lock()


def locked():
    for _ in range(100):
        with lock:
            counter[0] += 1
    counts[threading.get_ident()] = counts.get(threading.get_ident(), 0) + 100


counts = {}
actions = [walk_list, walk_list, change_list, walk_dict, walk_dict, change_dict, walk_set, change_set,
           walk_deque, change_deque, text, lifetime, locked, locked]
threads = [threading.Thread(target=guarded(action)) for action in actions]
for thread in threads:
    thread.start()
for thread in threads:
    thread.join()
print("threads", len(threads), "lock count", counter[0] == sum(counts.values()))
for leak, count in leaks.most_common(30):
    print(count, leak[:150])
print("runs", dict(runs))
for text_seen, count in expected.most_common(14):
    print(count, text_seen)
print("leaks", len(leaks))
