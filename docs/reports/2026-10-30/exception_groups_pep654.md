# P0-33 — PEP 654 예외 그룹: 분할 로직을 하나로, VM은 CPython처럼 `split()`을 부르게

날짜: 2026-10-30 · 브랜치 `work/bug-hunt` · 기준 CPython 3.12.15 (`e848e4b0`) · 프로브 `tests/bughunt/p74_exception_groups.py` (측정 케이스 61개, diff 0)

## 착수 전 측정 (`.artifacts/tmp/p33_now2.py`)

`except*`는 처리기로 들어가는 순간 무너졌다. CPython 3.12.15과 나란히 측정해 35줄 중 18줄이 달랐다.

```text
repr(ExceptionGroup('boom',[ValueError('a')]))
  CPython ExceptionGroup('boom', [ValueError('a')])
  SharpPy ExceptionGroup('boom')              # 멤버가 사라진다
.message   CPython 'boom'                     SharpPy AttributeError
.args      CPython ('boom', [ValueError('a')]) SharpPy ('boom',)
.split()   CPython (match, rest)               SharpPy AttributeError
except*    CPython 하위 그룹 포착              SharpPy '--- C# Exception Details ---'
ExcGroup('m', ['x'])  CPython ValueError: Item 0 of second argument (exceptions) is not an exception
                      SharpPy TypeError: second argument (exceptions) must be a non-empty sequence
```

원인은 코드가 세 곳으로 갈라져 있었던 것:

| 자리 | 내용 |
|---|---|
| `runtime/PyVM.cs` `ExceptionGroupMatches` | CLR 타입 비교 기반 자체 분할, 메시지 `derive` 없음 |
| `runtime/PyVM.cs` `PrepReraiseStarExceptions` | 남은 것을 전부 모아 메시지가 `"unhandled exceptions"`인 그룹으로재굽함 |
| `core/PyType.cs` / `core/PyScope.cs` | 생성자 사본 2개, `PyException`이 아닌 멤버를 **조용히 버림** |
| `core/PyExceptionGroup.cs` | `List<PyException>` 저장(= BaseException 전용 멤버 불가), `split`/`derive` 미구현 |

CPython은 이 전부를 `Objects/exceptions.c` 한 블록에 둔다: `BaseExceptionGroup_new`(검증),
`BaseExceptionGroup_derive`, `exceptiongroup_subset`, `exceptiongroup_split_recursive`,
`split`/`subgroup`, `_PyExc_PrepReraiseStar`. 그리고 `Python/ceval.c`의 `exception_group_match`는
부분 포착 시 **`exc.split(match_type)`를 평범한 속성 호출로** 부른다 — 서브클래스가 재정의할 수
있다는 뜻이다. VM이 자체 분할을 갖고 있는 한 이 의미가 사라진다.

## 고친 것

1. **`core/PyExceptionGroup.cs`를 `Objects/exceptions.c`의 번역으로 다시 작성**
   - 저장소를 `List<PyException>` → `PyObject[]` (BaseExceptionGroup은 BaseException 전용 멤버를 합법적으로 담는다)
   - 생성자 검증 하나: arity → `BaseExceptionGroup.__new__() takes exactly 2 arguments (N given)`,
     arg1 타입, `PySequence_Check` 실패 → `must be a sequence`, 빈 시퀀스 → **ValueError**,
     비예외 멤버 → **ValueError** `Item %d of second argument (exceptions) is not an exception`
   - `BaseExceptionGroup('m', [Exception 계열만])`는 **ExceptionGroup으로 승격**,
     `ExceptionGroup('m', [BaseException 전용])`은 `TypeError: Cannot nest BaseExceptions in an ExceptionGroup`
   - `message`/`exceptions`는 read-only member → `AttributeError: readonly attribute`
   - `derive`/`split`/`subgroup` 구현 (`split expected 1 argument, got 0`,
     `subgroup expected 1 argument, got 2`, `derive`는 `function takes exactly 1 argument (0 given)`)
   - `exceptiongroup_subset`은 `derive`로 만들고 traceback/context/cause를 복사한다
   - `exceptiongroup_split_recursive` 하나가 `split`, `subgroup`, VM 매칭, reraise 프로젝션를 전부 감당
   - `args`는 호출이 받은 시퀀스 객체 그대로 저장 → CPython처럼 `('', (ValueError('plain'),))` 같이
     tuple/list 괄호까지 일치
