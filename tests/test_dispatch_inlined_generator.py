# Regression test: DISPATCH_INLINED must not inline generator methods.
#
# Bug: LoadAttrMethodCacheHit's user-defined method fast path (DISPATCH_INLINED)
# was missing !IsGenerator() && !IsCoroutine() checks. When a method cached by
# LOAD_ATTR happened to be a generator function, the fast path swapped its
# frame directly into the eval loop, skipping the CreateGenerator path. The
# next RETURN_GENERATOR became a no-op, and the following POP_TOP tried to
# pop an empty stack → "RuntimeError: Stack is empty".
#
# Triggering pattern: instance method returns another instance's generator,
# repeated call after the LOAD_ATTR cache has warmed up.

print("=== Test: DISPATCH_INLINED bypassing generator handling ===")

class Player:
    def gen(self):
        yield "p1"
        yield "p2"


class Entrance:
    def __init__(self, player):
        self.player = player

    def proxy(self):
        return self.player.gen()


def call_method(instance, name, args=None):
    if args is None:
        args = []
    method = getattr(instance, name)
    return method(*args)


p = Player()
e = Entrance(p)
globals()["_e"] = e

# First call via eval — warms up the LOAD_ATTR cache.
g1 = eval("call_method(_e, 'proxy', None)")
assert list(g1) == ["p1", "p2"], "First (eval) call failed"
print("  pass: eval-first call")

# Second call via direct invocation — this one hit the DISPATCH_INLINED
# fast path and previously crashed with "Stack is empty".
g2 = call_method(e, "proxy", None)
assert list(g2) == ["p1", "p2"], "Second (direct) call failed"
print("  pass: direct-second call")

print("DISPATCH_INLINED generator fix verified.")
