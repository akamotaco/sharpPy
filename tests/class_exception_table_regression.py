"""Class handlers, finally paths and nested code units must be independent."""
events = []

class Handled:
    try:
        10 // 0
    except ZeroDivisionError as error:
        answer = 7
        assert isinstance(error, ZeroDivisionError)
    else:
        raise AssertionError('unexpected else')
    finally:
        events.append('handled-finally')
assert Handled.answer == 7
assert not hasattr(Handled, 'error')

class Normal:
    try:
        answer = 8
    except ValueError:
        raise AssertionError('unexpected handler')
    else:
        events.append('normal-else')
    finally:
        events.append('normal-finally')
assert Normal.answer == 8

class Outer:
    try:
        class Plain:
            marker = 1

        def divided(value):
            try:
                return 10 // value
            except ZeroDivisionError:
                return -1

        class InnerHandled:
            try:
                raise KeyError('inner')
            except KeyError:
                marker = True
            finally:
                events.append('inner-finally')

        assert divided(0) == -1
        assert InnerHandled.marker
        raise ValueError('outer')
    except ValueError:
        caught = True
    finally:
        events.append('outer-finally')
assert Outer.caught
assert Outer.Plain.marker == 1

try:
    class Propagated:
        try:
            raise ValueError('unhandled')
        finally:
            events.append('propagated-finally')
except ValueError:
    events.append('propagated-caught')
else:
    raise AssertionError('exception was swallowed')

assert events == ['handled-finally', 'normal-else', 'normal-finally',
                  'inner-finally', 'outer-finally', 'propagated-finally', 'propagated-caught']
print('class exception table regression: PASS')
