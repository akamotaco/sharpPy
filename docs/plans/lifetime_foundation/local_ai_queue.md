# 로컬 AI 작업 큐

작성: 2026-10-31, 기준 커밋 `85bd172`, 브랜치 `work/bug-hunt`.
**담당: 로컬 AI.** Q4 핵심(C1)과 재귀(P0-52)는 이 문서의 범위가 아니다. 그쪽은
[`q4_weakref_state.md`](q4_weakref_state.md)이고 Claude가 맡는다.

이 큐에 있는 작업의 공통점은 **틀리면 CPython 비교에서 바로 드러난다**는 것이다. 합격 프로브가
통과해도 우연일 수 있는 작업(GC 타이밍, 재부활, 경합)은 여기 넣지 않았다.

## 0. 규칙

고정값, 게이트, 셸 규칙은 [`README.md`](README.md) §1을 따른다. 시작 시점 게이트 값은
bughunt 92/92, evaluate 225/3/2, similarity exit 0, Godot 82/0이다. 아래 다섯 가지가 추가된다.

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
5. **항상 단일 경로를 점검한다** (사용자 확정, `AGENTS.md`의 단일 경로 점검). 코드를 고치기 전에 CPython의 결정 지점과
   저장 위치, SharpPy에서 같은 동작이 지나는 경로 전부를 표로 비교한다. 프로브를 통과시키는 분기나 특수 처리를
   더하지 않는다. 경로가 여럿이면 하나로 교체하고 나머지를 지운다. 지금 그렇게 못 하면 남은 경로, 호출자, 제거 조건을
   적고 멈춰서 보고한다. 이 표가 없는 보고서는 미완료로 본다. 프로브와 게이트를 통과해도 마찬가지다.

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
similarity exit 0, Godot 82/0이다. **완료 (`aff002c`).** 아래는 기록으로 남긴다.
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

## L2.2 — 완료 (C1 전)

L2.1 점검 기준 게이트: bughunt 96/96, evaluate 225/3/2, similarity exit 0, Godot 82/0.
**아래 세 커밋이 끝나면 멈춘다.** C1이 같은 작업 트리를 쓰므로 함께 진행하지 않는다. L3은 C1 커밋 뒤에 열린다.

**이 단계의 경계.** `core/PyFinalizer.cs`와 `modules/AtexitModule.cs`는 고치지 않는다. 고쳐야만 될 것처럼 보이면
멈추고 보고한다.

**1. 기록 정정 (커밋 하나).**

- P0-60의 "`raise ... from`와 `__notes__`(3.11+) 계열이 이 위에 올라 있다"에서 `raise ... from`을 뺀다. 점검에서
  `raise ... from`과 `__cause__`는 속성 대입이 실패하는 지금도 CPython과 같게 동작했다.
- P0-63의 "L4에서 함께 다룬다"를 지운다. `pickle`은 L4 범위가 아니고 별도 미배정 항목이다.
- `.artifacts/tmp/l2b_gap_measure.py`, `.artifacts/tmp/modx/x.py`를 `probes/gap_l2b_measure.py`,
  `probes/gap_l2b_modx/x.py`로 옮기고 P0-59~64의 재현 명령을 고친다. `.artifacts/tmp/`는 gitignore 대상이라
  저장소만으로 재현할 수 없다. `--filter`에 잡히지 않는 합격 기준 밖의 파일이다.

**2. P0-53 (커밋 하나).** 아래 L5 표의 지시를 따른다. `SystemExit`과 `KeyboardInterrupt`가 builtins에 없다는 기록이
`docs/known_gaps.md` 1338행에 있으니 여기서부터 본다. 끝나면 L2에서 미룬 `sys.exit` 경로를 확인해 보고서에
적는다: atexit 콜백이 돌고, 종료 코드가 CPython과 같아야 한다. 콜백 안의 `sys.exit(5)`는 종료 코드를 바꾸지 않아야 한다.

**3. P0-54 (커밋 하나).** 아래 L5 표의 지시를 따른다.

