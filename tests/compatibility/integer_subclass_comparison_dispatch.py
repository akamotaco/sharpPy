"""Python overrides and strict-subclass reflected priority."""
class Number(int):
    def __eq__(self, other):
        print('Number.eq')
        return NotImplemented

    def __lt__(self, other):
        print('Number.lt')
        return NotImplemented


class Child(Number):
    def __eq__(self, other):
        print('Child.eq')
        return 'child equality'

    def __gt__(self, other):
        print('Child.gt')
        return 'child greater'


print('equal', Number(1) == Child(2))
print('less', Number(1) < Child(2))
print('fallback', Number(1) == 1)
