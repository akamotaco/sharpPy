# Is the exit order creation order (what docs and PyFinalizer.cs say) or module-dict order?
# Created in the order 1, 2, 3, bound to names in the reverse order.
class T:
    def __init__(self, tag):
        self.tag = tag

    def __del__(self):
        print("exit", self.tag)


first_name = None
second_name = None
_underscore = None
third_name = None

third_name = T("created 1, bound as third_name")
second_name = T("created 2, bound as second_name")
first_name = T("created 3, bound as first_name")
_underscore = T("created 4, bound as _underscore")
print("end of module")