**P0-55, P0-56은 C1 뒤로 둔다.** P0-55는 생성 시점에 등록되는 가드(`PyFinalizer.RegisterIfDeclared`)와 얽히고,
P0-56은 할당 경로를 바꿔서 C1의 H3 비용 측정 기준을 흔든다.

## L2.3 — 완료 (C1 전)

L2.2는 끝났다 (`1fcb91d`, `7f09ef8`, `5c33807`). 점검 기준 게이트: bughunt 98/98, similarity exit 0. 합격 프로브를
통과한 뒤에도 아래 결함이 남았다. **아래를 커밋 하나로 하고 멈춘다.** 경계는 L2.2와 같다
(`core/PyFinalizer.cs`, `modules/AtexitModule.cs` 금지).

**합격.** `--filter l23_` 5/5 일치, `--filter l2` 일치 유지, 게이트 통과. 사본:

| 프로브 | 사본 |
|---|---|
| `l23_exit_fields.py` | `p100_exit_fields.py` |
| `l23_exit_message.py` | `p101_exit_message.py` |
| `l23_exit_bad_str.py` | `p102_exit_bad_str.py` |
| `l23_exit_keyword.py` | `p103_exit_keyword.py` |
| `l23_method_type.py` | `p104_method_type.py` |

**코드.**

1. **종료 상태를 `Shutdown()` 전에 정한다** (`core/FileExecutor.cs`의 두 곳). 지금은 `Shutdown()` 뒤에
   `ExitStatusOf`를 불러서, 주석과 달리 atexit이 `args`를 바꾸면 종료 코드가 바뀐다. CPython은
   `_Py_HandleSystemExit`가 메시지를 쓴 뒤 `Py_Exit`로 정리한다.
2. **메시지는 그 시점의 `sys.stderr`로 쓴다.** `Console.Error`가 아니다. `__str__`이 던져도 프로세스가 죽으면
   안 된다. 지금은 CLR 예외로 rc 3762504530이 된다. 상태는 1, `Shutdown()`은 그대로 돌아야 한다.
3. **`SystemExit.code`는 저장 필드다.** `args`에서 계산하지 않는다 (`SystemExit_init`,
   `Objects/exceptions.c:633`). 대입과 `del`이 되고, `del` 뒤에는 None으로 읽힌다.
4. **`SystemExit` 서브클래스도 `SystemExit`이다.** 지금 `class E(SystemExit)`의 `raise E(4)`는 traceback과 rc 1이다.
   `pythonError.PyException is PySystemExit` 판정과 인스턴스 생성 경로를 함께 본다.
   `Lib`의 사용자 코드가 쓰는 경로이므로 `ExitStatusOf`만의 분기로 때우지 말고, `.code`도 서브클래스에서 동작해야 한다.
5. **`KeyboardInterrupt` 생성자가 인자를 버린다.** `new PyKeyboardInterrupt()`가 고정 문구를 넣는다
   (`core/PyType.cs`, `core/PyException.cs:772`). L2.2가 이름을 공개하면서 드러났다. `KeyboardInterrupt()`의 args는 `()`이다.
6. **`exit()`의 인자.** `exit(code=2)`와 `exit(1, 2)`가 `Quitter.__call__(self, code=None)`처럼 동작하게 한다
   (`Lib/_sitebuiltins.py:38`).
7. **`method` 타입을 호출 가능하게 한다.** `type(o.m)(func, obj)`가 지금 `NotImplementedError`다. L3의
   `WeakMethod`가 이 호출을 쓴다 (`Lib/weakref.py:73`). 메시지는 프로브를 따른다.
8. **`dict.fromkeys == dict.fromkeys`**. 내장 클래스 메서드의 비교와 해시가 P0-54에서 빠졌다. 원인은 추측하지
   말고 그 객체의 실제 타입부터 확인한다.

**기록 정정.**

- `docs/reports/2026-10-31/the_exit_code_arrives.md`, `docs/known_gaps.md` P0-53: "상태는 teardown 전에 결정된다"는
  L2.2 시점에는 거짓이었다. 그 측정은 콜백이 예외를 내는 경우만 봐서 순서를 구별하지 못했다. L2.3에서 고쳤다고
  적는다.
