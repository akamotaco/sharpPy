// Python/compile.c of CPython v3.12.15: PEP 634 structural pattern matching.
using System;
using System.Collections.Generic;
using SharpPy.Generated;

namespace SharpPy
{
    public sealed partial class Compiler
    {
        private sealed class PatternContext
        {
            public List<string> Stores = new();
            public bool AllowIrrefutable;
            public List<Label> FailPop = new();
            public int OnTop;

            public PatternContext Copy() => (PatternContext)MemberwiseClone();
        }

        private static bool WildcardCheck(GeneratedPattern p) => p is GeneratedMatchAs { Name: null };
        private static bool WildcardStarCheck(GeneratedPattern p) => p is GeneratedMatchStar { Name: null };

        private void EnsureFailPop(PatternContext pc, int n)
        {
            int size = n + 1;
            while (pc.FailPop.Count < size)
                pc.FailPop.Add(NewLabel());
        }

        private void JumpToFailPop(Loc loc, PatternContext pc, ByteCodeOp op)
        {
            int pops = pc.OnTop + pc.Stores.Count;
            EnsureFailPop(pc, pops);
            ADDOP_JUMP(loc, op, pc.FailPop[pops]);
        }

        private void EmitAndResetFailPop(Loc loc, PatternContext pc)
        {
            if (pc.FailPop.Count == 0)
                return;
            for (int i = pc.FailPop.Count - 1; i > 0; i--)
            {
                UseLabel(pc.FailPop[i]);
                ADDOP(loc, ByteCodeOp.POP_TOP);
            }
            UseLabel(pc.FailPop[0]);
            pc.FailPop = new List<Label>();
        }

        private PythonException DuplicateStore(Loc loc, string n) =>
            Error(loc, $"multiple assignments to name '{n}' in pattern");

        private void PatternHelperRotate(Loc loc, int count)
        {
            while (1 < count)
                ADDOP_I(loc, ByteCodeOp.SWAP, count--);
        }

        private void PatternHelperStoreName(Loc loc, string? n, PatternContext pc)
        {
            if (n == null)
            {
                ADDOP(loc, ByteCodeOp.POP_TOP);
                return;
            }
            ForbiddenName(loc, n, Ctx.Store);
            if (pc.Stores.Contains(n))
                throw DuplicateStore(loc, n);
            PatternHelperRotate(loc, pc.OnTop + pc.Stores.Count + 1);
            pc.Stores.Add(n);
        }

        private void PatternUnpackHelper(Loc loc, GeneratedSeq elts)
        {
            int n = Len(elts);
            bool seenStar = false;
            for (int i = 0; i < n; i++)
            {
                if (elts[i] is GeneratedMatchStar)
                {
                    if (seenStar)
                        throw Error(loc, "multiple starred expressions in sequence pattern");
                    if (i >= (1 << 8) || n - i - 1 >= (int.MaxValue >> 8))
                        throw Error(loc, "too many expressions in star-unpacking sequence pattern");
                    ADDOP_I(loc, ByteCodeOp.UNPACK_EX, i + ((n - i - 1) << 8));
                    seenStar = true;
                }
            }
            if (!seenStar)
                ADDOP_I(loc, ByteCodeOp.UNPACK_SEQUENCE, n);
        }

        private void PatternHelperSequenceUnpack(Loc loc, GeneratedSeq patterns, PatternContext pc)
        {
            PatternUnpackHelper(loc, patterns);
            int size = Len(patterns);
            pc.OnTop += size;
            for (int i = 0; i < size; i++)
            {
                pc.OnTop--;
                PatternSubpattern((GeneratedPattern)patterns[i], pc);
            }
        }

