"""athrow argument checking is deferred; legacy warnings occur at creation."""
import warnings
async def values():
    yield 1
def advance(request):
    try: request.send(None)
    except BaseException as error: print(type(error).__name__, str(error))
for args in ((), (ValueError, 'x', None, None)):
    generator = values()
    with warnings.catch_warnings(record=True) as log:
        warnings.simplefilter('always')
        try:
            request = generator.athrow(*args)
            print('created', len(args), len(log))
            advance(request)
        except Exception as error: print('creation error', type(error).__name__, str(error))
generator = values()
with warnings.catch_warnings(record=True) as log:
    warnings.simplefilter('always')
    request = generator.athrow(ValueError, 'legacy')
    print('legacy', len(log), str(log[0].message) if log else 'missing')
    advance(request)
generator = values()
advance(generator.aclose())
print('closed empty throw')
advance(generator.athrow())