- `.artifacts/tmp/l22_*.py`로 인용한 재현 파일을 `probes/gap_l22_*.py`로 옮기고 인용을 고친다. gitignore 대상을 재현
  파일로 인용하지 않는다. L2.2 1번과 같은 지적이다.
- `docs/known_gaps.md`의 P0-53, P0-54 해결 날짜 `2026-11-01`을 `2026-10-31`로 고친다. 커밋 날짜와 맞춘다.
- `docs/plans/todo.md` L2.2 행의 깨진 글자("낌", "임")와 `docs/known_gaps.md`의 "튜풀"을 고친다. 커밋 전에 새로 쓴 문장을 다시 읽는다.

**`docs/known_gaps.md`에 등록.** 고치지 않는다.

- 슬롯 래퍼 비교: `h.__str__ == h.__str__`, `n.__add__ == n.__add__`가 False, `type(l.__len__)`이 `method-wrapper`가 아님.
- 잡히지 않은 `KeyboardInterrupt`: CPython rc 3221225786, 출력은 stderr. SharpPy rc 1, stdout.
- `s.upper == 'ab'.upper`: 같은 내용의 문자열이 같은 객체인지부터 재고, P0-56과 같은 원인인지 적는다. 측정 전에는 가설이다.

**번호.** `p90`, `p91`은 L2.2가 썼다. `todo.md`에서 예약된 번호였으므로, 앞으로는 쓰기 전에 `docs/plans`에서 번호를 검색한다.

## L2.4 — 완료 (C1 전)

L2.3은 끝났다 (`d37e363`). 점검 기준 게이트: bughunt 103/103, evaluate 225/3/2, similarity exit 0, Godot 82/0.
게이트와 인수 프로브를 통과한 뒤에도 아래 결함이 남았다. 일부는 L2.3이 만들었다. 예를 들어 `code` 조회가 실패하면
rc 1이어야 하는데 이제 rc 0이고, `code`를 property로 덮은 서브클래스는 생성에서 실패한다.

**커밋은 둘이다.** 1~8번과 10번을 하나로, 9번을 하나로 한다. 끝나면 멈춘다. 경계는 L2.2와 같다
(`core/PyFinalizer.cs`, `modules/AtexitModule.cs` 금지). 고쳐야만 될 것처럼 보이면 멈추고 보고한다.

**단일 경로 표부터 쓴다.** 아래 코드 항목은 증상 목록이고, 작업 단위는 이 표의 경로다. 코드를 고치기 전에 SharpPy 열을
측정해 채우고 보고서에 넣는다. 아래 CPython 열의 앵커도 소스에서 다시 확인한다.

| 동작 | CPython의 단일 지점 | SharpPy에서 확인할 경로 | 항목 |
|---|---|---|---|
| 예외 객체 | `do_raise` (`Python/ceval.c`)는 만든 객체를 그대로 올린다 | `runtime/PyVM.cs`의 `new PyException(...)` 감싸기 전부, `OriginalInstance`, `e is z`가 참인지 | 5, 7 |
| 생성 | `BaseException_new`, `BaseException_init` | `core/PyType.cs`의 이름별 `case`, 서브클래스 생성 경로, `__new__` 직접 호출 | 3, 4 |
| 저장 | 객체의 `args`, `code` 슬롯 | C# 필드, `InstanceDict`, `SetAttribute` 경유 | 2 |
| 읽기 | 멤버 디스크립터와 `BaseException_repr` | `.args` getter, `repr`, `str`, `PySystemExit.GetAttribute` | 2, 5 |
| 종료 | `_Py_HandleSystemExit` | 파일, `-c`, REPL의 각 경로 | 1, 6, 8 |
| 함수 이름 | `func_get_qualname`, `method_repr` | `PyFunction`, `PyMethod.GetAttribute`의 이름 `switch` | 9 |

`e is z`가 거짓이면 raise가 객체를 바꾸는 것이다. 그 경우 2, 5, 7번을 감싼 객체 쪽 분기로 맞추지 않는다. 감싸기를 없앨 수
있는지 먼저 본다. 범위를 넘으면 표와 호출자 수를 적고 멈춰서 보고한다. 표현 통합은 그 보고를 근거로 별도 항목으로 잡는다.

