"""Bytecode proximity: SharpPy --dis vs CPython 3.12 dis.

Metrics (diagnostic, not certification):
 1. numbering fidelity: ByteCodeOp enum name->value vs dis.opmap
 2. per-code-object opcode-sequence similarity vs CPython compile()
Run with reference CPython 3.12. Requires a built Release DLL.
"""
import argparse, dis, re, subprocess, sys
from difflib import SequenceMatcher
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def norm_name(n):
    return n.strip().strip("<>")

def sharp_dis(text):
    """parse '📄 Disassembly of NAME:' sections -> {name: [opnames]}"""
    sections, cur = {}, None
    for line in text.splitlines():
        m = re.match(r".*Disassembly of (.+?):\s*$", line)
        if m:
            cur = norm_name(m.group(1)); sections.setdefault(cur, []); continue
        m = re.match(r"\s*\d+:\s+([A-Z_][A-Z0-9_]*)", line)
        if m and cur is not None:
            if cur: sections[cur].append(m.group(1))
    return sections

def cpython_dis(path):
    src = Path(path).read_text(encoding="utf-8-sig")
    code = compile(src, path, "exec")
    out = {}
    def walk(c):
        key = norm_name(c.co_name)
        if key not in out:
            out[key] = [i.opname for i in dis.get_instructions(c)]
        for const in c.co_consts:
            if hasattr(const, "co_name"):
                walk(const)
    walk(code)
    return out

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dotnet", required=True)
    ap.add_argument("--dll", default=str(ROOT / "bin/Release/net8.0/sharppy.dll"))
    ap.add_argument("--files", nargs="*", default=[str(ROOT / "tools/fixtures/bytecode_comparison.py")])
    args = ap.parse_args()
    assert sys.version_info[:2] == (3, 12)

    cs = (ROOT / "runtime/PyBytecode.cs").read_text(encoding="utf-8-sig")
    enum = cs.split("public enum ByteCodeOp : int", 1)[1].split("}", 1)[0]
    sharp_enum = {n: int(v) for n, v in re.findall(r"^\s*(\w+)\s*=\s*(\d+)", enum, re.M)}
    ref = {k: v for k, v in dis.opmap.items() if v < 256}
    same_num = sorted(n for n in ref if sharp_enum.get(n) == ref[n])
    missing = sorted(n for n in ref if n not in sharp_enum)
    print(f"opcode enum: ref={len(ref)} sharppy={len(sharp_enum)} "
          f"same-numbering={len(same_num)} missing={len(missing)}")
    print(f"  missing sample: {missing[:12]}")

    seq_ratios, per_file = [], {}
    for f in args.files:
        sp = subprocess.run([args.dotnet, args.dll, "--dis", f], capture_output=True, timeout=120)
        sp_text = sp.stdout.decode("utf-8", errors="replace")
        sp_secs = sharp_dis(sp_text)
        cp_secs = cpython_dis(f)
        rows = []
        for name, cp_seq in cp_secs.items():
            sp_seq = sp_secs.get(name)
            if sp_seq is None:
                rows.append((name, None, len(cp_seq))); continue
            r = SequenceMatcher(a=cp_seq, b=sp_seq).ratio()
            seq_ratios.append(r); rows.append((name, r, len(cp_seq)))
        per_file[f] = rows
        print(f"\n== {Path(f).name}")
        for name, r, ln in rows:
            print(f"  {name:24s} cp-ops={ln:4d} ratio={'MISSING' if r is None else f'{r:.3f}'}")
    if seq_ratios:
        print(f"\nmean sequence-similarity: {sum(seq_ratios)/len(seq_ratios):.3f} over {len(seq_ratios)} code objects")

if __name__ == "__main__":
    main()
