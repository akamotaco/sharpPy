# P0-49 (Q3): 멈춘 제너레이터는 죽을 때 close 된다

기준: CPython `v3.12.15` pin. 기준 커밋 `b619ee7`(Q2). 지시서 §Q3.

## 원래 실패

멈춘 제너레이터를 버리면 `finally`가 실행되지 않았다 — 종료 시점에도. CPython은
`_PyGen_Finalize`(`Objects/genobject.c:70`)가 멈춘 제너레이터의 죽음을 `close()`로 번역하므로
그 자리의 `try/finally`가 돈다. 실측: `q3_gen_finalize` 13개 항목 전부 `[]`.

## 고친 것

Q2의 가드를 그대로 쓴다. 새 경로는 두 곳뿐이다.

- `type/PyGenerator.cs` `Next()`의 **`finally` 한 곳**에서 결정한다. 모든 탈출 경로가 여기를
  지나므로 `_finished = true` 지점 6개를 각각 손대는 것보다 작고 빠뜨릴 일도 없다.
  - 끝남 → `PyFinalizer.Detach(_guard)` (더 이상 빚이 없다)
  - 멈춤 → `_guard ??= PyFinalizer.Attach(this)` (첫 정지에서만. 한 번에 도는 제너레이터는 비용 0)
- `core/PyFinalizer.cs` `RunFinalizer`에서 대상이 `PyGenerator`이면 `__del__` 대신 `Close()`를
  부르고, 보고 문맥은 제너레이터다. CPython이
  `Exception ignored in: <generator object stubborn at 0x…>`로 **제너레이터 이름**을 내는 그대로.
- 예외 저장·복원은 Q2와 같은 자리에 같은 형태로 감싼다.

## 삭제한 경로

`PyGenerator.Dispose(bool)` override. CLR 쪽에서 `Close()`를 부르던 두 번째 길이었는데,
finalizer/Dispose 경로에서 Python을 부르는 건 정책상 금지고(`docs/lifetime_and_gc_policy.md`),
가드가 유일한 관찰 지점이 됐다. 호출자 확인 후 삭제(AGENTS.md: 새 경로를 만들었으면 죽은 경로를 지운다).

## 게이트

| 게이트 | 결과 |
| --- | --- |
| `q3_gen_finalize` | 일치 → `tests/bughunt/p87_gen_finalize.py` |
| stderr 조건 | `stubborn`(RuntimeError: generator ignored GeneratorExit), `raising`(ValueError: from finally) 2건 확인 |
| bughunt | **92/92** |
| evaluate | `225 / 3 / 2` |
| similarity | exit 0 |
| Godot 4.7.2 mono 재연 | 82 matched / 0 (재코딩된 coroutine/generator 스위트 포함) |

`yield from`, `with`, 명시적 `close()` 후 재사용, 고갈 후 재사용, 미시작, GeneratorExit 무시,
finally에서 raise — 프로브가 요구하는 13개 항목이 모두 일치한다.

## 성능 (git worktree A/B, 기준 `b619ee7`, 교호 4라운드 중앙값)

| 작업 | 배수 |
| --- | --- |
| `sum(x*2 for x in range(1000000))` | **1.01** (지시서 허용 1.05 ✓) |
| genexpr 중첩 + 필터 | 0.99 |
| `try/finally` 제너레이터 200,000회 반복 | 0.99 |
| 일반 제너레이터 1,000,000회 반복 | 1.00 |
| **만들고 한 번 받고 버림 100,000** | **3.74** |

반복 소모는 비용이 없고, **정지한 채로 버려지는 생성당 가드 할당**만 비싸다. 지시서가
"성능 전용"으로 따로 붙이라고 한 필터가 정확히 이 항목을 겨냥하며, 아래 이유로 이번 커밋에는
넣지 않았다.

## 남은 격차 (P0-49 잔여)

- **가드 필터 미적용** (위 3.74배). CPython은 yield 다음에 오는 RESUME에 적힌 handler depth로
  판단한다(`Objects/genobject.c:393-406`, depth 1은 암시적 StopIteration handler뿐이라 무시).
  SharpPy의 RESUME `argument`는 resume mode(0 loop/1 function/2 generator)이고 handler depth가
  아니므로, 여기에 대응하는 것은 `PyCodeObject.ExceptionTable`에 대한 지연 계산 bool이다. 단 SharpPyrpPy가 CPython처럼
  모든 코드에 암시적 최외곽 표 항목을 넣는지 먼저 확인해야 "표가 비었는가"가 쓸 수 있는 검사인지
  정해진다(`runtime/PyBytecode.cs:480`, `ExceptionTableEntry.Depth`는 존재하며 VM은
  `runtime/PyVM.cs:1481`에서 쓴다). 확인은 계측으로 한다 — 추측으로 표를 좁히면 `finally`를
  놓치는 방향으로 깨진다.
- async 제너레이터/코루틴은 범위 밖(지시서). CPython은 `_PyGen_Finalize` 안에서 asyncgen을 별도
  분기한다(`Objects/genobject.c:81-90`). 별도 측정으로 남긴다.
- stderr 주소 형식(`at 0x…` 없음)은 Q2에 등록된 그대로다.
