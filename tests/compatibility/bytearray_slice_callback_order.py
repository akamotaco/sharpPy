# CPython reconverts the original slice after converting a non-bytearray RHS.
calls = []
class Index:
    def __init__(self, name, value):
        self.name = name
        self.value = value
    def __index__(self):
        calls.append(self.name)
        return self.value

data = bytearray(b'abc')
data[Index('start', 0):Index('stop', 3):Index('step', 2)] = b'XY'
assert data == bytearray(b'XbY')
assert calls == ['step', 'start', 'stop', 'step', 'start', 'stop']
print('bytearray slice callback order: PASS')
