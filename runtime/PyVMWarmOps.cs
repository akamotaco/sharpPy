using System.Runtime.CompilerServices;

namespace SharpPy
{
    /// <summary>
    /// Warm opcode handler table — op당 독립 소형 static 핸들러.
    ///
    /// 배경 (docs/jit_dispatch_strategies.md):
    /// ExecuteFrame inline 경로는 L1i 캐시/레지스터 예산이 한계라 opcode를 추가할 수 없고
    /// (POP_JUMP_IF_TRUE/LIST_APPEND 는 과거 inline 에 있다가 IL 예산 때문에 제거됨),
    /// 거대 ExecuteInstruction switch 경유는 op당 ~50-80ns.
    /// 이 테이블은 V8 Ignition 식 "핸들러 분리" 구조: 각 핸들러가 독립 메서드라
    /// JIT 가 개별 최적화하고, 핸들러 추가가 다른 opcode 성능에 영향을 주지 않는다.
    ///
    /// 안전성: 순수 safe C# (delegate 배열). unsafe/delegate* 미사용 —
    /// Godot AOT(iOS/ARM)/웹 타깃에서 현재 코드베이스와 동일한 안전성 유지.
    ///
    /// 핸들러 규약:
    /// - 입력: (vm, frame, ip, arg) — ip 는 현재 명령 인덱스
    /// - 반환: 다음 ip. NotHandled(int.MinValue) 반환 시 호출측이 ExecuteInstruction 으로 폴백
    ///   (스택을 변형하기 전에만 NotHandled 반환 가능)
    /// - 예외: PythonException throw 허용 (호출 지점이 ExecuteFrame try 블록 내부)
    /// </summary>
    internal static class PyVMWarmOps
    {
        internal const int NotHandled = int.MinValue;

        internal delegate int WarmHandler(PyVM vm, PyFrame frame, int ip, int arg);

        // ByteCodeOp 최대값(~330) 커버. 미등록 op 는 null.
        // ExecuteFrame 이 직접 조회 (메서드 호출 없이 null 체크만) — 미등록 op 의 미스 비용 ~2ns.
        internal static readonly WarmHandler[] Table = CreateTable();

        private static WarmHandler[] CreateTable()
        {
            var t = new WarmHandler[384];
            t[(int)ByteCodeOp.SWAP] = Swap;
            t[(int)ByteCodeOp.COPY] = Copy;
            t[(int)ByteCodeOp.POP_JUMP_IF_TRUE] = PopJumpIfTrue;
            t[(int)ByteCodeOp.JUMP_FORWARD] = JumpForward;
            t[(int)ByteCodeOp.LIST_APPEND] = ListAppend;
            t[(int)ByteCodeOp.KW_NAMES] = KwNames;
            t[(int)ByteCodeOp.UNPACK_SEQUENCE] = UnpackSequence;
            t[(int)ByteCodeOp.GET_ITER] = GetIter;
            t[(int)ByteCodeOp.BUILD_LIST] = BuildList;
            t[(int)ByteCodeOp.BUILD_TUPLE] = BuildTuple;
            t[(int)ByteCodeOp.COPY_FREE_VARS] = CopyFreeVars;
            t[(int)ByteCodeOp.LOAD_SUPER_ATTR] = LoadSuperAttr;
            return t;
        }

        // CPython 3.12: SWAP(n) — TOS 와 PEEK(n) 교환 (Python/bytecodes.c SWAP)
        private static int Swap(PyVM vm, PyFrame frame, int ip, int arg)
        {
            frame.ValueStack.Swap(arg);
            return ip + 1;
        }

        // CPython 3.12: COPY(n) — PEEK(n) 을 push (Python/bytecodes.c COPY)
        // arg 가 스택 범위를 벗어나는 예외 정리 edge case 는 ExecuteInstruction 폴백 (None push 처리)
        private static int Copy(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var stack = frame.ValueStack;
            if (arg <= 0 || arg > stack.Count)
                return NotHandled;
            stack.PushValue(stack.PeekValueAt(arg - 1));
            return ip + 1;
        }

        // CPython 3.12: POP_JUMP_IF_TRUE(delta) — pop 후 truthy 면 next+delta 로 점프
        // truthiness 판정은 inline POP_JUMP_IF_FALSE 와 동일 (PyValue 태그 fast path)
        private static int PopJumpIfTrue(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var v = frame.ValueStack.PopValue();
            bool truthy;
            if (v.IsBool)
                truthy = v.AsBool;
            else if (v.IsIntLike)
                truthy = v.AsInt64 != 0;
            else
                truthy = v.ToObject().PyBoolValue();
            return truthy ? ip + 1 + arg : ip + 1;
        }

