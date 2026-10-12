"""Extract explicitly selected CPython 3.12.15 methods without test.support.

Method bodies and expected exceptions remain unchanged; Checks replaces only the
unittest assertion runner. The manifest pins the input hashes and method names.
"""
import argparse
import ast
import hashlib
import json
from pathlib import Path

SELECTION = {
    'test_int.py': {'IntTestCases': ['test_invalid_signs', 'test_unicode', 'test_no_args',
                                    'test_int_base_bad_types', 'test_string_float']},
    'test_sort.py': {
        'TestDecorateSortUndecorate': ['test_baddecorator', 'test_key_with_exception', 'test_key_with_mutation'],
        'TestOptimizedCompares': ['test_not_all_tuples', 'test_none_in_tuples'],
    },
}
EXPECTED_SHA256 = {
    'test_int.py': '6f4fba227e4afbd1663ea7ff8a3a9a2bcc995d1a78b30e0ac0f526daa50b5ee8',
    'test_sort.py': 'd488b94e23579cf4f62fee1230952a719ef82797743ff9285c3bee96e44501a2',
}
SUPPORT = '''# Generated from CPython v3.12.15. See cpython312_selected_manifest.json
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

'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', required=True)
    parser.add_argument('--output-dir', default='tests')
    args = parser.parse_args()
    root = Path(args.source_root)
    output = Path(args.output_dir)
    parts = [SUPPORT]
    manifest = dict(tag='v3.12.15', commit='e848e4b09ca4bc67c0041c9ce7526dae471d4c36',
                    adaptation='Unchanged selected method AST bodies; minimal assertion runner; no test.support imports.', sources=[])
    calls = []
    for filename, classes in SELECTION.items():
        raw = (root / 'Lib/test' / filename).read_bytes()
        digest = hashlib.sha256(raw).hexdigest()
        if digest != EXPECTED_SHA256[filename]:
            raise ValueError(f'{filename}: source differs from the pinned CPython 3.12.15 input')
        module = ast.parse(raw.decode('utf-8'))
        item = dict(path='Lib/test/' + filename, sha256=digest,
                    url='https://github.com/python/cpython/blob/' + manifest['commit'] + '/Lib/test/' + filename,
                    methods=[])
        for class_name, methods in classes.items():
            original = next(node for node in module.body if isinstance(node, ast.ClassDef) and node.name == class_name)
            chosen = []
            for name in methods:
                method = next(node for node in original.body if isinstance(node, ast.FunctionDef) and node.name == name)
                if method.decorator_list: raise ValueError('Do not silently drop decorators')
                chosen.append(method)
                item['methods'].append(class_name + '.' + name)
                calls.append(f"{class_name}().{name}()\nprint('{filename}:{class_name}.{name} PASS')\n")
            selected = ast.ClassDef(name=class_name, bases=[ast.Name(id='Checks', ctx=ast.Load())],
                                    keywords=[], body=chosen, decorator_list=[], type_params=[])
            parts.append(ast.unparse(ast.fix_missing_locations(selected)) + '\n\n')
        manifest['sources'].append(item)
    parts.extend(calls)
    (output / 'cpython312_selected_regression.py').write_text(''.join(parts), encoding='utf-8')
    (output / 'cpython312_selected_manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')


if __name__ == '__main__':
    main()