**합격.** `--filter l24_`의 `l24_*.py` 11개 일치, `l23_*.py` 5개 유지, 게이트 통과. `--filter`는 부분 문자열이라 `gap_l23_*`, `gap_l24_*`도 잡히지만 합격 기준 밖이다. 프로브는 이미 CPython 3.12.15에서 확인했고
지금 SharpPy에서는 11개 모두 다르다. 고치면 안 된다. 틀렸다고 생각되면 멈추고 보고한다. 사본:

| 프로브 | 사본 | rc | 코드 |
|---|---|---|---|
| `l24_exit_lookup.py` | `p107_exit_lookup.py` | 1 | 1 |
| `l24_exit_code_slot.py` | `p108_exit_code_slot.py` | 5 | 2, 3 |
| `l24_exception_new.py` | `p109_exception_new.py` | 0 | 4, 5 |
| `l24_write_empty.py` | `p110_write_empty.py` | 1 | 6 |
| `l24_write_newline.py` | `p111_write_newline.py` | 1 | 6 |
| `l24_write_bad_str.py` | `p112_write_bad_str.py` | 1 | 6 |
| `l24_write_huge.py` | `p113_write_huge.py` | 4294967295 | 6 |
| `l24_write_raises.py` | `p114_write_raises.py` | 1 | 6 |
| `l24_raise_from.py` | `p115_raise_from.py` | 3 | 7 |
| `l24_raise_from_msg.py` | `p116_raise_from_msg.py` | 1 | 7 |
| `l24_qualname.py` | `p117_qualname.py` | 0 | 9 |

`p105`, `p106`은 예약 번호이므로 쓰지 않는다.

**코드.**

1. **`code` 조회 실패** (`core/FileExecutor.cs`의 `ReportExitState`). 지금은 `code = null`이 `StatusOfCode`에서 None이 되어
   rc 0이다. 주석의 "writer로 넘어간다"와 다르다. CPython은 예외 객체 자체를 쓰고 1을 낸다
   (`Python/pythonrun.c:732`~`:744`).
2. **`code`와 `args`를 슬롯으로 저장한다.** 지금은 `self.SetAttribute("code", ...)`와 `InstanceDict`를 쓴다. 그래서 사용자
   `__setattr__`이 불리고, 읽기 전용 property가 생성을 막고, `__dict__`에 두 이름이 보이고, 클래스 속성 `code = 5`가 무시된다.
   `InstanceDict`의 예약 키로 풀지 않는다. `e.__dict__['code'] = 9`가 `.code`를 바꾸면 안 된다.
   **모든 `PyClassInstance`에 필드를 더하지 않는다.** C1의 H3 비용 측정 기준이 움직인다. 예외 인스턴스에만 비용이
   들게 할 방법이 없으면 멈추고 보고한다.
3. **`SystemExit.__init__(e)`는 `code`를 건드리지 않는다.** 인자가 없으면 `args`만 바뀐다
   (`SystemExit_init`, `Objects/exceptions.c:640`). `PySystemExit.CodeForArgs`의 주석도 이 규칙으로 고친다. 그 함수의
   None은 필드의 초기값일 뿐이다.
4. **`args`는 `__new__`에서 저장한다** (`BaseException_new`, `Objects/exceptions.c:56`). 지금은 `super().__init__`을 부르지
   않는 `__init__`이면 `args`가 `()`다. 키워드 인자는 `args`에 들어가지 않고 거부되지도 않는다. `SystemExit.__new__`,
   `BaseException.__new__(ValueError, 1, 2)`처럼 직접 부르는 경로도 된다. 예외 생성은 뜨거운 경로일 수 있으니 성능을 함께 잰다.
5. **`repr`은 `.args`와 같은 값을 읽는다.** `raise K()`로 잡은 예외가 `args`는 `('z',)`인데 `repr`은 `K()`다.
   `native.Args`만 보는 것이 의심되지만, 잡힌 객체의 실제 타입부터 확인한다. `BaseException.__repr__(1)`은 `__init__`과 같은
   `descriptor ... requires` `TypeError`를 낸다.