        // CPython 3.12: JUMP_FORWARD(delta) — next instruction 기준 상대 점프
        private static int JumpForward(PyVM vm, PyFrame frame, int ip, int arg)
        {
            return ip + 1 + arg;
        }

        // CPython 3.12: KW_NAMES(consti) — 다음 CALL 의 keyword 이름 tuple 설정
        // (Python/bytecodes.c KW_NAMES — kwargs 호출마다 CALL 직전에 실행되는 핫 op)
        private static int KwNames(PyVM vm, PyFrame frame, int ip, int arg)
        {
            frame.KeywordNamesForNextCall = frame.Code.Constants[arg] as PyTuple;
            return ip + 1;
        }

        // CPython 3.12: UNPACK_SEQUENCE(count) — 시퀀스를 역순으로 push
        // (Python/bytecodes.c UNPACK_SEQUENCE_TUPLE 특수화 대응 — tuple 만 처리)
        // list/str/오류 메시지 경로는 ExecuteInstruction 폴백
        private static int UnpackSequence(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var stack = frame.ValueStack;
            var top = stack.PeekValue();
            if (!top.IsObject || !(top.ObjRef is PyTuple tup) || tup.Items.Length != arg)
                return NotHandled;
            stack.PopValue();
            var items = tup.Items;
            for (int i = items.Length - 1; i >= 0; i--)
                stack.Push(items[i]);
            return ip + 1;
        }

        // CPython 3.12: GET_ITER — TOS 를 iterator 로 교체 (Python/bytecodes.c GET_ITER)
        private static int GetIter(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var stack = frame.ValueStack;
            var iterator = stack.Pop().GetIterator();
            stack.Push(iterator);
            return ip + 1;
        }

        // CPython 3.12: BUILD_LIST(count) — 스택 상위 count 개로 리스트 생성
        private static int BuildList(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var stack = frame.ValueStack;
            var items = new PyObject[arg];
            for (int i = arg - 1; i >= 0; i--)
                items[i] = stack.Pop();
            stack.Push(new PyList(items));
            return ip + 1;
        }

        // CPython 3.12: BUILD_TUPLE(count) — 스택 상위 count 개로 tuple 생성
        private static int BuildTuple(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var stack = frame.ValueStack;
            var items = new PyObject[arg];
            for (int i = arg - 1; i >= 0; i--)
                items[i] = stack.Pop();
            stack.Push(new PyTuple(items));
            return ip + 1;
        }

        // CPython 3.12: LIST_APPEND(i) — TOS 를 pop 해 PEEK(i)(pop 후 기준) 리스트에 append
        // (Python/bytecodes.c LIST_APPEND — PEP 709 inlined comprehension 핫 패스)
        // 타겟이 PyList 가 아닌 희귀 케이스(PyNull skip 등)는 스택 변형 전에 폴백
        private static int ListAppend(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var stack = frame.ValueStack;
            // item(TOS) pop 전이므로 리스트는 depth arg 위치 (pop 후 arg-1 과 동일 슬롯)
            if (stack.Count <= arg)
                return NotHandled;
            var target = stack.PeekValueAt(arg);
            if (!target.IsObject || !(target.ObjRef is PyList list))
                return NotHandled;
            list.Append(stack.Pop());
            return ip + 1;
        }

        // CPython 3.12: COPY_FREE_VARS(n) — func closure 셀을 프레임 cell 로 복사
        // (Python/bytecodes.c COPY_FREE_VARS — 클로저 함수 진입마다 실행되는 핫 op)
        // InitDirectClosure 의 freevar-only 케이스는 Cells == Closure (동일 배열) → 복사 불필요
        private static int CopyFreeVars(PyVM vm, PyFrame frame, int ip, int arg)
        {
            var cells = frame.Cells;
            var closure = frame.Closure;
            if (!ReferenceEquals(cells, closure)
                && closure != null && closure.Length >= arg)
            {
                int n = arg < cells.Length ? arg : cells.Length;
                for (int i = 0; i < n; i++)
                    cells[i] = closure[i];
            }
            return ip + 1;
        }

        // CPython 3.12: LOAD_SUPER_ATTR — super() 속성 접근 (특수화: _METHOD 대응 fast path 내장)
        // ExecuteLoadSuperAttr 는 항상 null 반환 (결과는 스택에 push) → 점프 없음
        private static int LoadSuperAttr(PyVM vm, PyFrame frame, int ip, int arg)
        {
            vm.ExecuteLoadSuperAttr(frame, arg);
            return ip + 1;
        }
    }
}
