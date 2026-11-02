class I:
    def __index__(self): return 5
class N:
    def __int__(self): return 7
class F:
    def __float__(self): return 1.5
class FI(float):
    pass
class Bad:
    def __int__(self): return 'x'
class BadI:
    def __index__(self): return 'x'
class Sub(int):
    def __str__(self): return 'sub'
    def __repr__(self): return 'sub'
class IR:
    def __int__(self): raise KeyError('k')
values = [I(), N(), F(), FI(2.5), Bad(), BadI(), Sub(3), IR(), 2j, 'a', b'a', None, 1.5, True, float('inf'), float('nan'), 10**30]
values_names = 'I N F FI Bad BadI Sub IR 2j a ba None 1.5 True inf nan big'.split()
for fmt in ('%d', '%i', '%u', '%o', '%x', '%X', '%5d', '%-5x', '%#x', '%#o', '%+d', '%05d', '%.3d', '%e', '%g', '%c'):
    for maker in (str, lambda s: s.encode()):
        f = maker(fmt)
        for n, v in zip(values_names, values):
            try:
                r = f % (v,)
            except Exception as e:
                r = type(e).__name__ + ': ' + str(e)
            print(fmt, type(f).__name__, n, repr(r))