        private void PatternHelperSequenceSubscr(Loc loc, GeneratedSeq patterns, int star, PatternContext pc)
        {
            pc.OnTop++;
            int size = Len(patterns);
            for (int i = 0; i < size; i++)
            {
                var pattern = (GeneratedPattern)patterns[i];
                if (WildcardCheck(pattern) || i == star)
                    continue;
                ADDOP_I(loc, ByteCodeOp.COPY, 1);
                if (i < star)
                {
                    ADDOP_LOAD_CONST(loc, PyInt.From(i));
                }
                else
                {
                    ADDOP(loc, ByteCodeOp.GET_LEN);
                    ADDOP_LOAD_CONST(loc, PyInt.From(size - i));
                    ADDOP_BINARY(loc, GeneratedSub.Instance, false);
                }
                ADDOP(loc, ByteCodeOp.BINARY_SUBSCR);
                PatternSubpattern(pattern, pc);
            }
            pc.OnTop--;
            ADDOP(loc, ByteCodeOp.POP_TOP);
        }

        private void PatternSubpattern(GeneratedPattern p, PatternContext pc)
        {
            bool allowIrrefutable = pc.AllowIrrefutable;
            pc.AllowIrrefutable = true;
            Pattern(p, pc);
            pc.AllowIrrefutable = allowIrrefutable;
        }

        private void PatternAs(GeneratedMatchAs p, PatternContext pc)
        {
            string? name = p.Name?.Value;
            if (p.Pattern == null)
            {
                if (!pc.AllowIrrefutable)
                {
                    if (name != null)
                        throw Error(LOC(p), $"name capture '{name}' makes remaining patterns unreachable");
                    throw Error(LOC(p), "wildcard makes remaining patterns unreachable");
                }
                PatternHelperStoreName(LOC(p), name, pc);
                return;
            }
            pc.OnTop++;
            ADDOP_I(LOC(p), ByteCodeOp.COPY, 1);
            Pattern(p.Pattern, pc);
            pc.OnTop--;
            PatternHelperStoreName(LOC(p), name, pc);
        }

        private void ValidateKwdAttrs(GeneratedSeq attrs, GeneratedSeq patterns)
        {
            int nattrs = Len(attrs);
            for (int i = 0; i < nattrs; i++)
            {
                string attr = ((GeneratedIdentifier)attrs[i]).Value;
                ForbiddenName(LOC((GeneratedPattern)patterns[i]), attr, Ctx.Store);
                for (int j = i + 1; j < nattrs; j++)
                {
                    if (((GeneratedIdentifier)attrs[j]).Value == attr)
                        throw Error(LOC((GeneratedPattern)patterns[j]), $"attribute name repeated in class pattern: {attr}");
                }
            }
        }

        private void PatternClass(GeneratedMatchClass p, PatternContext pc)
        {
            var at = LOC(p);
            var patterns = p.Patterns;
            var kwdAttrs = p.KwdAttrs;
            var kwdPatterns = p.KwdPatterns;
            int nargs = Len(patterns);
            int nattrs = Len(kwdAttrs);
            int nkwdPatterns = Len(kwdPatterns);
            if (nattrs != nkwdPatterns)
                throw Error(at, $"kwd_attrs ({nattrs}) / kwd_patterns ({nkwdPatterns}) length mismatch in class pattern");
            if (nattrs != 0)
                ValidateKwdAttrs(kwdAttrs, kwdPatterns);
            VisitExpr(p.Cls);
            var attrNames = new PyObject[nattrs];
            for (int i = 0; i < nattrs; i++)
                attrNames[i] = PyString.From(((GeneratedIdentifier)kwdAttrs[i]).Value);
            ADDOP_LOAD_CONST(at, PyTuple.From(attrNames));
            ADDOP_I(at, ByteCodeOp.MATCH_CLASS, nargs);
            ADDOP_I(at, ByteCodeOp.COPY, 1);
            ADDOP_LOAD_CONST(at, PyNone.Instance);
            ADDOP_I(at, ByteCodeOp.IS_OP, 1);
            pc.OnTop++;
            JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            ADDOP_I(at, ByteCodeOp.UNPACK_SEQUENCE, nargs + nattrs);
            pc.OnTop += nargs + nattrs - 1;
            for (int i = 0; i < nargs + nattrs; i++)
            {
                pc.OnTop--;
                var pattern = (GeneratedPattern)(i < nargs ? patterns[i] : kwdPatterns[i - nargs]);
                if (WildcardCheck(pattern))
                {
                    ADDOP(at, ByteCodeOp.POP_TOP);
                    continue;
                }
                PatternSubpattern(pattern, pc);
            }
        }