6. **`sys.stderr` 쓰기를 CPython 호출과 같게 한다.**
   - `WriteOnStderr`의 줄바꿈 분리를 지운다. 메시지 한 번, `"\n"` 한 번이다. 빈 문자열도 쓴다.
   - 쓰기가 실패해도 `"\n"`은 시도한다. 그 시도까지 실패하면 CPython은 실제 stderr로 떨어진다
     (`PySys_WriteStderr`).
   - 실패한 오류는 `Shutdown()` 전에 `PyErr_WriteUnraisable(NULL)`로 보고된다 (`Python/pylifecycle.c:3045`, 호출 순서 `:1830`,
     atexit은 `:1845`). `hook`의 `object`는 None이고 기본 출력에 `Exception ignored in` 머리글이 없다. 오류는 한 건만
     보고한다.
   - 큰 정수도 같은 경로다. 고정 문자열을 쓰지 말고 `OverflowError`를 만들어 보고한다. rc는 -1 그대로다.
   - `PyUnraisable.Report`는 `core/PyUnraisable.cs`의 `target`에 None을 넘겼을 때의 기본 출력부터 확인한다.
7. **`raise ... from`과 `with_traceback`.** `SystemExit`, `KeyboardInterrupt`, `GeneratorExit`, `BaseException`이 대상이다.
   - `raise SystemExit(3) from None`이 rc 1, `Exception: 3`이다. `from`이 없으면 맞다. `runtime/PyVM.cs:5059`, `:5249`가
     `new PyException(customInstance.ToString())`로 감싸는 것이 가설이다. `from` 경로를 추적해서 확정한다.
   - `core/PyException.cs:106`의 `(PyException)self`는 `InvalidCastException`을 낸다. `PySystemExit`,
     `PyKeyboardInterrupt`, `PyGeneratorExit`은 `PyBaseException`의 직계 파생이다. 이미 확인했다.
   - `PyBaseException`에 없는 필드가 문제라면 위치를 옮기되 범위가 커지면 멈추고 보고한다.
8. **REPL 종료.** `core/SharpPyRepl.cs:73`, `:336`을 `PySystemExit.Matches`로 바꾼다. 또 REPL은 지금 상태를 버린다.
   파일 실행과 같은 경로로 메시지와 상태를 처리한다. 하네스가 표준 입력을 받지 못하므로 아래 세 줄을 양쪽에서 재서
   보고서에 표로 적는다. CPython은 `python -i`로 잰다. 측정값: CPython rc 4, 4, 1, SharpPy rc 0, 0, 0 (점검 때 `exit(4)`도 CPython 4, SharpPy 0).
   마지막 사례는 파이프 입력에서 메시지가 나와야 하고, 세 경우 모두 `still here`가 나오면 안 된다.

   ```
   printf 'raise SystemExit(4)\nprint("still here")\n'
   printf 'class E(SystemExit): pass\n\nraise E(4)\nprint("still here")\n'
   printf 'import sys; sys.exit("m")\nprint("still here")\n'
   ```
9. **함수의 `__qualname__`** (커밋 따로). 함수, 람다, 메서드 모두 지금 `AttributeError`다. `functools.wraps`가 예외를 삼켜서
   눈에 띄지 않았다. 대입, 비문자열 거부, `del` 거부는 `Objects/funcobject.c`를 따른다. 값은 `DisplayQualName`에서 시작하되
   대입 뒤에는 `__name__`과 따로 움직인다. 이어서 `PyMethod`를 고친다.
   `repr(Method(h.m, 5))`는 `<bound method H.m of 5>`, `__name__`이 없는 호출 가능 객체는 `<bound method ? of 1>`이다.
   지금 `Method(C(), 1).__qualname__`이 `C`를 준다. `hasattr(C(), '__qualname__')` 쪽은 False이므로 `PyMethod.GetAttribute`의
   기본 분기부터 본다. 원인은 아직 확정하지 않았다.
10. **죽은 코드.** `PySystemExit.HandleStatus`는 호출처가 없다. 지운다.

