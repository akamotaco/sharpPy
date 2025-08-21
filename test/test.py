from folder import m
from folder.z import m as w
import folder.z.m as q
import func as func

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

class A:
    def __init__(self, x):
        self.x = x
    
    def x2(self):
        return self.x * 2
    
    def self(self) -> 'A':
        return self

a = A(10)
print(dir(a))
print(a.x)  # 10
# print(a.zip)
print(dir(func))
print(a.x2())
print(a.self().x)  # 10

print(globals().keys())
print("===============")
print(dir(globals()['__builtins__']))
print(round)