2. **VM에서 중복 제거** — `CHECK_EG_MATCH`는 `exception_group_match` 번역:
   완전 포착(그룹은 그대로, 맨 예외는 `ExceptionGroup('', (exc,))`로 감쌈) → 부분 포착은
   **`split()` 속성 호출** → 불일치는 rest. `PyErr_SetHandledException(match)`에 대응해
   절 내부의 handling 중 예외를 matched part로 갱신. `check_except_star_type_valid`도 그대로
   (`except* 5` → `catching classes that do not inherit from BaseException is not allowed`).
   `PrepReraiseStar`는 `_PyExc_PrepReraiseStar` 번역: 메타데이터가 같은 것은 재던지기(projection로
   원래 중첩 구조 복원), 새로 raised 된 것은 형제 추가, 래퍼 메시지는 빈 문자열.
3. **생성자 사본 2개 삭제** (`core/PyType.cs`, `core/PyScope.cs`) → 전부 팩토리로.
4. **`core/FileExecutor.cs`의 `--- C# Exception Details ---` 제거** — `TEMPORARY` 표시가 붙은
   디버그 출력으로, **던져진 모든 예외**에서 stdout에 CLR 스택을 두 번째 인쇄하고 있었다
   (`.artifacts/tmp/x2.py`로 재측정). Python 레벨 예외는 이미 인터프리터가 CPython 포맷으로
   출력한 뒤라 그대로 두고, 인터프리터가 보고하지 않은 failure만 stderr로 보낸다. 종료 코드 1 유지
   (CPython과 일치 확인).

## 게이트

- 빌드 오류 0
- `tools/run_bughunt.py`: **79/79 matching** (신규 p74 61케이스 diff 0, 기존 전부 유지)
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- Godot 4.7.2 mono: 기본 suite `SHARPPY_GODOT_PASS`, replay `82 matched, 0 other` (Release DLL)

## perf: 플래그는 났지만 실측으로 기각

`tools/perf_probe.py`는 `str_fmt +18~20%`, `list_comp +72~99%`를 회귀로 표시했다. 같은 기준
커밋(`7709520`) worktree 빌드와 교대로 월클럭을 재서 판정:

| 측정 | old | new | 배율 |
|---|---|---|---|
| `[x*x for x in range(100000)]` 1회 (프로세스 내 시간) | 0.1247 | 0.1249 | 1.002x |
| str_fmt+list_comp 12회 합계 (교대 4라운드, min) | 1.550 | 1.553 | 1.002x |
| `[i*i for i in range(200000)]` 30회 (교대 5라운드, min) | 3.703 | 3.529 | 0.95x |

같은 workload에서 회귀가 없고 오히려 5% 빠른 라운드도 있다. `str_fmt`/`list_comp`는 이 코드를
건드리지 않으며(변경 지점은 `CHECK_EG_MATCH`/그룹 객체 모델뿐) 코드가 바뀌지 않은 기준 빌드에서도
같은 플래그가 나오므로 **하네스 산출물**(오래된 baseline + 한 프로세스 내 측정 순서)로 결론.
baseline 재앙커는 기존 등록대로 housekeeping으로 남긴다.

## 남은 것 (docs/known_gaps.md에 등록)

1. **처리되지 않은 그룹의 traceback 배치** — CPython은 `+ Exception Group Traceback` 헤더와
   `+-+-- 1 --+` 서브 블록을 출력한다. SharpPy는 일반 traceback 한 장 + `ExceptionGroup: g (1 sub-exception)`만.
2. **traceback 출력 스트림** — SharpPy는 stdout, CPython은 stderr. (evaluate는 stdout을 비교하므로
   지금 게이트는 이걸 보지 못한다.)
3. **`__notes__` / `add_note`** — 저장소 자체가 없어서 `is_same_exception_metadata`의 notes 항목도
   관찰 불가능. `exceptiongroup_subset`의 notes 복사도 따라가지 못한다.
4. **`KeyboardInterrupt` · `SystemExit` · `EncodingWarning`이 builtins에 없다** — 그래서
   "BaseException 전용 멤버" 규칙을 Python에서 만들 수 없다(프로브에서 해당 케이스를 고정하지 않은 이유).
   게다가 `dir(builtins)`는 그 이름들을 CPython과 같은 62개로 보고하는데 `hasattr`/이름 조회는 실패한다.
5. **`class Custom(ExceptionGroup)`** — SharpPy는 예외 그룹의 서브클래스 인스턴스를 만들지 못하고
   `ExceptionGroup`을 돌려준다 → 재정의한 `split()`이 불리지 않는다 (builtin 서브클래스 동일성 계열).
6. **`except*` 절 안 `return/break/continue`** — CPython은 `SyntaxError`, SharpPy는 통과.
