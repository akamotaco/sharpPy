# 로컬 AI 작업 큐

작성: 2026-10-31, 기준 커밋 `85bd172`, 브랜치 `work/bug-hunt`.
**담당: 로컬 AI.** Q4 핵심(C1)과 재귀(P0-52)는 이 문서의 범위가 아니다. 그쪽은
[`q4_weakref_state.md`](q4_weakref_state.md)이고 Claude가 맡는다.

이 큐에 있는 작업의 공통점은 **틀리면 CPython 비교에서 바로 드러난다**는 것이다. 합격 프로브가
통과해도 우연일 수 있는 작업(GC 타이밍, 재부활, 경합)은 여기 넣지 않았다.

## 0. 규칙

고정값, 게이트, 셸 규칙은 [`README.md`](README.md) §1을 따른다. 시작 시점 게이트 값은
bughunt 92/92, evaluate 225/3/2, similarity exit 0, Godot 82/0이다. 아래 네 가지가 추가된다.

1. **합격 프로브를 직접 쓰거나 고치지 않는다.** 이미 CPython에서 실행해 출력을 확인했다. 프로브가
   틀렸다고 판단되면 고치지 말고 멈춰서 보고한다.
2. **"확정"이라고 쓰려면 재현 파일이 있어야 한다.** 보고서에 사실을 적을 때는 재현 파일 경로와
   명령을 함께 적는다. 재현 파일이 없거나 원인을 못 찾았으면 "가설"이라고 쓴다. "다시 추측하지
   말 것" 같은 문구는 쓰지 않는다. `q4_weakref_state.md`의 이전 판에 CLR 사실로 적힌 두 항목이
   이 규칙이 없어서 틀린 채로 남았다.
3. **지시서가 측정과 어긋나면 지시서를 의심한다.** 측정을 먼저 기록하고 멈춘다. 지시서를 맞추려고
   우회 코드를 쌓지 않는다.
4. **경계 파일.** `PyGuard`의 상태 전이, `Step`, `Drain`, `RunAtShutdown`의 순서는 C1이 바꾼다.
   L1에 지정된 줄 외에는 건드리지 않는다.

소스 수정은 편집 도구로 하고, 큰 작업은 중간에 WIP 커밋이나 패치 파일로 남긴다. Q4 때 `core/PyFinalizer.cs`가
도구 실수로 0바이트가 되어 그 파일의 Q4 변경을 전부 잃었다. 정확한 원인은 기록되지 않았다.
`git diff --stat`으로 파일이 잘리지 않았는지 자주 본다.

각 항목이 끝나면 README §6의 절차대로 known_gaps, 보고서, 커밋 하나를 만든다. 커밋 전에 프로브를
지정 이름으로 `tests/bughunt/`에 복사한다.

## L1 — `__del__` 결함 세 건 (P0-50)

세 건은 커밋 하나로 묶는다. 전부 Q2가 다시 쓴 코드의 결함이다.

**합격.** 아래 두 프로브 일치, 게이트 통과. `l1_del_hook`은 stderr에 `TypeError: 'NoneType' object is
not callable`이 나오는지 `.artifacts/bughunt/`에서 직접 확인한다.

```
<python> tools/run_bughunt.py --probes C:/Users/AMD-MAX395-128G-2T/Desktop/work/sharpPy/docs/plans/lifetime_foundation/probes --filter l1_
```

사본 이름: `p92_live_slots.py`, `p93_del_hook.py`.

### 1. `Live` 슬롯이 낡는다 (`core/PyFinalizer.cs:410` `RemoveFromLive`)

**원인.** swap-remove가 옮긴 항목의 대상이 이미 죽었으면 `Target()`이 null이라 그 가드의 `Slot`을 고치지
못한다. 뒤에 그 가드가 처리될 때 낡은 번호로 다른 가드의 항목을 지운다. 지워진 가드는 종료 시 `__del__`을
못 받는다. 측정: CPython 2050줄, SharpPy 2048줄 (3/3). 이 줄 수는 점검에서 다시 재지 못했다. 현재 프로브의
CPython 출력은 2053줄이다.

