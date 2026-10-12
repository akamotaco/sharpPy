"""Reconcile preserved reproductions against a differential run and default suite."""
import argparse
import ast
import json
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--audit', required=True)
    parser.add_argument('--baseline', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    audit = json.loads(Path(args.audit).read_text(encoding='utf-8'))
    baseline = json.loads(Path(args.baseline).read_text(encoding='utf-8'))
    registered = {c['name'].replace('\\', '/') for c in baseline['cases']}
    rows = []
    for case in audit['cases']:
        name = case['name'].replace('\\', '/')
        source = Path(name).read_text(encoding='utf-8-sig')
        description = (ast.get_docstring(ast.parse(source)) or '').splitlines()[0:1]
        rows.append(dict(path=name, status=case['status'], default_suite=name in registered,
                         description=description[0] if description else ''))
    Path(args.output).write_text(json.dumps(rows, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({status: sum(r['status'] == status for r in rows)
                      for status in sorted({r['status'] for r in rows})}, ensure_ascii=False))


if __name__ == '__main__':
    main()