**기록 정정.**

- `docs/known_gaps.md` P0-59를 "해결"에서 "L2.4에서 해결"로 고친다. L2.3 시점에는 `repr(X(1))`이 `X()`였다.
- `docs/known_gaps.md` P0-68과 `docs/reports/2026-10-31/the_exit_state_is_fixed_first.md`의 rc `130`을 `3221225786`으로
  고친다. `130`은 Git Bash가 보여준 값이다. `raise KeyboardInterrupt`와 Ctrl-C를 나눈 설명도 지운다.
  `subprocess`로 재면 `raise`도 `3221225786`이다. rc는 앞으로 `subprocess` 값으로 적는다.
- 같은 보고서의 P0-59 절과 "`?` 유지" 설명, "`PySystemExit.Matches`를 쓰는 곳"에 L2.4 정정을 덧붙인다. 이미 쓴 측정 표는
  지우지 않는다.

**`docs/known_gaps.md`에 등록.** 고치지 않는다. 번호는 `P0-70`부터 쓰고, 재현은 `probes/gap_l24_*.py`로 저장소에 둔다.

- 종료 시 `flush_std_files` 실패의 rc 120: `sys.stderr = object()`로 CPython 120, SharpPy 1
  (`Python/pylifecycle.c:1896`, `core/PyFinalizer.cs` 경계).
- `PyMethod.Call`의 `__exit__` 임시 처리: `A().__exit__(1, 2)`가 `(1, 2)`가 아니라 `(1, 2, None)`.
- `repr` 디스패치: `object.__repr__(E(1))`이 기본 표현이 아니라 `E(1)`, `@property def __repr__`가 무시됨.
- `method` 타입: 상속이 허용됨 (`type 'method' is not an acceptable base type`이어야 함), `M.__new__(M, f, 1)`이 `TypeError`.
- 예외 표면: `add_note`, `__reduce__` 없음, 내장 예외의 `__dict__` 없음, 잡힌 서브클래스 인스턴스에 `z.w = 1` 실패, `c.y = 2`가
  `ValueError` 서브클래스의 `__dict__`에 안 보임, 클래스 속성 `args`가 무시됨, 예외 그룹 서브클래스의 `str`. P0-60과
  겹치는 것은 P0-60에 잇는다.
- `e.args = (e,)`의 `repr`이 스택 오버플로로 프로세스를 끝낸다 (CPython은 `RecursionError`). P0-52에 잇는다.
- `type(exit).__name__`이 `object`다 (CPython `Quitter`). P0-66과 같은 계열인지 적는다.

## L2.5 — 지금 할 일 (L2.4 점검 결과, C1 전)

L2.4는 끝났다 (`c47c502`, `6ab5dca`). 점검 기준 게이트: bughunt 114/114, evaluate 225/3/2, similarity exit 0, Godot 82/0.
게이트와 인수 프로브를 통과했지만 L2.4가 회귀 넷을 만들었고, 점검 중 기록되지 않은 큰 결함 둘이 나왔다.
`class A: x: int = 1`이 `NameError`로 실패하고, `o.__dict__`는 처음 읽은 뒤 새 속성을 보여주지 않는다.
둘 다 일반 스크립트가 바로 부딪히는 결함이다.

**커밋은 셋이다.** 1~3번과 기록 정정을 하나로, 4번을 하나로, 5번을 하나로 한다. 끝나면 멈춘다. 경계는 L2.4와 같다.

**단일 경로 표부터 쓴다.** SharpPy 열을 측정해 채우고 CPython 앵커를 소스에서 다시 확인한 뒤 보고서에 넣는다.