**수정.** 번호를 가드가 아니라 항목에 둔다. 그러면 옮길 때 약참조를 풀 필요가 없다. 이 변경만으로 3/3
모두 2050줄이 되는 것을 scratch worktree에서 확인했다 (게이트는 돌리지 않았다).

```csharp
internal sealed class LiveEntry { internal int Index; internal WeakReference<PyGuard> Ref; }
private static readonly List<LiveEntry> Live = new();
// PyGuard: `internal int Slot = -1;` 를 `internal PyFinalizer.LiveEntry Entry;` 로 바꾼다.

// AddToLive
guard.Entry = new LiveEntry { Index = Live.Count, Ref = new WeakReference<PyGuard>(guard) };
Live.Add(guard.Entry);

// RemoveFromLive
var entry = guard.Entry;
if (entry == null) return;
int slot = entry.Index, last = Live.Count - 1;
if (slot != last) { Live[slot] = Live[last]; Live[slot].Index = slot; }
Live.RemoveAt(last);
guard.Entry = null;
```

`SnapshotLive`의 `else Live.RemoveAt(i);`는 **지운다.** 중간 삭제는 뒤 항목의 번호를 전부 어긋나게 한다.
죽은 항목은 대상 가드의 `Step`이 지운다.

### 2. `__del__`이 바인딩되지 않는다 (`core/PyFinalizer.cs:244`)

**원인.** `hook.Call(new[] { target }, null)`은 클래스 dict의 값을 그대로 부른다. CPython은
`slot_tp_finalize`(`Objects/typeobject.c:9071`)가 `lookup_maybe_method`(`:2188`)를 쓰므로 디스크립터
프로토콜을 지난다. 그래서 `staticmethod`, `classmethod`, `__get__`이 있는 객체, 호출 가능 객체가 전부 틀린다.

**수정.** 새 바인딩 코드를 만들지 않는다. `core/PyObject.cs`의 `BindSpecialMethod`를 쓴다
(`target.BindSpecialMethod(hook).Call(Array.Empty<PyObject>(), null)`). 접근 범위가 맞지 않으면 그 범위만
조정한다. 호출은 지금처럼 `try` 안에 둔다. `__get__`이 던지는 예외도 `Exception ignored`로 가야 한다.

### 3. `__del__ = None`이 부모를 부른다 (`type/PyClass.cs` `FindFinalizerHook`)

**원인.** `own is PyNone`이면 `continue`로 부모를 찾는다. CPython은 처음 찾은 값에서 멈추고 None을
호출해서 stderr에 `TypeError`를 보고할 뿐 부모를 부르지 않는다.

**수정.** `own is PyNone` 조건을 지운다. None도 훅이다. 2번의 바인딩과 호출을 지나면서 `TypeError`로 보고된다.
`_finalizerHook == null`이 "선언 없음"의 유일한 뜻이 된다.

## L2 — `ReferenceError`, `atexit`

**이유.** Q4의 전제 중 GC와 무관한 부분이다. 먼저 끝내면 C1이 이 둘을 기다리지 않는다.

**합격.** `--filter l2_` 일치, 게이트 통과. 사본: `p95_atexit_reference_error.py`.
프로브가 다루지 않는 종료 경로는 직접 확인해 보고서에 적는다: 정상 종료, 잡히지 않은 예외.
`sys.exit`는 L5 뒤에 다시 확인한다.

**변경.**

1. `builtins.ReferenceError`. MRO는 `ReferenceError, Exception, BaseException, object`.
   `modules/BuiltinsModule.cs`의 예외 등록 방식을 따른다.
2. `.artifacts/q4_wip/AtexitModule.cs`를 `modules/AtexitModule.cs`로 가져와 **아래를 고친다.** 그대로 쓰면
   프로브에 실패한다. 전부 `Modules/atexitmodule.c`에서 확인한 내용이다.

   | WIP | 맞는 동작 | 앵커 |
   |---|---|---|
   | `register`가 None 반환 | **func를 반환한다.** 데코레이터로 쓰인다 | `atexit_register` `:177` |
   | kwargs 버림 | 저장해서 호출 때 넘긴다 | 같음 |
   | `func is PyNone`만 검사 | `the first argument must be callable` | 같음 |
   | `unregister`가 참조 동일성으로 1개 제거, 없으면 `ValueError` | `==`로 비교, **일치 전부** 제거, 없어도 조용히 None | `atexit_unregister` `:270`, `:280` |
   | 종료 중 등록을 실행하려고 재시도 | 프로브 `register_more`처럼 실행되지 않는다 | `atexit_callfuncs` `:121` |

   WIP 주석의 `Modules/atexit.c`는 없는 파일이다. `Modules/atexitmodule.c`로 고치고 `:162`도 다시 확인한다.
   `Lib/weakref.py`의 `finalize`가 이 모듈을 쓴다.
