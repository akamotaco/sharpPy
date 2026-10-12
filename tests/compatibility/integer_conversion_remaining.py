"""Other int conversion APIs audited separately from digit-limit controls."""
def show(label, function):
    try: print(label, function())
    except Exception as error: print(label, type(error).__name__, str(error))
show('bytes', lambda: int(b'123'))
show('bytes-base', lambda: int(b'ff', 16))
show('bytearray', lambda: int(bytearray(b'123')))
class Index:
    def __index__(self): return 16
show('index-base', lambda: int('ff', Index()))
show('keyword-base', lambda: int('ff', base=16))
show('format-large', lambda: format(2**100, 'd'))
