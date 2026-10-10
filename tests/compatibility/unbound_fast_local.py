def local():
    return value
    value = 1

try:
    local()
except NameError as error:
    print(type(error).__name__, str(error))
