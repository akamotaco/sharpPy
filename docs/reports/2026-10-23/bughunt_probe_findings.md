# 버그 헌팅: bughunt probe 1차 결과 — 2026-10-23

브랜치 `work/bug-hunt`. 기준: CPython 3.12.15 (`C:\ProgramData\miniforge3\envs\sharppy-test-py312\python.exe`)
과의 출력 일치. 비교 대상 구현은 `41d5107` (+본 커밋의 probe) 기준.

- 실행: `python tools/run_bughunt.py` (3.12로 실행, probe당 양쪽 프로세스 separate, stdout/stderr diff)
- probe: `tests/bughunt/p01..p09`. 원시 diff는 `.artifacts/bughunt/*.cp.txt|*.sp.txt`
- 기존 하네스 230개는 전부 green 상태에서 시작했으므로, 아래는 **기존 회귀 스위트가 잡지 못한 이탈**이다.
- 모든 항목은 양쪽 interpreter에서 동일 소스로 관찰한 값이며, probe 자체 기인한 오류 3건
  (p03 무한 sentinel, p07 except* 내 return, p08 최상위 __init_subclass__)은 수정 후 재확보했다.

## A. 크래시 / CLR 예외 유출 (파일 전체 사망)

| # | 재현 | CPython 3.12 | SharpPy | 코드 위치 |
|---|---|---|---|---|
| A1 | 함수 안에서 `[lambda: i for i in range(3)]` | `[2,2,2]` | `System.Exception: Variable 'i' not found in cell or free variables` 컴파일 크래시 | `runtime/compile.cs:12267 EmitLoadClosure` |
| A2 | `type("C", (Base, Base), {})` | `TypeError: duplicate base class Base` | `System.ArgumentException`(MRO)가 **`except BaseException`을 통과**해 프로그램 사망 | `core/PyType.cs:414 CalculateC3MRO` |
| A3 | `(e := ValueError("x"), e.with_traceback(None), e.__traceback__)[2]` | `None` | `RuntimeError: STORE_FAST: index 0 out of range` | p07 |
| A4 | `{k: v for k, v in [("a",1),("a",2),("b",3)]}` | `{'a': 2, 'b': 3}` | `RuntimeError: LOAD_FAST_AND_CLEAR: index 0 out of range` | p04 |

A2는 특히 심각하다: CLR 예외가 Python 예외 계층을 뚫으므로 사용자 코드의 except가 방어 불가능하다.

## B. 결과 오류 (에러 없이 다른 값)

**숫자**
- `0 ** -1` → `inf` (기대: `ZeroDivisionError: 0.0 cannot be raised to a negative power`)
- `round(1234, -2)` → `1234`, `round(1250, -2)` → `1250`: **int의 음수 ndigits 무시**
- `round(2.675, 2)` → `2.68` (기대 `2.67`; double 정확 반올림 `_Py_dg_dtoa` 경로)
- `(1 << 63).bit_length()` → `OverflowError: int too large to convert to long`
- `str(-0.0)` → `'-0'` (기대 `'-0.0'`), `str(1e16)` ≠ `repr(1e16)` (최단 표기 문턱)
- `float.hex(0.1)` → `'0x3fb999999999999a'` (기대 `'0x1.999999999999ap-4'` — IEEE 비트 그대로 출력)

**컨테이너**
- set에 unhashable 허용: `{[]}` → `{[]}`, `{(1,[2])}` 통과 (기대 `TypeError: unhashable`)
- `{1,2} | [2,3]` → set 성공 (기대 `TypeError: unsupported operand`)
- `set.update([1], {2})` 가변 인수 미지원 ("update() takes exactly 1 argument")
- `reversed(range(3))` → "must be a sequence" (range is sequence)
- `iter(f, sentinel)` 2-인자 미지원, `zip(..., strict=True)` 무시, `map(print,'ab')`의 lazy next가 `StopIteration`
- `__getitem__` 프로토콜 클래스의 `in` → "argument of type 'S' is not iterable" (sequence fallback 없음)
- `a, *b = iter([1,2,3])` → "cannot unpack non-sequence list_iterator" (starred unpack이 iterator 불가)
- `L.__setitem__`/`__delitem__`/`__imul__` 속성이 list/tuple에 없음 (슬롯 래퍼 노출 부재)