| 동작 | CPython의 단일 지점 | SharpPy에서 확인할 경로 | 항목 |
|---|---|---|---|
| 클래스 속성 대입과 삭제 | `type_setattro`가 메타타입의 데이터 디스크립터를 먼저 본다 | `type/PyClass.cs:837`의 `ClassDict[name] = value`, 메타클래스 getset, `PyQualnameDescriptor` 분기 | 1 |
| 인스턴스 속성 삭제 | `PyObject_GenericSetAttr`의 NULL 대입, 대입과 같은 MRO 조회 | `type/PyClass.cs:2350` `DelAttribute`, L2.4가 `SetAttributeDefault`에 넣은 내장 MRO 조회 | 2 |
| `args` 대입과 읽기 | `BaseException_set_args`(`Objects/exceptions.c:261`)는 `PySequence_Tuple` 결과 하나를 저장한다 | `core/PyType.cs`의 setter 안 언래핑, `PyExceptionSlots`, `PyBaseException.Args`, `new PyTuple(stored)` | 3 |
| 주석 저장 | `compiler_annassign`은 모듈과 클래스에서만 저장한다. 저장 위치는 `STORE_NAME`의 이름공간이다 | `runtime/PyVM.cs:2605` `SETUP_ANNOTATIONS`, `runtime/compile.cs:5654`, `:5660`, `frame.Code.Name` 비교 | 4 |
| 인스턴스 `__dict__` | 속성 쓰기가 가는 dict 하나 | `InstanceDict`, `PyInstanceAttrDict`의 `_dict` 복사본과 `SyncFromUnderlying`, `PyFunctionAttrDict` | 5 |

**합격.** `--filter l25_` 5개 일치, `l23_*.py` 5개와 `l24_*.py` 11개 유지, 게이트 통과. 프로브는 CPython 3.12.15에서 확인했고
지금 SharpPy에서는 5개 모두 다르다. 고치면 안 된다. 틀렸다고 생각되면 멈추고 보고한다.

| 프로브 | 사본 | 코드 |
|---|---|---|
| `l25_type_setattr.py` | `p118_type_setattr.py` | 1 |
| `l25_member_delete.py` | `p119_member_delete.py` | 2 |
| `l25_args_set.py` | `p120_args_set.py` | 3 |
| `l25_annotations.py` | `p121_annotations.py` | 4 |
| `l25_instance_dict.py` | `p122_instance_dict.py` | 5 |

**코드.**

1. **클래스 대입이 메타타입의 디스크립터를 지나게 한다.** L2.4 회귀: `Q.__qualname__ = "R"`이 사라지고 `5`도 거부되지 않으며,
   `functools.update_wrapper`를 클래스에 쓰면 `__qualname__`이 옮겨가지 않는다. `PyClass.SetAttribute`가 `ClassDict`에 바로 쓰므로
   `type_set_qualname` 대응 코드에 도달하지 못한다. 이름 분기를 더하지 말고 `PyFunction`처럼 메타타입에서 setter 있는 데이터
   디스크립터를 찾아 넘긴다. `__del__`의 `FinalizerEpoch`과 캐시 무효화는 그대로 지나게 둔다. 삭제도 같은 조회를 쓴다.
   이 경로가 서면 `PyClass` 생성자의 `PyQualnameDescriptor` 타입 분기가 아직 필요한지 다시 본다.
2. **`DelAttribute`가 대입과 같은 MRO 조회를 쓴다.** L2.4 회귀: `SystemExit` 서브클래스의 `del e.code`가 `AttributeError`다.
   대입과 삭제가 조회 코드를 공유하게 하고 복사하지 않는다. `code`는 지우면 None으로 읽히고 다시 지워도 오류가 없다.
3. **`args` 저장을 하나로 한다.** L2.4 회귀: 서브클래스의 `e.args = [1, 2]`가 조용히 `()`가 되고, `e.args is e.args`가 False가 됐다.
   - 대입은 내장과 서브클래스 모두 `PySequence_Tuple` 의미다. 기존 `tuple()` 변환 경로를 쓴다.
   - 삭제는 `args may not be deleted` `TypeError`다.
   - 읽기는 저장된 튜플 객체를 그대로 준다.
   - setter 안의 `OriginalInstance` 언래핑을 지워 `PyExceptionSlots`로 합친다.
   - "A T_OBJECT member stores whatever it is given" 주석은 틀렸으니 지운다.
