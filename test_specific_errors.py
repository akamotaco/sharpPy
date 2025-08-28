# Test specific parsing errors

# 1. Type parameter issue
class Advanced[T: int, *Ts, **P]: pass

# 2. Function kwargs issue
def func(a, b=1, *args, **kwargs): pass

# 3. For loop issue  
for x in range(10): continue

# 4. Augmented assignment
x = 5
x += 5
x *= 2

# 5. Yield from issue
def gen():
    yield from range(5)