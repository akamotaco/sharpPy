import atexit
class O:
    def m(self):
        print("ran")
o = O()
atexit.register(o.m)
atexit.unregister(o.m)
print("done")