        private void PatternMapping(GeneratedMatchMapping p, PatternContext pc)
        {
            var at = LOC(p);
            var keys = p.Keys;
            var patterns = p.Patterns;
            int size = Len(keys);
            int npatterns = Len(patterns);
            if (size != npatterns)
                throw Error(at, $"keys ({size}) / patterns ({npatterns}) length mismatch in mapping pattern");
            string? starTarget = p.Rest?.Value;
            pc.OnTop++;
            ADDOP(at, ByteCodeOp.MATCH_MAPPING);
            JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            if (size == 0 && starTarget == null)
            {
                pc.OnTop--;
                ADDOP(at, ByteCodeOp.POP_TOP);
                return;
            }
            if (size != 0)
            {
                ADDOP(at, ByteCodeOp.GET_LEN);
                ADDOP_LOAD_CONST(at, PyInt.From(size));
                ADDOP_COMPARE(at, GeneratedGtE.Instance);
                JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            }

            // compiler_pattern_mapping: a set of the constants, so 0, 0.0, 0j and False are one key.
            var seen = new PySet();
            for (int i = 0; i < size; i++)
            {
                var key = (GeneratedExpr?)keys[i];
                if (key == null)
                    throw Error(LOC((GeneratedPattern)patterns[i]), "can't use NULL keys in MatchMapping (set 'rest' parameter instead)");
                if (key is GeneratedConstant constant)
                {
                    var value = ConstantValue(constant.Value);
                    if (seen.Contains(value).Value)
                        throw Error(at, $"mapping pattern checks duplicate key ({value.ToRepr().Value})");
                    seen.Add(value);
                }
                else if (key is not GeneratedAttribute)
                {
                    throw Error(at, "mapping pattern keys may only match literals and attribute lookups");
                }
                VisitExpr(key);
            }

            ADDOP_I(at, ByteCodeOp.BUILD_TUPLE, size);
            ADDOP(at, ByteCodeOp.MATCH_KEYS);
            pc.OnTop += 2;
            ADDOP_I(at, ByteCodeOp.COPY, 1);
            ADDOP_LOAD_CONST(at, PyNone.Instance);
            ADDOP_I(at, ByteCodeOp.IS_OP, 1);
            JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            ADDOP_I(at, ByteCodeOp.UNPACK_SEQUENCE, size);
            pc.OnTop += size - 1;
            for (int i = 0; i < size; i++)
            {
                pc.OnTop--;
                PatternSubpattern((GeneratedPattern)patterns[i], pc);
            }
            pc.OnTop -= 2;
            if (starTarget != null)
            {
                ADDOP_I(at, ByteCodeOp.BUILD_MAP, 0);
                ADDOP_I(at, ByteCodeOp.SWAP, 3);
                ADDOP_I(at, ByteCodeOp.DICT_UPDATE, 2);
                ADDOP_I(at, ByteCodeOp.UNPACK_SEQUENCE, size);
                while (size != 0)
                {
                    ADDOP_I(at, ByteCodeOp.COPY, 1 + size--);
                    ADDOP_I(at, ByteCodeOp.SWAP, 2);
                    ADDOP(at, ByteCodeOp.DELETE_SUBSCR);
                }
                PatternHelperStoreName(at, starTarget, pc);
            }
            else
            {
                ADDOP(at, ByteCodeOp.POP_TOP);
                ADDOP(at, ByteCodeOp.POP_TOP);
            }
        }

        // The equal constants of a set: 1, 1.0 and True are one key.

