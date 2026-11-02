# marshal과 _thread 설계

기준: CPython `v3.12.15`, 작성 시점 커밋 `70214e9`. 코드는 아직 바꾸지 않았고, 아래 "현재 구조"는 소스에서 확인한 내용이다. Godot 플랫폼 항목은 이 저장소에서 측정한 것이 아니라 Godot 4 공개 동작 기준이므로 각 단계에서 실기기로 확인한다.

## 범위 결정 요약

| 대상 | 구현 범위 | 하지 않는 것 |
|---|---|---|
| `marshal` | 3.12 형식 그대로 값과 코드 객체 읽기·쓰기, 코드 객체 단일 생성 경로, 소스 없는 `.pyc` import | 쓰기 결과의 바이트 일치 보장, 특수화된 인라인 캐시 저장 |
| `_thread` | 실제 .NET 스레드와 GIL, 스레드별 상태, 실제 락과 `_local` | 자유 스레딩, `fork`, 서브 인터프리터, 시그널 핸들러 |
| 공통 | 단일 스레드 실행 경로의 비용을 늘리지 않음, `unsafe` 없음, 순수 .NET | Godot 노드 접근의 자동 스레드 안전화 |

## marshal

### 왜 필요한가

- Godot 배포에서 시작 시 소스 컴파일을 건너뛸 수 있다.
- 소스 없이 코드만 내보낼 수 있다.
- `importlib`, `zipimport`, `pstats`와 `test_marshal`, `test_bool`, `test_exceptions.testRaising`이 요구한다.

### 현재 구조

- `ByteCodeOp`의 숫자는 CPython opcode와 같고, `Instructions`는 `CACHE`와 `EXTENDED_ARG`까지 CPython 단어와 1:1이다. raw 비교 8039건이 이를 보여 준다.
- `PyCodeObject.Tables`가 `co_code`, `co_linetable`, `co_exceptiontable`을 들고 있어 쓰기는 값만 내보내면 된다.
- 역방향 경로가 없다. 바이트에서 `Instructions`를 만드는 코드가 없고, `code.replace`는 머리 필드 3개만 바꾸며, `types.CodeType` 생성자도 없다.
- `LocationTable.Read`는 이미 있어 위치 정보는 복원할 수 있다.

### 설계

**형식.** `Python/marshal.c`의 버전 4를 그대로 쓴다. 자체 형식을 만들지 않는다. 바이트코드가 같으므로 CPython 3.12가 만든 코드 객체도 읽을 수 있다.

**C와 C#의 차이: `FLAG_REF`.** CPython은 `Py_REFCNT(v) == 1`이면 참조 표시를 생략한다. C#에는 참조 수가 없어 같은 판단을 할 수 없다. 따라서 다음과 같이 정한다.

- 읽기: CPython 산출물을 전부 받는다.
- 쓰기: 의미가 같으면 된다. 같은 객체는 같은 객체로 복원되지만, 바이트 열은 CPython과 다를 수 있다.
- 검증: 바이트 비교가 아니라 왕복 비교와 CPython 교차 읽기로 한다. 우리 산출물을 CPython이 읽고, CPython 산출물을 우리가 읽는다.

**코드 객체 단일 경로.** 이 설계의 핵심이다. `PyCodeObject.FromTables(code, linetable, exceptiontable, consts, names, ...)` 하나를 만들고 다음 셋이 모두 이 경로를 탄다.

1. `marshal.loads`
2. `code.replace`의 모든 필드 (`co_code`, `co_linetable` 포함)
3. `types.CodeType(...)`

`marshal` 전용 복원 코드를 따로 만들지 않는다. 이 경로는 `co_code`를 단어 단위로 `Instructions`로 풀고, `co_linetable`로 위치를 채우고, `co_exceptiontable`을 `ExceptionTableEntry`로 푼다. `CACHE` 단어를 그대로 두므로 점프 인자는 변환이 필요 없다.

**잘못된 입력.** C#은 범위를 검사하므로 메모리 오류는 없지만, 예외는 CPython과 같게 맞춘다 (`EOFError`, `ValueError: bad marshal data`, `TypeError`). `co_code`의 opcode 유효성은 CPython처럼 검사하지 않는 대신, VM이 C# 예외로 멈추지 않는지 시험한다.

**`.pyc` import.** 매직 넘버 3531(3.12)과 헤더 16바이트를 맞춘다.

- 데스크톱: 소스 옆 `__pycache__`를 읽고 쓴다.
- Godot: `res://`는 읽기 전용이므로 읽기만 하고, 쓰기는 끈다. 사전 컴파일은 내보내기 단계의 도구로 만든다.
- 소스 없는 모듈의 `SourceLines`는 비어 있고, 트레이스백 줄 표시는 CPython처럼 생략된다.

### 단계

