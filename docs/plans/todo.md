# SharpPy Todo — CPython 3.12.15 호환 잔무 목록

기준: 고정 CPython `v3.12.15` (커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`),
참조 인터프리터 `C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe`.
이 목록의 모든 항목은 **측정된 증상 + C 앵커 + 끝내는 조건**을 갖는다. 앵커가 없는
항목은 착수 전에 앵커부터 찾는다.

스냅샷 시각: `52766a0` (2026-10-31), 수명 항목은 `da5891f` 이후 개정. 상세 이력은 [`known_gaps.md`](../known_gaps.md),
판단 근거는 [`lifetime_and_gc_policy.md`](../lifetime_and_gc_policy.md).

## 작업 규칙 (매 항목 공통)

- 한 항목 = 하나의 커밋 = 하나의 메커니즘. 범위를 줄였으면 원래 요청은 부분 해결로 남긴다.
- 착수 전: 실패 사례를 실측으로 고정한다. 완료 후: 같은 사례를 다시 실행한다.
- 새 adapter/cache/fallback을 만들기 전에 기존 경로를 삭제할 수 있는지부터 확인한다.
- 게이트 (참용 Python이 아니라 **고정 인터프리터**로):
  - `C:/ProgramData/miniforge3/envs/sharppy-test-py312/python.exe tools/run_bughunt.py`
  - `... tools/evaluate.py --dotnet <dotnet> --timeout 90`
  - 타입/속성/VM/컴파일/IO/코덱/수명/시동 변경 시 Godot 4.7.2 mono 재연
  - `... tools/cpython_similarity.py --root .` (exit code 0 유지)
  - 명령행 형태 변경 시 `... tools/argv_matrix.py`
- 새 probe 번호: **p83부터** (현재 마지막 `p82_reduce_and_sizeof.py`). p83~p90은 아래 수명 항목이
  쓰므로 다른 작업은 **p91부터** 쓴다.

## 완료 정의 (방 세 개)

- **A방 — 결정적 결과**: 출력·타입·메시지 계열이 CPython과 같아야 하고 probe로 고정한다.
- **B방 — 도달 시점**: CLR 수명 때문에 원리적으로 다르다.-close 남발 금지, 격차로 남긴다.
  `gc.collect()` 이후의 관측은 A방이고, **호출 유실은 B방이 아니라 결함이다** (2026-10-31 개정).
- **C방 — 자원 상태**: 메커니즘은 달라도 **결과는 같게** 만든다 (P0-38이 그 선례).

---

## 0. 문서 드리프트 (먼저, 5분)

- `docs/evaluation_and_performance_plan.md` 상단이 **2026-10-13** 기준이고 단일 우선
  작업이 `namespace 중복 저장·동기화 경로 정리`로 박혀 있다. `AGENTS.md`는 "현재 상태는
  계획 문서 상단 한 곳"을 요구하므로 이 문서와 모순된다.
- 끝내는 조건: 상단 상태를 현 커밋 기준으로 갱신하고, 이 todo를 하위 목록으로 링크한다.

## 1. P0-43b — 컨테이너 표현 수렴 (list 끝, 3개 남음)

CPython은 객체 하나다(`list_new`가 레이아웃을 subclass로 할당 + `inherit_slots`
`Objects/typeobject.c:6803`). SharpPy는 `PyClassInstance`가 값 저장소를 따로 든다.

필수 사전 규칙 — **두 갈래**를 타입별로 먼저 나누기 (`type/PyList.cs`의
`RawCount`/`RawIterator`/`RawContains`이 그 형태):

| 경로 | CPython | SharpPy |
| --- | --- | --- |
| `len(x)` 등 프로토콜 | 슬롯 → 선언한 dunder가 이김 | virtual (`Length()`) |
| `dict.__len__(x)` 등 기본형 자체 descriptor | layout 직접 읽는 C 함수 | virtual 아닌 raw |

이걸 안 나누고 전환하면 `_warnings`처럼 기본형 descriptor가 사용자 오버라이드를
삼킨다 (`tests/warnings_boundary_regression.py`가 회귀 시험).

1. **P0-43b-1 dict**: `PyDictSubclass : PyDict`, `_dictStorage` 삭제.
   호출점 조사 필수 — `keys/values/items`, `pop`, `update`, pickling, `__or__`.
2. **P0-43b-2 set/frozenset**: `PySetSubclass`, `PyFrozenSetSubclass`.
   set에는 family flag가 **없다**(`PySet_Check`는 subtype 검사,
   `Include/setobject.h:36`,`:30`) — flag를 추가하면 그 자체가 diverge다.
3. **P0-43b-3 str**: `_strStorage` 정리 (`PyStrSubclass`는 이미 CLR 상속).
4. 마무리: `PyNativeStorage`는 캐스트만 남기고 `Unwrap` 축소,
   `PyClassInstance`는 CPython이 주는 일(`__dict__`를 가진 heap type)만 지킨다.
   `is PyClassInstance`로 "사용자 인스턴스"를 판정하던 지점
   (`core/PyBuiltin.cs:3776`, `core/PyType.cs:2882` 등)을 항목마다 개별 확인.

## 2. 수명 기반 (최대 공백 — 1번과 독립, **먼저 진행 가능**)

**작업 지시서: [`lifetime_foundation/README.md`](lifetime_foundation/README.md).** 이유, 수정 위치, 뼈대
코드, 합격 프로브, 함정, 중단 조건이 모두 그 문서에 있다. 설계는 2026-10-31 사용자 승인.

실측 (기준 `da5891f`): `__del__` 300,000건 중 7,887건만 호출, 제너레이터 `finally` 미실행,
`id()` 충돌, `import weakref`·`atexit`·`copy`·`tempfile` → `ModuleNotFoundError`.

| 순서 | 항목 | 내용 | probe |
|---|---|---|---|
| Q1 | **P0-47** | `id()`를 식별자로 | p83 |
| Q2 | **P0-48** | Guard + 안전점 + `gc.collect()` 앵커, `__del__` 유실 수정, 종료 순서 | p84, p85, p86 |
| Q3 | **P0-49** | 제너레이터 finalize | p87 |
| Q4 | weakref C1 | `_weakref`, `ReferenceError`, `weakref.py`, `_weakrefset.py`, `atexit` | p88 |
| Q5 | | `proxy` (별도 커밋) | p89 |
| Q6 | | `copy`, `tempfile`, `shutil` | p90~ |

순서를 바꾸지 않는다. `copy.deepcopy`는 `id()`에 의존하고, weakref callback은 안전점 없이는 돌 곳이 없다.

금지: Python 코드(`__del__`, weakref callback)를 CLR finalizer에서 **실행**하지 않는다. finalizer는
큐에 넣기만 한다. `ref.clear()`, `ref.alive`는 3.12.15에 없으므로 추가하지 않는다.

## 3. P0-44 — `class C(b)`에서 b가 타입이 아니다 (측정 4종, 미구현)

`type('C', (b,), {})`는 P0-43에서 닫혔고, **class 문장**은 계산된 metaclass 호출 경로를
타므로 여전히 클래스를 만든다. CPython은 `__build_class__`가 base를 호출하며 실패한다:

- base `5` → `TypeError: int() takes at most 2 arguments (3 given)`
- `Inst` 인스턴스 → `TypeError: Inst() takes no arguments`
- `len` → `TypeError: cannot create 'builtin_function_or_method' instances`
- `None` → `TypeError: NoneType takes no arguments`

probe: `p91_non_type_base_statement.py`.

## 4. P0-46 — 등록 위치를 CPython의 타입별 스펙으로

CPython은 타입마다 자기 스펙을 같은 .c 파일에 둔다(`set_methods`는 `Objects/setobject.c`).
`type/PySet.cs`의 own-file 등록은 아직 **0**, 중앙 55. **1번(표현 수렴)이 끝난 뒤** 한 번에
옮긴다(그렇지 않으면 두 번 옮긴다). CLR static 생성자 의존 순서를 깨면 안 되고
`tests/bughunt/p65_registration_order.py`가 그 문지기다.

## 5. P0-34 코호트 3 → 4 → 5 (str 좌표계 잔무)

- **3**: parse/compile 열 좌표 (`runtime/compile.cs`, `Parser/ast.cs`) — astral에서
  traceback 캐럿 위치·에러 컬럼이 틀림 (P0-42 remainder 3도 여기 속한다).
- **4**: regex / StringIO 좌표 (`modules/_SreModule.cs`, `core/PyStringIO.cs`).
- **5**: interop escape 정식화 — 진짜 BCL `string` 호출만 UTF-16 단위를 쓰도록 남기기.
  현재 `Value.Length` 사이트는 리포지토리 전체에 흩어져 있고(코호트 2에서 153 → 107로
  줄었다는 기록은 저장되는 표현이 UTF-16이라는 전제 하에서 집계), 남는 것은 인코딩 루프와
  UTF-16이 올바른 내부 검색이다.

## 6. P0-41 — 시동 형태 진단

- `python - one` → CPython은 stdin을 스크립트로 실행하고 `sys.argv == ['-','one']`.
  SharpPy는 REPL을 시작한다.
- `python < file` → CPython은 조용히 실행, SharpPy는 banner/`>>> `가 섞인다.
- `python -c`(operand 없음) → CPython은 usage 출력 후 exit 2, SharpPy는 REPL exit 0.
- 허용 플래그 집합이 CPython보다 좁다(`-u`, `-O`, `-B`를 거부하지 않고 건너뛴다).
- usage 문구 자체는 별도 호환 결정(프로그램 이름이 들어간다)으로 분리한다.

## 7. 표면·디스패치 잔여 (측정 고정돼 있고 코드 변경 범위 작음)

- **P0-28**: 내장 타입 표면이 "어떤 객체를 먼저 만들었는가"에 의존 → CLR static 생성자
  터치 순서 대신 등록 순서를 명시로 바꾼다 (`p65_registration_order.py` 확장).
- **P0-29**: positional 과잉 인수 메시지 계열 잔여 sweep (attribute 계열은 닫힘).
  참고로 P0-45에서 CPython 자기 안에 두 모양이 있음이 실측됐다 — direct(vectorcall)는
  정의 타입 이름, bound unpack은 수신 타입 이름. 일반 경로를 따르고 나머지는 기록.
- **P0-24**: container subclass dunder 잔여 항목 정리 (P0-40/P0-43b가 대부분 흡수).
- **P0-33**: 예외그룹 잔여.
- **P0-42**: 다중 base 레이아웃 충돌은 CPython의 `best_base` 계산이 필요
  (`class M(list, dict)`는 거부, `class E(str, int)`는 IntEnum 때문에 합법),
  직접 `s.__ior__([2])`는 CPython에서 `NotImplemented`.

## 8. 소스 처리

- **P0-35**: 소스 디코딩 — NUL 바이트와 decode 실패 경로.
- `Lib/dataclasses.py`의 `[DEBUG]` print 제거 (31, 32, 37행). CPython 카피본에 섞인
  것이라 수정 근거를 커밋 메시지에 남기고 patch로 고친다(일괄 교체 금지).

## 9. 성능 (정확도 뒤에)

- **P0-36**: case 운영이 CLR intrinsic보다 비싸다(측정된 비용). ASCII 경로는 이미
  `ToUpperInvariant`/`ToLowerInvariant` intrinsic에 위임하므로, 남는 비용이 실제인지
  A/B worktree로 재확인한 뒤 다룬다.
- perf baseline 재앵커: `str_fmt`, `list_comp` 등 노이즈가 큰 워크로드가 낡았다.
  회귀 판정은 교호 실행 + `bench_suite.py` 합 + CPython 노이즈 상한 + 해당 코드가
  실제로 그 워크로드에서 실행되는지까지 보고 한다.

## 10. 게이트 위생

- `tools/run_bughunt.py`가 `sys.executable`를 쓰므로 PATH의 3.14.7로 돌리면
  `50/80` 같은 거짓 실패가 난다. 기본값을 고정 인터프리터로 바꾸거나
  `--reference` 인자를 받게 한다.
- `tools/evaluate.py` 기본 30초에서 간헐 timeout(한 번 관측). 기본값 조정 여부를 결정한다.

## 닫지 못하면 남길 것 (B방 — close 금지)

- `gc.collect()` 이전의 `__del__`/약참조 callback 도달 시점 (호출 자체의 유실은 결함이다, P0-48).
- 순환 참조에서의 callback/`__del__` 순서 (CPython은 callback이 먼저, SharpPy는 refcount 경로 순서).
- 인스턴스 생성 **뒤에** 클래스에 대입한 `__del__`.
- `gc.get_objects()` / `gc.get_referrers()` 반환값, `sys.getrefcount` 수.
- `__sizeof__`의 수값: 빈 set/frozenset 200, 빈 dict 48, 두 항목 208은 실측으로 맞췄지만
  표가 커진 뒤 항은 추정치다 (CLR 저장소라 C 할당자 수와 일치시킬 수 없다).


> 실시 진행 상태: 수명 지시서 Q1(P0-47 `id()` + 그것이 드러난 정체 결함 2개)은 2026-10-31 완료, probe는 `tests/bughunt/p83_id_identity.py`. 다은 항목은 Q2(P0-48 Guard 안점). 그 결과는 [id는 주소가 아니라 정체](../reports/2026-10-31/id_is_identity_not_a_hash.md).
> 실시 진행 상태: Q2(P0-48 Guard + 안점 + gc.collect 앵커)까지 2026-10-31 완료. probe는 p84_del_delivery, p85_retention, p86_shutdown_order. 다은 항목은 Q3(P0-49 제너레이터 finalize). 그 결과는 [가드, 안점, 앵커](../reports/2026-10-31/guards_safety_points_and_the_anchor.md).