        private void PatternOr(GeneratedMatchOr p, PatternContext pc)
        {
            var at = LOC(p);
            var end = NewLabel();
            int size = Len(p.Patterns);
            var oldPc = pc.Copy();
            List<string>? control = null;
            for (int i = 0; i < size; i++)
            {
                var alt = (GeneratedPattern)p.Patterns[i];
                var altLoc = LOC(alt);
                pc.Stores = new List<string>();
                pc.AllowIrrefutable = i == size - 1 && oldPc.AllowIrrefutable;
                pc.FailPop = new List<Label>();
                pc.OnTop = 0;
                ADDOP_I(altLoc, ByteCodeOp.COPY, 1);
                Pattern(alt, pc);
                int nstores = pc.Stores.Count;
                if (i == 0)
                {
                    control = pc.Stores;
                }
                else if (nstores != control!.Count)
                {
                    throw Error(at, "alternative patterns bind different names");
                }
                else if (nstores != 0)
                {
                    int icontrol = nstores;
                    while (icontrol-- != 0)
                    {
                        string name = control[icontrol];
                        int istores = pc.Stores.IndexOf(name);
                        if (istores < 0)
                            throw Error(at, "alternative patterns bind different names");
                        if (icontrol != istores)
                        {
                            int rotations = istores + 1;
                            var rotated = pc.Stores.GetRange(0, rotations);
                            pc.Stores.RemoveRange(0, rotations);
                            pc.Stores.InsertRange(icontrol - istores, rotated);
                            while (rotations-- != 0)
                                PatternHelperRotate(altLoc, icontrol + 1);
                        }
                    }
                }
                ADDOP_JUMP(altLoc, ByteCodeOp.JUMP, end);
                EmitAndResetFailPop(altLoc, pc);
            }
            pc.Stores = oldPc.Stores;
            pc.AllowIrrefutable = oldPc.AllowIrrefutable;
            pc.FailPop = oldPc.FailPop;
            pc.OnTop = oldPc.OnTop;
            ADDOP(at, ByteCodeOp.POP_TOP);
            JumpToFailPop(at, pc, ByteCodeOp.JUMP);

            UseLabel(end);
            int stores = control!.Count;
            int nrots = stores + 1 + pc.OnTop + pc.Stores.Count;
            for (int i = 0; i < stores; i++)
            {
                PatternHelperRotate(at, nrots);
                string name = control[i];
                if (pc.Stores.Contains(name))
                    throw DuplicateStore(at, name);
                pc.Stores.Add(name);
            }
            ADDOP(at, ByteCodeOp.POP_TOP);
        }

        private void PatternSequence(GeneratedMatchSequence p, PatternContext pc)
        {
            var at = LOC(p);
            var patterns = p.Patterns;
            int size = Len(patterns);
            int star = -1;
            bool onlyWildcard = true;
            bool starWildcard = false;
            for (int i = 0; i < size; i++)
            {
                var pattern = (GeneratedPattern)patterns[i];
                if (pattern is GeneratedMatchStar)
                {
                    if (star >= 0)
                        throw Error(at, "multiple starred names in sequence pattern");
                    starWildcard = WildcardStarCheck(pattern);
                    onlyWildcard &= starWildcard;
                    star = i;
                    continue;
                }
                onlyWildcard &= WildcardCheck(pattern);
            }
            pc.OnTop++;
            ADDOP(at, ByteCodeOp.MATCH_SEQUENCE);
            JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            if (star < 0)
            {
                ADDOP(at, ByteCodeOp.GET_LEN);
                ADDOP_LOAD_CONST(at, PyInt.From(size));
                ADDOP_COMPARE(at, GeneratedEq.Instance);
                JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            }
            else if (size > 1)
            {
                ADDOP(at, ByteCodeOp.GET_LEN);
                ADDOP_LOAD_CONST(at, PyInt.From(size - 1));
                ADDOP_COMPARE(at, GeneratedGtE.Instance);
                JumpToFailPop(at, pc, ByteCodeOp.POP_JUMP_IF_FALSE);
            }
            pc.OnTop--;
            if (onlyWildcard)
                ADDOP(at, ByteCodeOp.POP_TOP);
            else if (starWildcard)
                PatternHelperSequenceSubscr(at, patterns, star, pc);
            else
                PatternHelperSequenceUnpack(at, patterns, pc);
        }