**dict**
- unhashable key 검사 없음: `{([1], 2): ...}` 저장, `.get(([1],2))` → None, `{{1:2}: 3}` 통과
- `d |= {...}` (`__ior__`) 속성/연산 없음
- keys view가 live가 아님: `d.update(c=3)` 후 `len(view)` = 2 (기대 3)
- 반복 중 mutation 시 `RuntimeError: dictionary changed size during iteration` 없음
  (그리고 view iterator의 `list(it)`이 "'iterator' object is not iterable")

**호출/인자 바인딩**
- `take(*[1], 2)` → `(2, 1, ...)` **`*` 이후 positional 순서 뒤집힘**
- `take(1, 2, d=3, **{"d": 4})` → 무검사 통과 (기대 `got multiple values for keyword argument 'd'`)
- `take(1, 2, a=3)` → `a`가 `**kw`로 흡수 (기대 `got multiple values for argument 'a'`)
- `take(*[a,b,c], **{"d": d})` → kw `d`가 `rest`로 유입
- `(lambda *a, b: ...)(1, 2, b=3)` → "unexpected keyword argument 'b'" (kwonly 바인딩 broken)
- `(lambda *, a: a)(1)` → positional 허용 (kwonly 강제 없음)

**bool/클래스**
- `bool(obj)`가 `__bool__` 무시 (False 반환 obj → True), `__len__()==0`도 True
- `__slots__`: `hasattr(Slotted(), "__dict__")` → True (slots 미준수)
- `__init_subclass__` 미호출 (flavor/kwargs 처리 전부 누락)
- 다중 상속 예외 `class MyErr(RuntimeError, ValueError)` → `except ValueError`로 못 잡음 +
  `args`에 `<MyErr instance at 0x...>` 자기_repr 오염

**f-string / str**
- `f"{x=}"` → `'3'` (debug 지시자 `=` 소실)
- `casefold()` 미작 (`straße` 그대로), `title()` apostrophe 규칙 (`It'S` vs `It's`),
  `center()` 홀수 remainder 배분 반대 (`-ab--` vs `--ab-`),
  `startswith(tuple)` 미지원, `splitlines()`의 `\x0b \x0c \x1c \x1d` 미분할
- `%` 포맷: `%(key)s` mapping, `%*d` 동적 너비, `%.3s`/`%5.1f` precision·width 미지원
- `f"{x=}"` 포함 파서 수용 범위는 3.12와 다르지 않되, except* 절 내 `return`을 컴파일 허용
  (CPython 3.11+는 `SyntaxError: 'break', 'continue' and 'return' cannot appear in an except* block`)

## C. 예외 타입/메시지 이탈 (저우선)

- `gen.throw(AssertionError("msg"))` → 타입이 `Exception`으로 승격 (원 타입 소실)
- 암시적 예외 체인: handler 내 새 raise에서 `__context__` 미설정 (None). `raise from` 경로는 정상
- `e.with_traceback`은 A3, `type.__base__` 속성 없음, `dict.__eq__`/`type.__eq__` 노출 없음
- `encode("no-such-codec")` → `LookupError`(cp) vs `ValueError`(sp)
- 메시지 서식: `take()` missing 인자 ("missing 2 required positional arguments: 'a' and 'b'"),
  `tuple.index` ("tuple.index(x): x not in tuple" vs `<str object at 0x…> is not in tuple` —
  **메시지에 메모지 주소가 박혀 출력 비결정적**), property setter/deleter 문구
- `compile("def f():\n nonlocal x", ...)` 구문 검사 약함 (비구문 nonlocal 통과)
- NameError 제안 과잉: `Did you mean: 'w'?` (CPython은 무제안)

## 판정 유보 항목

- `OSError(2, ...).errno` → **양쪽 모두 22** (Windows strerror 경로, 이탈 아님)
- p01 선행 항목(divmod/mod/shift/hash 동치 등)과 p06 다수는 일치

## 제안 우선순위 (수석 분석, 미수정)

1. **A2 CLR 유출** — 예외 경계 하나(호스트 콜 wrapper)로 대부분 막힐 가능성. 영향 최대.
2. **A1/A3/A4 컴파일·프레임 인덱스 크래시** — cell/comprehension/walrus 경로 회귀망 필요.
3. **인자 바인딩 4종** (순서/중복/kwonly/kw 유입) — CPython `ceval.c` argument_unpack 경로 대비.
4. **hashability 검사 + set/dict/live view** — 해시 테이블 공통 진입점에서 hash 실패 전파.
5. **int round 음수 ndigits, bool 프로토콜, `{x=}`, `%` 포맷** — 개별 소수정.

각 항목은 수정 전 본 문서의 재현 라벨을 고정하고, 대응 CPython 3.12 소스 경로를 보고서에 남긴다.
