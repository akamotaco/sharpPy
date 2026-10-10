class BindingMeta(type):
    def value(cls, value):
        # Retain a class cell without depending on the old broken super binding.
        if value < 0:
            return super().__repr__()
        return value

class BindingTarget(metaclass=BindingMeta):
    pass

def classcell_binding(n):
    total = 0
    for i in range(n):
        total += BindingTarget.value(i)
    return total
