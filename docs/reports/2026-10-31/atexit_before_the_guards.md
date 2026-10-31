# atexit이 가드보다 먼저다 (L2)

날짜: 2026-10-31. 기준 `c425681`, 브랜치 `work/bug-hunt`.
지시서: [`docs/plans/lifetime_foundation/local_ai_queue.md`](../../plans/lifetime_foundation/local_ai_queue.md) L2.

`import atexit`이 `ModuleNotFoundError`였다. 약한 참조 작업(C1/L3)이 이 모듈에 재퍼(reaper)를
등록하러 오는 길목이라, 수선 순서상 L1 다음·weakref 앞에 놓였다.

## CPython에서 이건 C 모듈이다

`Lib/atexit.py`는 없다. 구현은 `Modules/atexitmodule.c` 하나이고, 등록표 모양도 거기서 그대로
옮겼다 — 새 추상화를 만들지 않기 위해.

|소스 앵커 | 의미 | 실측된 관측 |
|---|---|---|
| `Modules/atexitmodule.c:177` `atexit_register` | `register(func, /, *args, **kwargs) -> func` | `register(show, "first") is show` → True |
| `:181` | 인자 0개 | `TypeError: register() takes at least 1 argument (0 given)` |
| `:188` | 호출 불가능 | `TypeError: the first argument must be callable` |
| `:270` `atexit_unregister`, `:280` `PyObject_RichCompareBool`, `:289` `Py_RETURN_NONE` | `==`로 비교, **모든** 일치 삭제, 항상 None | `unregister(removed)` → `None`, 모른 체 |
| `:53` `atexit_delete_cb` | 슬롯을 NULL로만 만든다 | `_ncallbacks()`는 고수위 → 삭제 후에도 `5` |
| `:255` `atexit_ncallbacks` | `state->ncallbacks` | 위 값이 줄지 않는 이유 |
| `:121` `atexit_callfuncs` | `for i = ncallbacks-1 .. 0` | 최신역순, 범위 진입 시 고정 → 종료 중 등록은 그 라운드 미실행 |
| `:138` `_PyErr_WriteUnraisableMsg("in atexit callback", func)` | 실패 보고 후 계속 | stderr `Exception ignored in atexit callback: <function raising at 0x…>` |
| `Python/pylifecycle.c:1845` `_PyAtExit_Call` | `finalize_modules`(:1573, 호출은 :1919) **앞** | `__del__ after atexit`가 마지막 줄 |

등록표는 C의 배열을 흉내냈다: 빈 슬롯을 지우지 않고 건너뛰는 dense list, 길이는 고수위.
`unregister` 뒤 `_ncallbacks()`가 그대로인 이유가 "버그"가 아니라 소스의 동작이라 프로브에 고정됐다.

## 유일한 종료 진입점

`core/FileExecutor.cs`에는 종료 지점이 네 개였다(정상, 파일 오류, `-c`, 예외). 하나씩 호출을
붙이다 둘 중 하나를 빠뜨릴 위험이 있어 `PyFinalizer.Shutdown()`으로 합쳤다:

```csharp
internal static void Shutdown()
{
    try { Modules.AtexitModule.RunCallbacks(); }
    catch { /* 한 단계가 죽어도 다음 단계를 멈추지 않는다 */ }
    RunAtShutdown();
}
```

atexit이 먼저인 이유는 `pylifecycle.c:1845`가 `finalize_modules`보다 앞서기 때문이다. callback은
아직 인터프리터를 쓸 수 있고, 그래서 객체의 `__del__`은 그 뒤에 온다.

## exit 경로 실측 (`os._exit`는 새 결함으로 등록)

`sys.executable`과 같은 스크립트로 대조 (`.artifacts/tmp/l2_paths.py`):

| 경로 | CPython | SharpPy (수정 후) |
|---|---|---|
| 정상 종료 | `body`, `bye`, rc 0 | `body`, `bye`, rc 0 ✓ |
| 예외 미포착 | traceback(stderr), `bye`, rc 1 | `bye` 포함, rc 1 ✓ (traceback이 stdout — 기존 등록 결함) |
| `sys.exit(3)` | `body`, `bye`, rc **3** | `body`, `bye`, rc **1** (P0-53) |
| `os._exit(0)` | `body`, rc 0, callback **안 됨** | `os._exit` 자체가 없음 → `AttributeError`, rc 1 → **P0-58** |

## 덤으로 메운 기존 결함

`ReferenceError`를 builtins에 넣으려다 같은 계열을 발견했다: `BufferError`는 이름은 builtins에
등록돼 있고 생성 표에 없어서 `BufferError('x')`가 `NotImplementedError: Built-in function
'BufferError' not implemented`를 던졌다 (`Exception`, `ValueError`는 정상 동작). 두 개를
`PyType.CreateInstance`에 함께 넣었다.

예외 생성 표가 `core/PyType.cs`(CreateInstance)·`core/PyScope.cs`(`PyBuiltinType.Call`)·
`runtime/PyVM.cs`(이름→CLR 타입) **세 곳**에 나뉘어 있다는 사실은 중복으로 남는다. 이번에는 실제
경로에만 넣었고, 표 통합은 P0-46(등록 지역성) 계열로 둔다.

`ReferenceError`의 MRO은 측정 그대로 `ReferenceError → Exception → BaseException → object`
(`Objects/exceptions.c` PyExc_ReferenceError).

## 변화량

| 파일 | 내용 |
|---|---|
| `modules/AtexitModule.cs` | 신규. C 모듈의 등록표·5개 함수·`RunCallbacks` |
| `core/PyFinalizer.cs` | `Shutdown()` 추가 (atexit → finalizer) |
| `core/FileExecutor.cs` | 종료 4곳을 `Shutdown()`으로 통일 |
| `core/PyModule.cs` | `["atexit"]` 등록 |
| `core/PyType.cs` | `ReferenceErrorType`, CreateInstance에 `ReferenceError`/`BufferError` |
| `core/PyException.cs` | `PyReferenceError`, `PyBufferError`의 `(args, kwargs)` 생성자 |
| `modules/BuiltinsModule.cs` | `ReferenceError` 노출 |

## 게이트

| 게이트 | 결과 |
|---|---|
| 빌드 | 오류 0 |
| `probes --filter l2_` | 1/1 일치 |
| `tests/bughunt/p95_atexit_reference_error.py` | 일치 |
| stderr 수동 확인 | `Exception ignored in atexit callback` 1회 |
| `tools/run_bughunt.py` | 94/94 (p95 포함 재실행: 본 커밋 메시지) |
| `tools/evaluate.py --timeout 90` | `225 / 3 / 2` |
| `tools/cpython_similarity.py` | exit 0 |
| Godot 4.7.2 mono 재연 | **82 matched, 0 other**, `REPLAY_THREADS 2 main=2` |

## 남은 것

- **P0-58** `os._exit` 부재 — callback을 태우지 않고 죽는 정상 경로는 이제 관측 대상이 됐다.
- **P0-53** `sys.exit(n)` 종료 코드 (L5).
- 예외 생성 표 3중화 → P0-46 계열.
- stderr의 repr 주소 형식·traceback 출력 stream은 기존 등록 결함 그대로.