| 단계 | 내용 | 끝나는 조건 |
|---|---|---|
| M1 | 값 타입 `dumps`/`loads`/`dump`/`load`, 버전 인자 | `test_marshal` 중 코드 외 항목, CPython 교차 읽기 |
| M2 | `FromTables`, `code.replace` 전 필드, `types.CodeType` | 코퍼스 8039건이 재구성 후 raw 동일, `test_code` 항목, `test_exceptions.test_missing_lineno_shows_as_none` |
| M3 | 코드 객체 `marshal` | 코퍼스 왕복 raw 동일, 복원한 코드로 evaluate 전체 통과 |
| M4 | `.pyc` import와 내보내기 도구 | CPython이 만든 `.pyc` 실행, Godot 재생 게이트 |

M2가 M3보다 먼저인 이유는 위험이 가장 큰 부분이 바이트에서 명령어로 가는 변환이고, 이를 코퍼스 전체로 먼저 검증할 수 있기 때문이다.

## _thread

### 왜 필요한가

- 게임에서 파일 읽기나 긴 계산을 메인 스레드 밖으로 빼서 프레임 끊김을 막는다.
- `threading`, `queue`, `concurrent.futures`, `asyncio`의 executor가 전제한다.

순수 Python 계산은 GIL 때문에 병렬로 빨라지지 않는다. 이득은 대기 시간과 C# 쪽 작업에서 나온다. CPython 3.12와 같은 성질이다.

### 현재 구조

- `start_new_thread`는 항상 예외를 낸다. 락은 대기하지 않는 단일 스레드용이다.
- `PyVM.Instance`가 프로세스에 하나이고, `_frameStack`, `_inlineCall`, `_hostFrames`, `_tracing`, `TraceFunction`이 그 인스턴스 필드다.
- `PyPending.Flag`를 `RESUME`과 `JUMP_BACKWARD`에서 이미 검사한다. CPython의 eval breaker와 같은 지점이다.
- 데스크톱 인터프리터는 256MB 스택 스레드에서 돌고, 임베드 시에는 `HostRecursionLimit = 64`다.

### 방식 비교

| 방식 | 판단 |
|---|---|
| 실제 스레드 + GIL | **채택.** CPython 3.12와 의미가 같고, 스레드 전환 시점이 안전 지점으로 제한되어 기존 C# 자료구조를 그대로 쓸 수 있다. |
| 자유 스레딩 | 거부. `Dictionary`, `List`, 메서드 캐시, 인턴 표를 전부 다시 써야 하고 3.12 의미에도 없다. |
| 한 OS 스레드 위 코루틴식 전환 | 거부. Python 호출 사이에 호스트 스택(`__repr__`, 내장 함수 콜백)이 끼어 있어 C# 스택을 저장할 수 없다. `unsafe` 없이는 불가능하다. |

### 설계

**C와 C#의 차이: 스레드 상태 교체.** CPython은 스레드마다 `PyThreadState`를 두고 스레드 지역 변수로 찾는다. C#의 `[ThreadStatic]`은 매 접근 비용이 있어 VM 경로에 넣으면 단일 스레드가 느려진다. GIL을 쥔 스레드만 VM을 쓰므로 다음과 같이 한다.

- `_frameStack`, `_inlineCall`, `_hostFrames`, `_tracing`, `TraceFunction`을 `PyThreadState` 묶음으로 옮긴다.
- GIL을 얻을 때 VM 필드에 그 스레드의 묶음을 넣고, 놓을 때 빼낸다.
- VM 경로의 코드는 그대로이므로 단일 스레드 비용이 늘지 않는다.

**GIL은 필요할 때만 켠다.** 첫 `start_new_thread` 전까지는 락이 없고 기존 `PyPending.Flag` 검사만 있다. 스레드를 쓰지 않는 Godot 프로젝트는 비용이 없다.

**전환 지점.**

- 대기 중인 스레드가 5ms(`sys.getswitchinterval`) 뒤 `PyPending.Flag`를 세워 요청한다. CPython의 `gil_drop_request`와 같다.
- `PyFinalizer.Drain`을 부르는 두 지점을 `EvalBreaker` 하나로 묶어 파이널라이저와 GIL 전환을 함께 처리한다.
- 대기 호출은 `PyGil.AllowThreads(() => ...)`로 GIL을 놓는다. 대상은 `time.sleep`, 락 대기, 파일 읽기·쓰기, `input`이다. `Py_BEGIN_ALLOW_THREADS`에 해당하며, 이 안에서는 Python 객체를 건드리지 않는다.

**안전성의 근거.** 전환은 안전 지점에서만 일어나므로 C# 연산 하나는 다른 Python 스레드에 대해 원자적이다. 단, 사용자 `__eq__`나 `__hash__`가 C# 순회 도중 실행되면 그 안에서 전환될 수 있다. 재진입과 같은 문제이지만 빈도가 늘어난다. 순회 중 C# `InvalidOperationException`이 새지 않도록 `dict`, `set`, `list` 경로를 점검한다.

