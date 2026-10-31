# 수명 기반 작업 지시서 (로컬 LLM용)

작성: 2026-10-31, 기준 커밋 `da5891f`, 브랜치 `work/bug-hunt`.
설계는 사용자가 승인했다. 이 문서만 읽고 실행할 수 있게 썼다. 이전 대화는 필요 없다.

**개정 2026-10-31 (점검 뒤, 기준 `85bd172`).** Q1~Q3은 완료됐다. 남은 작업은 담당별 문서로 나눴다.
**다음에 할 일은 이 문서가 아니라 아래 표의 문서에서 찾는다.** 이 문서는 §1 고정값, §2~§4 배경, §6~§7 절차의
기준으로 남는다.

| 담당 | 문서 | 맡는 일 | 기준 |
|---|---|---|---|
| 로컬 AI | [`local_ai_queue.md`](local_ai_queue.md) | L1 `__del__` 결함, L2 `atexit`, L3 `weakref.py`와 타입 플래그, L4 `copy` 계열, L5 표면 결함 | 틀리면 CPython 비교에서 바로 드러나는 작업 |
| Claude | [`q4_weakref_state.md`](q4_weakref_state.md) | C1 weakref 수명 핵심, Q5 `proxy`, P0-52 재귀 스택, 각 단계 뒤의 경계 점검 | 프로브가 우연히 통과할 수 있거나 설계 판단이 필요한 작업 |

순서: L1 → L2 → C1 → L3 → L4, Q5 → L5. P0-52는 C1 뒤에 판단한다.

**공통 규칙.**

1. **합격 프로브는 구현자가 아닌 쪽이 쓴다.** 끝나면 다른 쪽이 경계 사례를 따로 점검한다. Q1~Q3의 결함은 전부
   합격 프로브를 통과한 코드에서 나왔다.
2. **"확정 사실"은 재현 파일이 있을 때만 쓴다.** 없으면 "가설"이다. 원인을 못 찾은 현상을 사실로 적지 않는다.
3. **지시서도 가설이다.** 스파이크가 시험한 구조와 시험하지 않은 구조를 구분해 적는다. 측정이 지시서와
   어긋나면 측정을 믿고 멈춘다.

## 0. 사용법

1. §1~§4를 먼저 읽는다. **왜** 이렇게 하는지 모르면 Q2에서 잘못된 쉬운 해결을 고르게 된다.
2. Q1부터 **순서대로** 진행한다. 뒤의 Q는 앞의 Q 위에 있다.
3. Q 하나가 커밋 하나다. 게이트를 모두 통과해야 다음 Q로 간다.
4. 합격 프로브를 고치거나 약하게 만들어서 통과시키지 않는다. 프로브가 틀렸다고 판단되면 §7대로
   멈춘다. 지시서에 없는 기능을 추가하지 않는다.
5. 줄 번호는 `da5891f` 기준이다. 어긋나면 함수 이름으로 찾는다.

## 1. 고정값

| 항목 | 값 |
|---|---|
| 저장소 | `C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy` |
| dotnet | `C:/Users/AMD-MAX395-128G-2T/AppData/Local/SharpPy/dotnet/dotnet.exe` |
| 빌드 | `<dotnet> build -c Release -p:SkipCodeGeneration=true` |
| 실행 | `<dotnet> bin/Release/net8.0/sharppy.dll <script.py>` |
| 참조 Python | `C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe` (PATH의 python은 3.14라서 쓰면 안 된다) |
| CPython 소스 | `C:/Users/AMD-MAX395-128G-2T/Desktop/work/cpython-v3.12.15` (`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`) |

프로브 하나 실행 (stdout과 종료 코드를 CPython과 비교한다). `--probes`는 **절대 경로**여야 한다.
상대 경로는 `tools/run_bughunt.py`에서 `ValueError`가 난다.

```
<python> tools/run_bughunt.py --probes C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy/docs/plans/lifetime_foundation/probes --filter q1_id_identity
```

`q2_gap_late_del.py`는 합격 기준이 아니므로 디렉터리 전체를 돌리지 말고 `--filter`를 쓴다.
stderr는 비교되지 않는다. `Exception ignored in:` 같은 stderr 조건은
`.artifacts/bughunt/<프로브>.sp.txt`에서 직접 확인한다.

