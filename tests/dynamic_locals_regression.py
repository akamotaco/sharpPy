"""Default eval/exec locals must observe fast locals and retain added names."""

def dynamic(value):
    value += 1
    local = 3
    assert eval('value + local') == 11
    d = locals()
    exec('copied = value + local')
    assert d['copied'] == 11
    assert locals()['copied'] == 11
    exec('value = 99')
    assert value == 8
    assert d['value'] == 99
    assert locals()['value'] == 8
    assert eval('copied') == 11
dynamic(7)

def empty():
    exec('created = 5')
    assert locals()['created'] == 5
empty()
assert 'created' not in globals()

def captured(value):
    def inner():
        assert value == 4
        assert eval('value') == 4
    inner()
captured(4)
print('dynamic locals regression: PASS')
