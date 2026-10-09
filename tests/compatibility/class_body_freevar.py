# A class body nested in a function can refer to that function's local class.
def build():
    class Local:
        pass
    class Holder:
        item = Local()
    return Holder.item


assert type(build()).__name__ == "Local"
print("class body free variable: PASS")