3. `core/PyModule.cs`에 등록한다. 이름이 `core/StdlibModuleNames.cs`에 있는지 확인한다.
4. `core/FileExecutor.cs`의 **모든** `PyFinalizer.RunAtShutdown()` 호출(현재 4곳) 앞에서 실행한다.
   한 곳에 `Shutdown` 함수를 만들어 호출을 합치는 편이 빠뜨릴 일이 없다. CPython은
   `Py_FinalizeEx`에서 모듈 정리 전에 `_PyAtExit_Call`을 부른다 (`Python/pylifecycle.c:1845`).

## L2.1 — L2 점검 결과 수정

L1, L2는 완료됐다 (`c425681`, `fa6f57a`). 점검 기준 게이트는 bughunt 95/95, evaluate 225/3/2,
similarity exit 0, Godot 82/0이다. 아래를 **커밋 하나**로 한다. 이 항목이 **지금 할 일**이고, 끝나면 멈춘다.
다음 L3은 C1 커밋이 있어야 열린다.

**합격.** `--filter l2b_` 일치, `--filter l2_` 일치 유지, 게이트 통과. 사본: `p89_atexit_edges.py`.

**코드.** `modules/AtexitModule.cs`만 고친다.

1. `Equals`를 지우고 `unregister`가 `PyObject.Compare(left, right, PyObject.CompareOp.EQ)`의 결과를 참/거짓으로
   판정하게 한다. 비교 전에 `ReferenceEquals`를 먼저 본다. `PyObject_RichCompareBool`과 같다. VM의 `==`가
   쓰는 경로이고 반사 비교를 이미 처리한다. `__eq__`의 예외는 잡지 않고 그대로 올린다.
2. `RunCallbacks`의 `Callbacks[i] = null;`을 지운다. C 소스는 호출 전에 슬롯을 비우지 않는다
   (`atexit_callfuncs`, `Modules/atexitmodule.c:121`). `i >= Callbacks.Count` 검사는 남긴다. 중첩 실행이나
   `_clear()`가 목록을 비울 수 있다.
3. 인자 검사. `_ncallbacks`, `_clear`, `_run_exitfuncs`는 인자를 거부하고, `unregister`는 키워드를 거부한다.
   메시지는 `atexit.` 접두사까지 프로브와 같게 한다. 내장 함수의 공통 인자 검사 경로가 있으면 그것을 쓴다.

**기록 정정.** 코드 동작과 무관하다.

| 위치 | 정정 |
|---|---|
| `core/PyFinalizer.cs:314` | 첫 `<summary>`를 `RunAtShutdown` 앞으로 옮긴다 |
| `modules/AtexitModule.cs:154` | 실제로 출력하지 않는 `Traceback` 줄을 주석에서 지운다 |
| `modules/AtexitModule.cs` 주석 | `:138` → `:139`, `:289` → `:288`. `:130~131`의 슬롯 주석은 코드 2번과 함께 지운다 |
| `docs/reports/2026-10-31/three_edges_of_the_guard.md` | `:9069` → `:9071`, 게이트 `92/92` → `94/94` |
| `docs/reports/2026-10-31/atexit_before_the_guards.md` | `:138` → `:139`, `:289` → `:288`, 게이트 `94/94` → `95/95`, 종료 경로의 "파일 오류" → 파일과 `-c` 각각의 정상/예외 |

**`docs/known_gaps.md`에 등록.** 전부 L1, L2 이전부터 있던 결함이다. 재현을 CPython과 비교해 적고, 이 커밋에서
고치지 않는다.

