# The script directory must be discoverable when launching a file from elsewhere.
from sibling import VALUE

assert VALUE == 42
print(VALUE)
