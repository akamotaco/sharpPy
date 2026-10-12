"""Common valid paths; legacy workload names support historical binaries."""
import sys

def function_call(n):
    result = 0
    for i in range(n):
        if '-'.join(('alpha', 'beta', 'gamma')) == 'alpha-beta-gamma': result += i
    return result

def dictionary(n):
    result = 0
    for i in range(n):
        if repr('alpha beta gamma') == "'alpha beta gamma'": result += i
    return result

def frame_observation(n):
    result = 0
    for i in range(n):
        if len(sys._getframe().f_globals) > 0: result += i
    return result
