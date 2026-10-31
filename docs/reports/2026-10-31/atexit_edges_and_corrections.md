# atexit의 모서리 three 면 (L2.1)

날짜: 2026-10-31. 기준 `fa6f57a`, 브랜치 `work/bug-hunt`.
지시서: [`docs/plans/lifetime_foundation/local_ai_queue.md`](../../plans/lifetime_foundation/local_ai_queue.md) L2.1.
합격 프로브: `probes/l2b_atexit_edges.py` → 사본 `tests/bughunt/p89_atexit_edges.py`.

L2를 점검한 결과 세 군데가 CPython과 달랐다. 전부 `modules/AtexitModule.cs` 안에서 고쳤다.

## 1. `unregister`의 비교

`PyObject_RichCompareBool`은 **참값**을 판정하고, 두 피연산자를 다 풀고(reflected), 예외를 그대로
올린다. 내 코드는 자체 `Equals` helper로 `PyBool`만 받아서 `__eq__`가 `1`을 반환하면
`TypeError: __eq__ returned non-bool (type int)`를 던지고 프로그램이 그 자리에서 죽었다
(rc 1, 실측 `.artifacts/tmp/l2b_before.txt`).

이제 VM이 `==`에 쓰는 경로를 그대로 쓴다:

```csharp
if (ReferenceEquals(left, right)) return true;
var result = PyObject.Compare(left, right, PyObject.CompareOp.EQ);
return result != null && result.IsTrue();
```

`core/PyObject.cs:303`의 주석 그대로 `RichCompare`가 단일 타입 슬롯이고 `Compare`가 양변을 푼다.
측정 (`probes/l2b_atexit_edges.py`):

| `__eq__` | CPython | SharpPy (수정 후) |
|---|---|---|
| `1` 을 반환 | 삭제됨 (실행 안 됨) | 삭제됨 ✓ |
| `[]` 반환 | 유지 → `ran falsy` | 유지 ✓ |
| `NotImplemented` | 우변 `__eq__` 발동 → `reflected eq` | 발동 ✓ |
| 예외 발생 | `ValueError: eq raised` 전파, callback은 살아남아 나중에 실행 | 동일 ✓ |

## 2. 호출 전에 슬롯을 비우지 않는다

`atexit_callfuncs` (`Modules/atexitmodule.c:129`)는 `callbacks[i]`를 읽기만 하고, 비우는 일은 루프가
끝난 뒤 `atexit_cleanup`이 한다. 내 코드는 호출 전에 `Callbacks[i] = null;`을 해서 **자기 자신을
다시 만나는 중첩 실행**을 잃었다.

`.artifacts/tmp/l2b_nested.py`로 격리 측정:

```text
CPython            SharpPy (L2, 수정 전)      SharpPy (수정 후)
again 0            again 0                    again 0
again 1            <없음>                     again 1
older              older                      older
after inner 0      after inner 0              after inner 0
end 0              end 0                      end 0
```

슬롯 비우기는 지웠다. `i >= Callbacks.Count` 경계 검사는 남긴다 — 중첩된 라운드의 cleanup이나
`_clear()`이 목록을 줄인 채로 이 루프로 돌아올 수 있기 때문이다. CPython도 같은 자리에서 NULL을
읽고 `continue`한다 (`:131`).

## 3. 인자 검사

`atexit.` 접두사까지 측정값에 맞췄다. 키워드는 개수를 세기 전에 거부한다.

| 호출 | CPython | SharpPy (수정 전) |
|---|---|---|
| `unregister()` | `atexit.unregister() takes exactly one argument (0 given)` | 접두사 없음 |
| `unregister(print, print)` | `... (2 given)` | 접두사 없음 |
| `unregister(func=print)` | `atexit.unregister() takes no keyword arguments` | `...takes exactly one argument (0 given)` (틀린 종류) |
| `_ncallbacks(1)` | `atexit._ncallbacks() takes no arguments (1 given)` | 인자를 삼키고 `0` |
| `_clear(1)` | `atexit._clear() takes no arguments (1 given)` | 삼킴 |
| `_run_exitfuncs(1)` | `atexit._run_exitfuncs() takes no arguments (1 given)` | 삼킴 |

공통 헬퍼가 없어서(`callable()`·`globals()`도 각자 메시지를 적는다) `NoArguments(qualified, args, kwargs)`
한 개를 모듈 안에 만들고 `_clear`/`_ncallbacks`/`_run_exitfuncs`가 함께 쓰도록 했다. 메시지의 접두사는
CPython이 `__qualname__`에서 가져오는 부분이다 — SharpPy 내장 함수의 `__qualname__`은 `__name__`과
같으므로(`core/PyBuiltin.cs:1922`) 여기서만 명시적으로 붙인다.

## 기록 정정

- `core/PyFinalizer.cs`: `Shutdown()`을 끼워 넣을 때 `RunAtShutdown`의 `<summary>`가 `Shutdown()` 위로
  붙어버렸었다. 원위치.
- `modules/AtexitModule.cs`: 안 내보내는 `Traceback (most recent call last):` 줄을 주석에서 삭제.
  앵커 `:138` → `:139`, `:289` → `:288`.
- `reports/.../three_edges_of_the_guard.md`: `slot_tp_finalize`는 `:9069`가 아니라 `:9071`
  (`:9069`는 빈 줄, `:9070`은 `static void`), `PyErr_WriteUnraisable(del)`은 `:9082`가 아니라 `:9084`,
  게이트는 94/94.
- 이 보고서의 게이트 수치를 95/95로, 종료 지점 설명을 `파일`/`-c` × `정상`/`예외`로 정정.

## 새로 등록한 결함 (고치지 않음)

`docs/known_gaps.md`의 **P0-59 ~ P0-64**. 재현은 `docs/plans/lifetime_foundation/probes/gap_l2b_measure.py (L2.2에서 probes/로 올렸다)`를 양쪽
인터프리터로 돌린 것과 `docs/plans/lifetime_foundation/probes/gap_l2b_modx`에서의 `-m` 비교. 요약:

| 항목 | CPython | SharpPy |
|---|---|---|
| P0-59 예외 서브클래스 repr | `Sub('x')` | `<__main__.Sub object at 0x…>` |
| P0-60 예외 인스턴스 속성 대입 | `e.note = 1` 성공 (`tp_dictoffset`, `Objects/exceptions.c:484`) | `AttributeError` |
| P0-61 내장 예외 `__doc__` | 문자열 존재 | `None` |
| P0-62 `functools.partialmethod.__get__` | 있음 | 없음 |
| P0-63 `import pickle` | 동작 | `ModuleNotFoundError` |
| P0-64 `-m x one` | 모듈 실행, argv `[x.py, one]`, rc 0 | REPL 배너 후 rc 0 (`Program.cs:74-78` 주석) |

## 게이트

| 게이트 | 결과 |
|---|---|
| 빌드 | 오류 0 |
| `--filter l2` (l2 + l2b) | 2/2 일치 |
| `--filter p89` | 일치 |
| `tools/run_bughunt.py` | 96/96 일치 |
| `tools/evaluate.py --timeout 90` | `225 / 3 / 2` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot 4.7.2 mono 재연 | 82 matched, 0 other |