**`_thread` 표면.** `start_new_thread`, 실제 `lock`(시간 제한 포함), `RLock`, `_local`, `get_ident`, `get_native_id`, `_count`, `stack_size`, `interrupt_main`, `_set_sentinel`을 구현한다. `threading.py`는 CPython 원본을 쓴다. 처리되지 않은 예외는 `threading.excepthook` 경로로, `SystemExit`는 조용히 끝낸다.

**스택.** 작업 스레드는 `new Thread(run, stackBytes)`로 만든다. 데스크톱 기본값은 16MB로 하고, `stack_size()`를 따른다. `HostRecursionLimit`은 스레드별 스택 크기에 맞춰 스레드 상태에 둔다.

**`_signal`.** `unittest`를 불러오는 데 필요하므로 테스트 스텁에서 내장 모듈로 옮긴다. 상수와 `signal`/`getsignal`만 두고 실제 핸들러는 메인 스레드 기본값만 지원한다. Godot에서는 시그널을 가로채지 않는다.

### Godot 조건

| 항목 | 설계 |
|---|---|
| 지원 플랫폼 | Windows, Linux, macOS, Android, iOS는 .NET 스레드 사용. Godot 4 C#은 Web 내보내기가 없다. 스레드가 없는 대상이 생기면 `PyThreadSupport.Available = false`로 지금처럼 `RuntimeError`를 낸다. |
| 메인 스레드 멈춤 | 호스트가 Python을 호출하면 GIL을 기다린다. 작업 스레드는 5ms 안에 놓지만, 긴 C# 내장 연산(`zlib`, `re`) 중에는 안전 지점이 없어 더 걸린다. 해당 내장 연산은 `AllowThreads` 대상으로 검토한다. |
| 호스트 진입 | Godot이 Python을 부르는 진입점에 `PyGILState_Ensure`에 해당하는 `PyGil.Enter`/`Leave`를 둔다. GIL이 꺼져 있으면 아무 일도 하지 않는다. |
| 노드 접근 | SceneTree는 스레드 안전이 아니다. 자동으로 막거나 고치지 않는다. `sharppy.call_on_main(fn)` 큐를 제공하고 호스트가 프레임마다 비운다. 작업 스레드에서의 노드 접근은 사용자 책임으로 문서화한다. |
| 종료 | 씬 종료 시 호스트가 `threading._shutdown`에 해당하는 종료 함수를 부른다. 모든 작업 스레드는 `IsBackground = true`로 만들어 프로세스 종료를 막지 않는다. 데몬이 아닌 스레드를 기다리는 것은 `_shutdown`의 몫이다. |

### 단계

| 단계 | 내용 | 끝나는 조건 |
|---|---|---|
| T1 | `PyThreadState` 분리, `EvalBreaker` 통합. 스레드는 아직 만들지 않음 | 전체 게이트 통과, 단일 스레드 벤치마크 측정 전후 비교 |
| T2 | GIL, `start_new_thread`, 실제 락, `_local`, `AllowThreads` | `test_thread`, `test_threading_local`, `test_lock` 계열 |
| T3 | `threading` 원본, `queue`, `concurrent.futures`, `_signal` | `test_threading`, `test_queue`, `test_concurrent_futures`, `test_super` 스레드 항목 |
| T4 | 순회 안전성 점검, 스트레스 시험 | 경합 시나리오에서 C# 예외 유출 없음 |
| T5 | Godot 진입점, `call_on_main`, 종료 경로 | Godot 재생 게이트, Android 실기기 확인 |

## 작업 순서

1. **marshal M1~M3를 먼저 한다.** 다른 경로에 영향이 없고, `code.replace`와 `types.CodeType`의 공백도 함께 메운다.
2. **M4는 Godot 내보내기 방식이 정해진 뒤 한다.**
3. **_thread T1을 한다.** 스레드 없이도 구조만 바꾸는 단계라 회귀를 게이트로 잡을 수 있다.
4. **T2 이후를 진행한다.** T1의 성능 측정에서 단일 스레드가 느려지면 멈추고 재검토한다.

## 위험

- **`FromTables` 변환 오류.** 코퍼스 전체의 재구성 비교로 잡는다. 실패해도 M2 단계에서 멈추므로 기존 컴파일 경로에는 영향이 없다.
- **단일 스레드 성능.** 스레드 상태 교체 설계의 전제이므로 T1에서 측정한다.
- **임베드 환경 교착.** 호스트가 C# 락을 쥔 채 Python을 호출하면 GIL과 교착할 수 있다. 호스트 진입 규칙으로 문서화하고 T5에서 시험한다.
- **Android 런타임.** Godot 4의 Android C#은 실험적 지원이다. 스레드 스택 크기와 `posix` 분기를 실기기로 확인해야 하며, 이 저장소에서는 아직 측정한 적이 없다.
