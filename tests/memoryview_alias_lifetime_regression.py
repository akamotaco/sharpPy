data = bytearray(b'abc')
view = memoryview(data)
alias = view
del view
try: data.append(100)
except BufferError: print('alias retains export')
else: raise AssertionError('live alias lost its export')
child = alias[1:]
alias.release()
try: data.append(100)
except BufferError: print('child retains export')
else: raise AssertionError('live child lost its export')
child.release()
data.append(100)
print(data)
