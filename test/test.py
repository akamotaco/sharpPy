from test.folder import m
from test.folder.z import m as w
import test.folder.z.m as q
import test.func as func

print(func.sum(1,2))

print(int(3.7))    # PythonInt
print(float(5) )   # PythonFloat  
print(type(2) )    # 'int'
print(type(2.0))   # 'float'
print(type(2/1))   # 'float' (Python3 방식)

print(globals())
def a():
    return 1
print(globals())

print(q.mul(2,3))  # from folder.z.m import mul