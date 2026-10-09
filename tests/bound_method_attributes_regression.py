class Example:
    def method(self):
        return 7

function = Example.method
function.__override__ = True
function.tag = [1]
instance = Example()
bound = instance.method
assert bound.__override__ is True
assert bound.tag is function.tag
function.tag = [2]
assert bound.tag == [2]
assert bound.__self__ is instance
assert bound.__func__ is function
assert bound.__class__ is type(bound)
assert not hasattr(bound, 'missing_attribute')
assert bound() == 7
print('bound method attributes: PASS')