        private void PatternValue(GeneratedMatchValue p, PatternContext pc)
        {
            if (p.Value is not (GeneratedConstant or GeneratedAttribute))
                throw Error(LOC(p), "patterns may only match literals and attribute lookups");
            VisitExpr(p.Value);
            ADDOP_COMPARE(LOC(p), GeneratedEq.Instance);
            JumpToFailPop(LOC(p), pc, ByteCodeOp.POP_JUMP_IF_FALSE);
        }

        private void PatternSingleton(GeneratedMatchSingleton p, PatternContext pc)
        {
            ADDOP_LOAD_CONST(LOC(p), ConstantValue(p.Value));
            ADDOP_COMPARE(LOC(p), GeneratedIs.Instance);
            JumpToFailPop(LOC(p), pc, ByteCodeOp.POP_JUMP_IF_FALSE);
        }

        private void Pattern(GeneratedPattern p, PatternContext pc)
        {
            switch (p)
            {
                case GeneratedMatchValue v:
                    PatternValue(v, pc);
                    break;
                case GeneratedMatchSingleton s:
                    PatternSingleton(s, pc);
                    break;
                case GeneratedMatchSequence s:
                    PatternSequence(s, pc);
                    break;
                case GeneratedMatchMapping m:
                    PatternMapping(m, pc);
                    break;
                case GeneratedMatchClass c:
                    PatternClass(c, pc);
                    break;
                case GeneratedMatchStar s:
                    PatternHelperStoreName(LOC(p), s.Name?.Value, pc);
                    break;
                case GeneratedMatchAs a:
                    PatternAs(a, pc);
                    break;
                case GeneratedMatchOr o:
                    PatternOr(o, pc);
                    break;
                default:
                    throw Error(LOC(p), "invalid match pattern node in AST");
            }
        }

        // compiler_match and compiler_match_inner
        private void Match(GeneratedMatch s)
        {
            var pc = new PatternContext();
            VisitExpr(s.Subject);
            var end = NewLabel();
            int cases = Len(s.Cases);
            var m = (GeneratedMatchCase)s.Cases[cases - 1];
            int hasDefault = WildcardCheck(m.Pattern) && 1 < cases ? 1 : 0;
            for (int i = 0; i < cases - hasDefault; i++)
            {
                m = (GeneratedMatchCase)s.Cases[i];
                var at = LOC(m.Pattern);
                if (i != cases - hasDefault - 1)
                    ADDOP_I(at, ByteCodeOp.COPY, 1);
                pc.Stores = new List<string>();
                pc.AllowIrrefutable = m.Guard != null || i == cases - 1;
                pc.FailPop = new List<Label>();
                pc.OnTop = 0;
                Pattern(m.Pattern, pc);
                foreach (var name in pc.Stores)
                    Nameop(at, name, Ctx.Store);
                if (m.Guard != null)
                {
                    EnsureFailPop(pc, 0);
                    JumpIf(at, m.Guard, pc.FailPop[0], false);
                }
                if (i != cases - hasDefault - 1)
                    ADDOP(at, ByteCodeOp.POP_TOP);
                VisitStmts(m.Body);
                ADDOP_JUMP(Loc.NoLocation, ByteCodeOp.JUMP, end);
                EmitAndResetFailPop(at, pc);
            }
            if (hasDefault != 0)
            {
                m = (GeneratedMatchCase)s.Cases[cases - 1];
                var at = LOC(m.Pattern);
                ADDOP(at, cases == 1 ? ByteCodeOp.POP_TOP : ByteCodeOp.NOP);
                if (m.Guard != null)
                    JumpIf(at, m.Guard, end, false);
                VisitStmts(m.Body);
            }
            UseLabel(end);
        }
    }
}
