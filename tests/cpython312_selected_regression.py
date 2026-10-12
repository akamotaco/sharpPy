# Generated from CPython v3.12.15. See cpython312_selected_manifest.json
# and cpython312_LICENSE.txt for provenance and the upstream license.
class Raises:
    def __init__(self, expected): self.expected = expected
    def __enter__(self): return self
    def __exit__(self, kind, value, traceback):
        if kind is None: raise AssertionError('expected exception missing')
        if not isinstance(value, self.expected): return False
        return True

class Checks:
    def assertEqual(self, actual, expected):
        assert actual == expected, (actual, expected)
    def assertRaises(self, expected, function=None, *args, **kwargs):
        if function is None: return Raises(expected)
        with Raises(expected): function(*args, **kwargs)

class IntTestCases(Checks):

    def test_invalid_signs(self):
        with self.assertRaises(ValueError):
            int('+')
        with self.assertRaises(ValueError):
            int('-')
        with self.assertRaises(ValueError):
            int('- 1')
        with self.assertRaises(ValueError):
            int('+ 1')
        with self.assertRaises(ValueError):
            int(' + 1 ')

    def test_unicode(self):
        self.assertEqual(int('१२३४५६७८९०1234567890'), 12345678901234567890)
        self.assertEqual(int('١٢٣٤٥٦٧٨٩٠'), 1234567890)
        self.assertEqual(int('१२३४५६७८९०1234567890', 0), 12345678901234567890)
        self.assertEqual(int('١٢٣٤٥٦٧٨٩٠', 0), 1234567890)

    def test_no_args(self):
        self.assertEqual(int(), 0)

    def test_int_base_bad_types(self):
        """Not integer types are not valid bases; issue16772."""
        with self.assertRaises(TypeError):
            int('0', 5.5)
        with self.assertRaises(TypeError):
            int('0', 5.0)

    def test_string_float(self):
        self.assertRaises(ValueError, int, '1.2')

class TestDecorateSortUndecorate(Checks):

    def test_baddecorator(self):
        data = 'The quick Brown fox Jumped over The lazy Dog'.split()
        self.assertRaises(TypeError, data.sort, key=lambda x, y: 0)

    def test_key_with_exception(self):
        data = list(range(-2, 2))
        dup = data[:]
        self.assertRaises(ZeroDivisionError, data.sort, key=lambda x: 1 / x)
        self.assertEqual(data, dup)

    def test_key_with_mutation(self):
        data = list(range(10))

        def k(x):
            del data[:]
            data[:] = range(20)
            return x
        self.assertRaises(ValueError, data.sort, key=k)

class TestOptimizedCompares(Checks):

    def test_not_all_tuples(self):
        self.assertRaises(TypeError, [(1.0, 1.0), (False, 'A'), 6].sort)
        self.assertRaises(TypeError, [('a', 1), (1, 'a')].sort)
        self.assertRaises(TypeError, [(1, 'a'), ('a', 1)].sort)

    def test_none_in_tuples(self):
        expected = [(None, 1), (None, 2)]
        actual = sorted([(None, 2), (None, 1)])
        self.assertEqual(actual, expected)

IntTestCases().test_invalid_signs()
print('test_int.py:IntTestCases.test_invalid_signs PASS')
IntTestCases().test_unicode()
print('test_int.py:IntTestCases.test_unicode PASS')
IntTestCases().test_no_args()
print('test_int.py:IntTestCases.test_no_args PASS')
IntTestCases().test_int_base_bad_types()
print('test_int.py:IntTestCases.test_int_base_bad_types PASS')
IntTestCases().test_string_float()
print('test_int.py:IntTestCases.test_string_float PASS')
TestDecorateSortUndecorate().test_baddecorator()
print('test_sort.py:TestDecorateSortUndecorate.test_baddecorator PASS')
TestDecorateSortUndecorate().test_key_with_exception()
print('test_sort.py:TestDecorateSortUndecorate.test_key_with_exception PASS')
TestDecorateSortUndecorate().test_key_with_mutation()
print('test_sort.py:TestDecorateSortUndecorate.test_key_with_mutation PASS')
TestOptimizedCompares().test_not_all_tuples()
print('test_sort.py:TestOptimizedCompares.test_not_all_tuples PASS')
TestOptimizedCompares().test_none_in_tuples()
print('test_sort.py:TestOptimizedCompares.test_none_in_tuples PASS')