- `class Sub(ValueError)`의 `repr`이 `Sub(...)`가 아니라 `<__main__.Sub object ...>`
- 내장 예외 인스턴스에 속성 대입 불가 (`e.note = 1`이 `AttributeError`)
- 내장 예외의 `__doc__` 없음
- `functools.partialmethod`에 `__get__` 없음
- `import pickle` 실패
- `sharppy -m x`가 모듈을 실행하지 않고 REPL로 들어감 (`Program.cs:74`의 분기가 주석 처리됨)

## L3 — `weakref.py`, 타입 플래그 (C1 뒤)

**C1이 커밋되기 전에 시작하지 않는다.** `_weakref`의 `ref`가 있어야 관측할 수 있다.

1. `.artifacts/q4_wip/weakref.py`, `_weakrefset.py`를 `Lib/`로 복사한다. CPython과 바이트까지 같은 것을
   확인했다. 수정하면 안 된다. 수정이 필요해 보이면 대개 native 쪽이 틀린 것이다.
2. `PyTypeFlags.ManagedWeakRef`를 타입 정의마다 넣는다. 표는 참조 인터프리터로 `builtins`의 모든 타입과
   `types` 모듈의 타입을 측정해 완성한다. 별도 허용 목록을 만들지 않는다. 측정 스크립트를
   `.artifacts/tmp/`에 두고 보고서에 경로를 적는다.
3. C1의 `SupportsWeakReference()`는 그때까지 일반 인스턴스와 함수만 받는 임시 답을 준다. 플래그로 교체하면
   임시 분기를 **지운다.**

**합격.** `--filter q4_weakref` 일치, 게이트 통과. 사본: `p88_weakref.py`. 프로브의 `ref died first`를
완화하면 안 된다. `q4_weakref_state.md` 이전 판의 제안은 철회됐다.

## L4 — Q6: `copy`, `tempfile`, `shutil`

Q5(`proxy`)와 무관하게 L3 뒤에 시작할 수 있다. 절차와 합격은 README §Q6이다. 번호는 `p96`부터 쓴다.

## L5 — 기존 표면 결함

수명 작업과 무관하게 기준 커밋(`d95c129`)부터 있던 결함이다. 재현: `probes/gap_outside_lifetime.py`.
L1~L4 뒤에 **항목당 커밋 하나**로 한다. 각 항목은 착수 전에 최소 프로브를 CPython 출력으로 고정한다.
이 항목에 한해 프로브를 로컬 AI가 쓰고, 다음 점검에서 경계 사례를 따로 확인한다.

| 항목 | 현상 | 주의 |
|---|---|---|
| P0-53 | `sys.exit(n)`의 종료 코드가 항상 1 (`sys.exit(0)` 포함) | 추측하지 말고 `SystemExit`이 `core/FileExecutor.cs`까지 오는 경로부터 추적한다. 처리 없는 `None`, 문자열 인자도 CPython과 비교한다. 끝나면 L2의 `sys.exit` 경로를 확인한다 |
| P0-54 | `o.m == o.m`이 False | `Objects/classobject.c`의 `method_richcompare`: `__self__`는 `is`, `__func__`는 `==`. 해시도 일관되게. 끝나면 `atexit.unregister(o.m)`이 지워지는지 확인한다 |
| P0-55 | `x.__class__ = T`가 조용히 무시됨 | `type(x)`, 메서드, `__del__`이 모두 바뀌어야 한다. 레이아웃이 다른 경우의 `TypeError`도 맞춘다 (`Objects/typeobject.c` `object_set_class`) |
| P0-56 | `is` 불일치: 작은 정수, 빈/한 글자 str, 빈 bytes, `tuple(t) is t`, `str(s) is s` | 경로별로 나누고 성능을 함께 잰다. 작은 정수 캐시는 할당 경로 전체를 건드리므로 1.02배 기준을 지킨다 |

## 멈춰야 할 때

README §7을 따른다. 이 큐에서는 다음도 바로 멈추고 보고한다.

- L1이 `Step`, `Drain`, 상태 전이를 고쳐야만 통과할 것처럼 보일 때
- 위 표의 CPython 앵커가 실제 소스와 다를 때
- 같은 현상이 실행마다 다르게 나올 때. 수명 작업에서 비결정성은 설계 문제다. 재시도로 넘기지 않는다
