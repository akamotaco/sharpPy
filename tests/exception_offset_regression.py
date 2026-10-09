"""CACHE-bearing calls before exceptions must not shift protected byte ranges."""
def after_calls():
    len([])
    len([])
    len([])
    try:
        len([])
        return 10 // 0
    except ZeroDivisionError:
        return 7
assert after_calls() == 7

class AfterCalls:
    len([])
    len([])
    len([])
    try:
        len([])
        10 // 0
    except ZeroDivisionError:
        caught = True
assert AfterCalls.caught
print('exception offset regression: PASS')
