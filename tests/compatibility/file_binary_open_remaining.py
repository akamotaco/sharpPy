"""Binary file access and eager opening remain outside the text-only facade."""
import sys
path = sys._getframe().f_code.co_filename
with open(path, 'rb') as source:
    data = source.read(3)
    print(type(data).__name__, data == b'"""')