4. **주석 문장.** `class A: x: int`, `class A: x: int = 1`, 함수 안의 `x: int`가 `NameError`다. 함수에서는 저장도 평가도 하지 않는다
   (`z: undefined_name`이 통과해야 한다). 클래스의 `SETUP_ANNOTATIONS`가 `LOAD_NAME`과 다른 이름공간에 쓰는 것이 가설이다. 추적해서
   확정한다. `frame.Code.Name == "<module>"` 비교는 없앤다. 끝나면 `dataclass`와 `typing.NamedTuple`의 결과를 보고서에 적는다.
   합격 기준 밖이다. 다른 결함이 나오면 고치지 말고 등록한다.
5. **`__dict__`를 저장소 하나로 만든다.** `_dict` 복사본과 동기화를 지운다. 동기화 호출을 더 넣는 수정은 하지 않는다.
   `InstanceDict`는 뜨거운 경로이므로 `self.x = 1`, 인스턴스 생성, `PyClassInstance` 크기를 전후로 잰다. 같은 바이너리 대조와
   번갈아 실행을 쓴다. 단일 실행 편차가 1.7배까지 난다. 비용 없이 못 하면 표와 측정을 적고 멈춘다.

**기록 정정.**

- `docs/reports/2026-11-01/the_slots_an_exception_owns.md`: 표의 "`raise SystemExit(3) from None` exits 1"은 3이다.
  성능 표의 머리글 줄이 빠졌다. "the only route"는 3번을 끝낸 뒤의 사실로 고친다.
- 앵커를 소스에서 다시 찾아 고친다. `Objects/exceptions.c:23`, `:1051`(`key_str`이 아니다), `Python/errors.c:207`(빈 줄이다),
  `Objects/funcobject.c:533`(빈 줄이다), `Objects/typeobject.c:250`(`type_qualname`이 아니다).
  `type/PyClass.cs` 주석의 `Objects/type.c`는 없는 파일이고 `Objects/typeobject.c`가 맞다. 코드 주석과 보고서를 함께 고친다.
- `Program.cs`, `core/SharpPyRepl.cs`의 주석이 커밋되지 않는 `.artifacts/tmp/l24_repl.py`를 가리킨다. 재현은 저장소 안으로 옮기거나
  보고서의 측정표를 가리키게 한다.
- `docs/known_gaps.md` P0-70은 신원 문제로만 적혀 있다. 아래를 잇고, 뿌리가 CLR 계보 둘이라고 적는다. 더 고치지 않는다.
  - `raise X from E("y")`(사용자 예외 인스턴스)와 `raise X from TypeError`(클래스)가 `TypeError`다.
  - 올리기 전 사용자 예외에 `__traceback__`, `add_note`, `__reduce__`가 없다.
  - `sys.exc_info()[1] is z`, `__exit__`의 값, `gen.throw(z)`가 다른 객체를 준다.
  - 사용자 서브클래스에서 `OSError.errno`, `StopIteration.value`, `ImportError`/`AttributeError`/`NameError` 키워드, `SyntaxError`, `UnicodeError`, `ExceptionGroup` 필드가 없다.

**`docs/known_gaps.md`에 등록.** 고치지 않는다. 번호는 `P0-72`부터 쓰고, 재현은 `probes/gap_l25_*.py`로 저장소에 둔다.

- 내장 함수의 `__qualname__`: `print`, `[].append`, `str.join`이 `AttributeError`다.
- `tp_new_wrapper` 검사: `object.__new__(E)`, `ValueError.__new__(KeyError, 1)`, `BaseException.__new__(int)`의 메시지.
- 레이아웃 충돌: `type("X", (int, Exception), {})`가 생성된다 (CPython `multiple bases have instance lay-out conflict`).
- `p.__dict__ = {...}` 대입 뒤 `p.a`가 `AttributeError`다.

표현 통합(P0-70)은 이 항목이 아니다. C1 뒤 Claude가 설계한다. 그 전까지 예외 슬롯을 더 늘리지 않는다.

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
P0-53, P0-54는 L2.2에서 먼저 한다. P0-55, P0-56은 L1~L4 뒤에 **항목당 커밋 하나**로 한다. 각 항목은 착수 전에 최소 프로브를 CPython 출력으로 고정한다.
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
