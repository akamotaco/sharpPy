class A():
    def __init__(self, name:str, age:int):
        self.name = name
        self.age = age

a = A(age=10,name='name')
print(a.name)
print(a.age)