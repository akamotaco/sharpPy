"""Equality/hash contract and identity before equality in both set families."""
for factory in (set, frozenset):
    for values in ([1, 1.0, True], [0, -0.0, False], [2**61, float(2**61)],
                   [(1, 2), (1.0, 2.0)], [float('inf'), float('inf')]):
        result = factory(values)
        print(factory.__name__, len(result), all(x in result for x in values))
    nan = float('nan')
    result = factory([nan, nan])
    print('nan', len(result), nan in result)
    class Key:
        def __hash__(self): return 123
        def __eq__(self, other): raise ValueError('equality')
    key = Key()
    result = factory([key, key])
    print('identity', len(result), key in result)
    try: Key() in result
    except ValueError as error: print('error', str(error))
    if factory is set:
        result.remove(key)
        print('remove', len(result))
