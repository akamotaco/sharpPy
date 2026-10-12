"""Parsing probe exposed str.join rejecting an iterable string."""
print('_'.join('123'))
print('_'.join(iter(['a', 'b'])))
