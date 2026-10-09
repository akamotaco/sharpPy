"""Checks that volatile-field comparison cannot hide meaningful output errors."""
import unittest
from evaluate import stable_output


class VolatileFieldsTests(unittest.TestCase):
    def test_time_fields_are_explicit(self):
        name = 'examples/advanced.py'
        self.assertEqual(stable_output(name, 'Execution time: 0.25 seconds\n')[0],
                         stable_output(name, 'Execution time: 0.5 seconds\n')[0])
        for bad in ('-1', 'nan', 'inf', '1e999'):
            self.assertEqual(stable_output(name, f'Execution time: {bad} seconds\n')[1], [])
        self.assertEqual(stable_output('another.py', 'Execution time: 0.5 seconds\n')[1], [])

    def test_meaningful_values_are_preserved(self):
        name = 'test_python312_missing_features.py'
        text = '  Override marker: False\n  Modified data: wrong\n'
        self.assertEqual(stable_output(name, text), (text, []))
        a = "   Stats: {'age_seconds': 0.1, 'results_count': 2}\n"
        b = "   Stats: {'age_seconds': 0.2, 'results_count': 3}\n"
        self.assertNotEqual(stable_output('test_ultimate_complex_features.py', a)[0],
                            stable_output('test_ultimate_complex_features.py', b)[0])

    def test_memory_address_only(self):
        name = 'test_python312_missing_features.py'
        good = '  Memoryview slice: <memory at 0xABC>\n'
        self.assertEqual(len(stable_output(name, good)[1]), 1)
        for text in ('unrelated: <memory at 0xABC>\n', '  Memoryview slice: <memory at 0x0>\n',
                     '  Memoryview slice: <wrong at 0xABC>\n'):
            self.assertEqual(stable_output(name, text)[1], [])


if __name__ == '__main__':
    unittest.main()
