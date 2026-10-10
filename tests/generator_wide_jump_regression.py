for width in (300, 14000):
    source = '''events = []
def wide(flag):
    total = 0
    try:
        for i in range(2):
            if flag:
'''
    source += '                total += 1\n' * width
    source += '''            try:
                value = yield total
                total += value
            except ValueError:
                total += 7
    finally:
        events.append(total)
'''
    namespace = {}
    exec(source, namespace)
    wide = namespace['wide']
    events = namespace['events']
    for flag in (False, True):
        step = width if flag else 0
        g = wide(flag)
        assert next(g) == step
        assert g.send(3) == 2 * step + 3
        try:
            g.throw(ValueError('injected'))
            assert False
        except StopIteration:
            pass
        assert events[-1] == 2 * step + 10
        g = wide(flag)
        assert next(g) == step
        g.close()
        assert events[-1] == step
    print('wide generator:', width, events)