**게이트 (Q마다 전부).** 시작 시점 값:

| 게이트 | 명령 | 기준 |
|---|---|---|
| bughunt | `<python> tools/run_bughunt.py` | 87/87 일치 |
| evaluate | `<python> tools/evaluate.py --dotnet <dotnet> --timeout 90` | `225 / 3 / 2` |
| similarity | `<python> tools/cpython_similarity.py --root .` | exit 0 |
| Godot | `docs/plans/overnight_queue.md`의 replay 절차. 수명·VM 변경이므로 필수 | `82 matched, 0 other` |

셸 규칙 (`AGENTS.md`): 한 호출에 한 동작, heredoc 금지, 임시 스크립트는 `.artifacts/tmp/`에 먼저
파일로 쓴다. 소스 계측은 git worktree에서 한다.

## 2. 왜 하는가: C와 C#의 차이

| 관점 | CPython (C) | CLR (C#) | SharpPy에 주는 결과 |
|---|---|---|---|
| 죽는 시점 | refcount 0이 되는 순간 | 다음 GC | 순간은 재현 불가. **참조 카운팅은 도입하지 않는다** |
| 죽음을 아는 쪽 | 객체가 dealloc을 직접 실행 | GC만 안다 | 객체가 스스로 알리는 신호를 만들어야 한다 |
| 소멸 코드 스레드 | 인터프리터 스레드 | finalizer 스레드 | 인터프리터 스레드로 넘겨야 한다 |
| 실행 끼어들기 | `eval_breaker` + pending call | SharpPy에 없음 | 안전점을 만들어야 한다 |
| `id()` | 주소 | 주소는 GC가 옮기고 해시는 비고유 | 별도 번호가 필요하다 |

**실측된 결함.** 기준 커밋에서 측정했다.

| 관측 | CPython | SharpPy | 원인 |
|---|---|---|---|
| 버린 `__del__` 객체 300,000개 | 300,000회 호출 | **7,887회** | `Pending`이 약참조라 CLR이 먼저 수거하면 호출이 사라진다 |
| 멈춘 제너레이터 버림 (`finally`) | 실행 | **실행 안 됨, 종료 시에도** | `_PyGen_Finalize` 대응이 없다 |
| 값이 같은 별개 str/int/float의 `id` | 다름 | **같음** | `id()`가 해시를 돌려준다 |
| 살아 있는 list 300,000개의 고유 `id` | 300,000 | **299,312** | 같음 |
| `import weakref`, `copy`, `tempfile` | 동작 | **ModuleNotFoundError** | 표준 라이브러리 29개 파일이 `weakref` 계열을 import한다 |

**순서의 이유.** `copy.deepcopy`의 memo는 `id()`를 키로 쓰므로 `id()`를 먼저 고치지 않으면 `copy.py`가
조용히 틀린 객체를 반환한다. weakref callback은 안전점과 Guard 없이는 돌 곳이 없다. 그래서
`id()` → Guard → 제너레이터 → weakref 순서다.

## 3. 확정 설계

```
~PyGuard()  →  Queue.Enqueue(this); Flag = true     finalizer 스레드, Python 실행 없음
                     ↓
안전점의 Drain()                                      인터프리터 스레드
  Armed:     __del__ 또는 gen.close() 실행
             State = Finalized, ReRegisterForFinalize
  Finalized: ref 무효화, callback 실행 (Q4)            ← 다음 GC에서도 죽어 있을 때만 도달
             State = Cleared, Owner = null
```

- `PyGuard`는 **대상을 강참조**한다. 대상이 죽으면 Guard도 도달 불가가 되고, Guard의 finalizer가
  실행될 때 Guard가 대상을 다시 살린다. 그래서 `__del__`에 `self`를 넘길 수 있다.
- Guard는 `ConditionalWeakTable<PyObject, PyGuard>`에 둔다. 객체 필드를 늘리지 않고, CLR 타입도
  바꾸지 않으므로 `PyClassInstance`, `PyListSubclass`, `PyGenerator` 모두에 같은 방식으로 붙는다.
- Guard는 `__del__`을 선언한 타입의 인스턴스, 처리기가 있는 제너레이터, weakref가 걸린 객체에만
  붙는다. **일반 객체의 비용은 0이어야 한다.**
- **재부활:** `__del__`이 객체를 어딘가에 저장하면 객체가 살아 있으므로 2단계가 오지 않는다. 다시
  버려지면 재등록해 둔 Guard가 2단계로 진입한다. `__del__`은 다시 불리지 않는다. CPython과 같다.

**스파이크로 확인된 사실** (`spike/Program.cs.txt`, 복원 방법은 §8). 이 표는 대상을 가리키는 약참조만
시험했다. 가드가 ref를 들고 있는 구조는 시험하지 않았고, 그 부분은 [`q4_weakref_state.md`](q4_weakref_state.md) §2가
측정으로 대체한다.

| 사실 | 결과 |
|---|---|
| 300,000개 전달 | 유실 0 |
| 재부활 후 다시 버림 | `__del__` 1회, callback 1회 |
| CWT 항목은 재부활 후에도 유지 | 유지됨, 상태 조회 가능 |
| `__del__` 중 대상의 long 약참조 | 살아 있음 |
| `__del__` 중 **자식**의 short 약참조 | **죽어 있음** → short 금지 |
| 2단계 직후 long 약참조 | 아직 살아 있음 → **`Dead` 플래그를 먼저 본다** |

## 4. 금지 사항과 이유

| 금지 | 이유 |
|---|---|
| finalizer 안에서 Python 호출, 또는 `ToRepr` 같은 Python 진입 가능 멤버 호출 | 별도 스레드이며 예외가 나면 프로세스가 죽는다. 할 일은 enqueue와 플래그뿐이다 |
| `PyObject`에 refcount나 필드 추가 | 정책 §7. 모든 객체의 비용이 된다 |
| 모든 객체에 Guard 부착 | 위와 같다. 측정되지 않은 일반 경로 비용을 만든다 |
| `WeakReference<T>(obj)` 기본 생성자로 대상 추적 | short다. 자식이 `__del__`에서 죽어 보인다. `trackResurrection: true`를 쓴다 |
| 가드 안에 `WeakReference<T>` 저장 | 그 객체의 finalizer가 가드와 같은 패스에 핸들을 해제한다 (측정). `GCHandle`을 쓴다 |
| `gc.collect()` 앵커 없이 `del` 직후 시점을 맞추려는 코드 | B방이다. 시점을 흉내 내지 않는다 |
| `Close`, `Next` 같은 곳에 `GC.Collect()` 추가 | 시점 문제를 비용으로 덮는 것이다. 강제 GC는 `gc.collect()`와 기존 release bus 경로뿐이다 |
| `ref.clear()`, `ref.alive` 추가 | 3.13 API다. 3.12에 없다 |
| CPython `Lib/`를 통째로 복사 | `AGENTS.md`. 필요한 파일만 복사하고 기존 파일은 부분 수정한다 |

## 5. 작업 큐

### Q1 — `id()`를 식별자로 (P0-47) — 완료 `98c9889`

**이유.** `id()`는 살아 있는 객체끼리 고유하고 `is`와 일치해야 한다. CPython은 주소를 쓰지만
(`Python/bltinmodule.c:1234`) CLR은 객체를 옮기므로 주소를 쓸 수 없다. `GetHashCode()`는 고유하지 않다.
번호는 재사용하지 않아도 된다. 언어 규칙은 "동시에 살아 있는 객체끼리 고유"만 요구한다.

**변경.**

1. `core/PyObjectId.cs`를 새로 만든다.

   ```csharp
   using System.Runtime.CompilerServices;
   using System.Threading;

   namespace SharpPy
   {
       /// <summary>
       /// id(). CPython answers with the address (Python/bltinmodule.c:1234 builtin_id, :1237
       /// PyLong_FromVoidPtr). The CLR moves objects and GetHashCode is neither unique nor unrelated
       /// to value, so a number is handed out on the first call and lives as long as the object.
       /// A number is never reused, which is stricter than the language asks for.
       /// </summary>
       internal static class PyObjectId
       {
           private sealed class Number
           {
               internal readonly long Value = Interlocked.Add(ref _next, 16);
           }

           private static long _next = 0x10000;
           private static readonly ConditionalWeakTable<PyObject, Number> Numbers = new();

           internal static long Of(PyObject value) => Numbers.GetValue(value, static _ => new Number()).Value;
       }
   }
   ```

2. `core/PyBuiltin.cs:1032` `CallId`를 `return new PyInt(PyObjectId.Of(args[0]));`로 바꾼다.

**바꾸지 않는 것.**

- 31개의 `repr` 주소 표기(`0x{GetHashCode():x}`)는 바꾸지 않는다. CPython도 `hex(id(x))`와 repr의 주소
  문자열이 다르다. Windows에서 repr은 `0x000002024ED1C880`처럼 대문자·0 채움이다.
- `object.__hash__` (`type/PyClass.cs:934`)는 바꾸지 않는다.

**합격.** `--filter q1_id_identity` 일치, 게이트 통과. 그다음 프로브를
`tests/bughunt/p83_id_identity.py`로 복사한다.

**함정.** 지금까지 해시가 값 기준이어서 숨어 있던 문제가 드러날 수 있다. 어떤 경로가 호출마다 새
`PyString`을 만들면 `id(C.__name__) == id(C.__name__)`이 False가 된다. 게이트가 그것으로 실패하면
그 프로브에서 `is`도 CPython과 다른지 측정한다. 다르다면 기존 결함이 드러난 것이므로 known_gaps에
기록하고 Q1의 범위 밖으로 둔다. `PyObjectId`를 고쳐 덮지 않는다.

### Q2 — Guard, 안전점, `gc.collect()` 앵커 (P0-48) — 완료 `b619ee7`, 결함은 L1

**이유.** §2의 유실을 없애고 `gc.collect()`를 결정적 앵커로 만든다. 이것이 없으면 weakref callback이
돌 곳이 없다. 이 작업은 **시점을 맞추는 작업이 아니라 유실을 없애는 작업**이다.

**변경 1: 플래그.** 새 파일 `core/PyPending.cs`:

```csharp
namespace SharpPy
{
    // On its own and without initializers or a static constructor: the VM reads this on every back
    // edge, and a class that needs a static-init check would put that check on the hot path.
    internal static class PyPending
    {
        internal static volatile bool Flag;
    }
}
```

**변경 2: 안전점.** `runtime/PyVM.cs`에 두 곳만 추가한다. CPython의 검사 위치와 같다
(`Python/bytecodes.c:145`).

```csharp
case ByteCodeOp.RESUME:                       // :2912. 지금은 빈 case
    if (argument < 2 && PyPending.Flag) PyFinalizer.Drain();
    break;
```

`case ByteCodeOp.JUMP_BACKWARD:` (`:3721`) 맨 앞에 `if (PyPending.Flag) PyFinalizer.Drain();`를 넣는다.
`JUMP_BACKWARD_NO_INTERRUPT`(`:3698`)에는 넣지 않는다. 이름 그대로 CPython도 검사하지 않는다.
quickened 경로가 이 `case`를 건너뛰는지 `ByteCodeOp.JUMP_BACKWARD`로 검색해 확인한다.

**변경 3: `core/PyFinalizer.cs`를 다시 쓴다.** `Pending`, `Finalized`, `Entry`를 **삭제**한다.
`Finalized`는 강참조 집합이라 남기면 최종화된 객체가 영원히 죽지 못한다.

```csharp
internal enum GuardState { Armed, Finalized, Cleared }

internal sealed class PyGuard
{
    internal PyObject Owner;                 // strong: this is how self survives until __del__
    internal GuardState State;
    internal long Sequence;                  // creation order, for shutdown
    internal int Slot = -1;                  // index in PyFinalizer.Live, -1 when not listed
    internal PyGuard(PyObject owner) { Owner = owner; }
    ~PyGuard() { PyFinalizer.FromFinalizerThread(this); }
}
```

`PyFinalizer`의 요구 동작:

| 멤버 | 동작 |
|---|---|
| `FromFinalizerThread(g)` | `if (shutdownDone) return; Queue.Enqueue(g); PyPending.Flag = true;` **이 외의 코드를 넣지 않는다** |
| `Attach(obj)` | `Guards.GetValue(obj, ...)`로 Guard를 얻는다. 이미 있으면 그대로 둔다(멱등). 새로 만들면 `Sequence`를 매기고 `Live`에 `WeakReference<PyGuard>`로 넣는다 |
| `Drain()` | 재진입 시 즉시 0 반환(`Python/ceval_gil.c:903`). `PyPending.Flag = false`를 **먼저** 하고 큐를 비운다. 처리 수를 반환한다 |
| `Step(g)` | `Armed`: `Finalized`로 바꾸고 `Live`에서 제거, `RunFinalizer`, `GC.ReRegisterForFinalize(g)`. `Finalized`: `Cleared`로 바꾸고 `Owner = null` (Q4에서 weakref 처리 추가) |
| `RunFinalizer(g)` | 대상의 **실제 타입**에서 `__del__`을 찾아 호출한다. 예외는 기존 `ReportIgnored`로 보고한다 |
| `CollectAndDrain()` | `GC.Collect(); GC.WaitForPendingFinalizers(); n = Drain();`을 `n == 0`이 될 때까지, 최대 10회 |
| `IsFinalized(obj)` | `Guards.TryGetValue` 후 `State != Armed` |

`Live`는 swap-remove로 O(1) 제거한다. `Slot`을 함께 갱신한다. 생성 순서는 `Sequence`가 보존하므로
목록 순서를 유지할 필요가 없다. 이것으로 known_gaps P0-37 잔여 5가 해결된다.

**변경 4: 등록 지점.** 현재 `type/PyClass.cs:258`에만 있어서 `object.__new__(C)`가 빠진다.

- 호출부를 `PyFinalizer.RegisterIfDeclared(created)`로 바꾼다. 클래스 인자를 없애고 **생성된 객체의
  실제 타입**으로 판단한다. `__new__`가 다른 타입을 반환하면 호출된 클래스의 `__del__`을 쓰면 안
  된다 (프로브 P).
- `core/PyObject.cs:1957`, `core/PyType.cs:1697`의 `return new PyClassInstance(cls);`도 등록한다.
- `Attach`가 멱등이므로 중복 호출은 안전하다.
- `core/PyType.cs:2349`, `:2732`, `:2994`와 `PyListSubclass` 계열은 `type/PyClass.cs:258`을 지나는지
  프로브 I와 H로 확인한다.

**변경 5: 클래스 캐시.** `type/PyClass.cs:101-112`의 `_finalizerChecked`는 한 번 답하면 고정된다.
`InvalidateTypeCache`(`core/PyType.cs:291`)는 **서브클래스로 전파되지 않으므로** 버전 태그 비교로는
안 된다. 전용 전역 epoch를 쓴다.

```csharp
internal static ulong FinalizerEpoch = 1;        // PyClass의 static
private ulong _finalizerCheckedAt;
// DeclaresFinalizer(): if (_finalizerCheckedAt != FinalizerEpoch) { 재조회; _finalizerCheckedAt = FinalizerEpoch; }
```

`PySetAttribute`(`:783` 부근)와 `PyDelAttribute`(`:799` 부근)에서 `name == "__del__"`이면
`FinalizerEpoch++`를 한다.

**변경 6: `gc.collect()`.** `modules/GcModule.cs:161-167`의 순서를 바꾼다. CPython은 start 콜백,
수집, stop 콜백 순서다 (`Modules/gcmodule.c:1425`, `:1427`).

```csharp
InvokeCallbacks("start", generation);
PyFinalizer.CollectAndDrain();
PyResourceBus.ReleaseUnreferenced(forceCollection: false);   // 방금 수집했다
InvokeCallbacks("stop", generation);
return new PyInt(0);
```

`gc.disable()`은 Drain을 막으면 안 된다. CPython에서 `disable`은 순환 수집기만 멈추고 refcount
해제는 계속된다 (프로브 K).

**변경 7: 종료.** `RunAtShutdown`을 다음 순서로 바꾼다. `running` 재진입 방지는 유지한다.

1. `Drain()`으로 이미 죽은 것을 처리한다.
2. `__main__` 모듈 dict를 **삽입 순서로** 돌며 값에 Armed Guard가 있으면 `Step`한다. 실측상 CPython
   순서는 생성 순서가 아니라 dict 순서다 (`q2_shutdown_order`). `__main__`이 없으면(임베딩) 건너뛴다.
3. `Live`의 나머지를 `Sequence` 순으로 `Step`한다.
4. 2~3에서 새 Guard가 생겼으면 반복한다 (최대 5회). `__del__`이 새 객체를 만들 수 있다.
5. `PyResourceBus.ReleaseAllAtShutdown()`, 그다음 `shutdownDone = true`.

**변경 8: 틀린 주석 수정.** 코드가 바뀌면 다음 주석도 함께 바꾼다.

| 위치 | 틀린 내용 |
|---|---|
| `core/PyFinalizer.cs` 머리말 전체 | "종료 때 호출", "재부활은 재호출", "생성 순서" |
| `core/FileExecutor.cs:73-78` | "Shutdown is also the only point..." |
| `modules/GcModule.cs:33-34`, `:150-160` | "does not run __del__ on garbage", PLANNED 주석 |
| `type/PyClass.cs:256-257` | "only moment ... is interpreter shutdown" |

**합격.**

| 프로브 | 조건 |
|---|---|
| `q2_del_delivery` | 일치 |
| `q2_retention` | 일치 (`s_control_kept`만 0, 나머지 29개 1) |
| `q2_shutdown_order` | 일치 |
| 기존 `tests/bughunt/p76_gc_and_finalizers.py`, `p77_resource_release.py` | 일치 유지 |
| 성능 | 아래 표 |

프로브를 각각 `p84_del_delivery.py`, `p85_retention.py`, `p86_shutdown_order.py`로 복사한다.

**성능 측정.** 기준 커밋의 git worktree 빌드와 교대로 5라운드 실행한다.

| 작업 | 허용 |
|---|---|
| 일반 클래스 인스턴스 300,000개 | 1.02배 이하 (일반 객체는 비용이 없어야 한다) |
| 역방향 점프만 도는 `while` 1천만 회 | 1.02배 이하 (안전점 비용) |
| `__del__` 클래스 100,000개 | 측정값 보고 (`Attach`와 CWT 비용) |
| `gc.collect()` 1,000회, 객체 100만 개 살아 있는 힙 | 측정값 보고 (앵커가 이제 진짜 GC를 한다) |

**함정.**

- **J 실패:** `__del__`이 진행 중인 예외 상태를 덮는다. `slot_tp_finalize`는 예외를 저장·복원한다
  (`Objects/typeobject.c:9077`, `:9091`). VM이 예외 상태를 저장하는 위치를 찾아 `RunFinalizer` 앞뒤에서
  같은 일을 한다. `Exception ignored` 경로만 고쳐서는 안 된다.
- **수거가 안 되는 것처럼 보임:** 대상이 `Rooted` 같은 임시 변수에 남았을 수 있다. Tier-0 JIT는
  `TryGetTarget(out var x)`의 숨은 슬롯을 메서드 끝까지 유지한다. 실측으로 재현했다. 대상을 꺼내는
  코드는 `[MethodImpl(MethodImplOptions.NoInlining)]` 도우미로 분리하고, `DOTNET_TieredCompilation=0`으로
  현상이 달라지는지 비교한다.
- **M 실패:** 콜백이 수집 뒤에 불린다면 변경 6의 순서가 틀린 것이다.
- **`__del__`이 두 번 호출:** `Armed` 검사 전에 `RunFinalizer`를 부르고 있는 것이다. 상태를 **먼저**
  바꾼다. `__del__` 안에서 Drain이 다시 돌 수 있다.
- **`Live`의 약참조:** `PyGuard`에 대한 short 약참조로 충분하다. 대상이 아니라 Guard를 추적한다.
  Guard가 큐에 들어가면 약참조는 죽고, 그 Guard는 1단계의 `Drain()`이 처리한다.

### Q3 — 제너레이터 finalize (P0-49) — 완료 `661d58c`, 필터 미적용 (3.74배, known_gaps P0-49)

**이유.** `_PyGen_Finalize`(`Objects/genobject.c:70`)는 멈춘 제너레이터가 죽을 때 `close()`한다. 이것이
없으면 `with`나 `try/finally`를 쓰는 제너레이터의 정리 코드가 실행되지 않는다. Q2의 Guard를 그대로 쓴다.

**변경.**

1. `RunFinalizer`에서 `Owner`가 `PyGenerator`이면 `__del__` 대신 `gen.Close()`를 호출한다. 예외는
   `ReportIgnored(gen, ...)`로 보고한다. 컨텍스트는 함수가 아니라 제너레이터의 repr이다. CPython
   출력은 `Exception ignored in: <generator object stubborn at 0x...>`이다.
2. `type/PyGenerator.cs`에서 첫 yield로 멈출 때 한 번 `PyFinalizer.Attach(this)`를 호출하고 결과를
   필드 `_guard`에 저장한다. `_started`가 처음 true가 되는 지점이 아니라 **처음 멈추는 지점**이다.
   한 번에 끝나는 제너레이터는 비용을 내지 않는다.
3. 제너레이터가 끝나면(`_finished = true`가 되는 모든 지점) `PyFinalizer.Detach(_guard)`를 호출한다.
   `Detach`는 `Live`에서 제거하고 `State = Cleared`로 바꾸고 `Owner = null`, `GC.SuppressFinalize`를 한다.
   Q4 이후에는 weakref가 있으면 Guard를 유지해야 한다. 그때 다시 조정한다.
4. 제너레이터 전용 필터를 넣는다. 처리기가 없는 제너레이터는 `close()`해도 실행되는 코드가 없으므로
   Attach가 필요 없다 (`Objects/genobject.c:399-404`). `PyCodeObject`에 지연 계산 bool을 두고
   `ExceptionTable`로 판단한다. 필터는 **성능 전용**이다. 먼저 필터 없이 합격시키고 나서 넣는다.
   SharpPy가 모든 제너레이터에 암시적 표 항목을 넣는지 `plain()`의 표를 먼저 확인한다.
5. `type/PyGenerator.cs:448`의 `Dispose` override를 호출자 없음이 확인되면 삭제한다. 새 경로를 추가했으면
   죽은 경로를 지운다 (`AGENTS.md`).

**합격.** `q3_gen_finalize` 일치, stderr에 `stubborn`과 `raising`의 `Exception ignored in:`이 나온다.
게이트 통과. `genexpr` 많은 작업이 1.05배 이하. `tests/bughunt/p87_gen_finalize.py`로 복사한다.

**범위 밖.** async 제너레이터(`asyncgen_hooks`)와 코루틴. 측정만 해서 known_gaps에 기록한다.

### Q4 — weakref: 담당별로 분리됨 (2026-10-31 개정)

**이 절의 이전 지시는 폐기됐다.** 4항 `List<WeakReference<PyWeakRef>>`는 동작할 수 없는 지시였다. 스파이크가
그 구조를 시험하지 않았는데 검증된 설계처럼 적었다. `git show d95c129:docs/plans/lifetime_foundation/README.md`에
이전 판이 있다.

| 조각 | 담당 | 문서 | 합격 |
|---|---|---|---|
| `ReferenceError`, `atexit` | 로컬 AI (L2) | [`local_ai_queue.md`](local_ai_queue.md) | `l2_atexit_reference_error` |
| `PyGuard` 확장, `PyWeakRef`, `_weakref` | Claude (C1) | [`q4_weakref_state.md`](q4_weakref_state.md) | `c1_weakref_core` |
| `weakref.py`, `_weakrefset.py`, 타입 플래그 | 로컬 AI (L3) | [`local_ai_queue.md`](local_ai_queue.md) | `q4_weakref` |

### Q5 — `proxy` (별도 커밋) — 담당 Claude

**이유.** `proxy`는 40여 개 슬롯을 전달하므로 format, f-string, dict key, isinstance, 연산자 디스패치를
조용히 바꿀 수 있다. 그래서 분리한다.
앵커: `Objects/weakrefobject.c`의 proxy 구현. 죽은 proxy 접근은
`ReferenceError: weakly-referenced object no longer exists` (`:396`).
착수 전에 참조 인터프리터로 슬롯별 프로브를 먼저 만들고, `p90_weakref_proxy.py`로 고정한다.

### Q6 — `copy`, `tempfile`, `shutil` — 담당 로컬 AI (L4)

**이유.** 이 작업 전체가 열어 주는 결과다. `docs/library_surface_policy.md`의 절차를 따른다.
`Lib/`에 없으므로 복사가 기본이고, 각 파일이 끌고 오는 import를 측정한 뒤 진행한다.
합격: `copy.deepcopy`가 300,000개 객체에서 틀린 항목 0, 해당 정책 문서의 사례 일치. 번호는 `p96`부터 쓴다.

## 6. Q마다 끝낼 때

1. 원래 실패 프로브를 다시 실행해 일치를 확인한다.
2. `docs/known_gaps.md`에서 해결된 항목을 정리하고 잔여를 명시한다.
3. `docs/plans/todo.md`의 해당 항목을 갱신한다.
4. `docs/reports/<날짜>/`에 짧은 보고서를 쓴다. 포함 내용: 실행 코드 추가·삭제량, 전후 측정값, 삭제한
   경로, 남은 격차. 기존 보고서 형식은 `docs/reports/2026-10-30/gc_module_and_finalizers.md`를 본다.
5. 커밋 하나. 메시지는 `fix(P0-47): ...` 형식이다. 커밋 메시지에 측정값을 적는다.

## 7. 멈춰야 할 때

같은 Q에서 정직하게 두 번 시도해도 안 되면 트리를 초록 상태로 되돌리고, 측정된 상태를
`docs/known_gaps.md`에 쓰고, 사용자에게 보고한다. 다음 경우에는 **시도하지 말고 바로** 보고한다.

- 합격 프로브를 바꾸지 않으면 통과할 수 없다고 판단될 때
- 금지 사항을 어겨야만 해결될 때
- 일반 객체 경로의 성능이 1.02배를 넘고 원인을 줄일 수 없을 때
- Godot replay가 실패했고 네이티브 크래시가 포함될 때
- Godot이 Python을 둘 이상의 스레드에서 호출하는 것으로 드러날 때. 현재 설계는 인터프리터 스레드가
  하나라고 전제한다. 호스트 호출 시 RESUME에서 Drain이 돌기 때문에 별도 tick은 필요 없을 것으로
  보지만 이 부분은 **아직 Godot에서 검증하지 않았다**

## 8. 자료

| 파일 | 내용 |
|---|---|
| `probes/q1_id_identity.py` | Q1 합격 |
| `probes/q2_del_delivery.py` | Q2 합격: 유실, 재부활, 타입 조회, 예외, 경로별 등록, callbacks |
| `probes/q2_retention.py` | Q2 합격: VM이 객체를 붙잡는지 30개 시나리오 |
| `probes/q2_shutdown_order.py` | Q2 합격: 종료 순서 |
| `probes/q2_gap_late_del.py` | **합격 기준 아님.** 기록된 격차 |
| `probes/q3_gen_finalize.py` | Q3 합격 |
| `probes/q4_weakref.py` | L3 합격: `weakref.py`까지 포함한 전체 |
| `probes/l1_live_slots.py`, `probes/l1_del_hook.py` | L1 합격 |
| `probes/l2_atexit_reference_error.py` | L2 합격 |
| `probes/c1_weakref_core.py` | C1 합격: `_weakref`만 사용 |
| `probes/gap_lifetime_recorded.py`, `probes/gap_outside_lifetime.py` | **합격 기준 아님.** 점검에서 찾은 기록된 격차 |
| `spike/Program.cs.txt`, `spike/spike.csproj.txt` | 격리 C# 스파이크: Guard 인도 |
| `spike/guard_weak_list.cs.txt` | 격리 C# 스파이크: 가드가 ref를 드는 방법. 복원 방법은 위와 같다 |
| `spike/stage2_real_vm.py`, `spike/instrument_stage2.py` | 실제 VM의 2단계 측정. `q4_weakref_state.md` §1 |

프로브는 전부 참조 인터프리터에서 실행해 출력을 확인했다. 시작 시점에 SharpPy는 q1, q2_del_delivery,
q2_retention, q2_shutdown_order, q3, q4에서 CPython과 다르며 이것이 예상 상태다.

스파이크는 저장소 빌드에 섞이지 않도록 `.txt`로 저장했다. `sharppy.csproj`가 하위 폴더의 `.cs`를 모두
컴파일하므로 **저장소 안에서 이름을 되돌리면 안 된다.** 실행하려면 `.artifacts/tmp/spike/`로 복사해
`Program.cs`, `spike.csproj`로 이름을 바꾼 뒤 `dotnet build -c Release`한다.
비용 수치는 스파이크의 상대값이다. Guard의 실제 비용은 Q2에서 측정한다.

정책 근거 전체는 [`docs/lifetime_and_gc_policy.md`](../../lifetime_and_gc_policy.md)에 있다.
