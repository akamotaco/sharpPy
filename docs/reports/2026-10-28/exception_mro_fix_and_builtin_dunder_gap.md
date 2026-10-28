# 2026-10-28 세션 2 — 사용자 예외 MRO 수정 + 내장 듀던더 노출 격차 규명

branch `work/bug-hunt`. 지시: "성능이 느려도 정확도가 CPython과 동일해야 한다.
최적화 전에 발견된 버그부터 고친다."

## 1. 완료 — 사용자 정의 예외 서브클래스 raise/match (커밋 `30ec547`)

`def f(): class E(Exception)...; raise E` 가 `TypeError: exceptions must derive from
BaseException` 으로 실패. 원인은 세 가지가 모두 **이름/참조 동등 비교**로 MRO 를 안 밟은 것:

1. bare `raise Child`(user 서브클래스) → 인스턴스화가 `PyClassInstance`(CLR
   `PyBaseException` 아님)라 거부. → MRO 에 `BaseException` 이 있으면 원 클래스/인스턴스를
   보존한 `PyException` 으로 래핑해 raise.
2. `except <조상>` 이 다른 클래스인 서브클래스를 못 잡음(raise Child / except Base,
   except (E, Child)). `CHECK_EXC_MATCH` 가 정확 클래스 참조/이름만 비교. → raised 클래스
   MRO 를 걸어 조상(Exception/BaseException/custom base) 모두 매칭.
3. `IsExceptionLike()` 가 모든 객체에 true 반환(`raise 5` 가 int 를 래핑). → BaseException
   파생만 raisable.

부수 교정: 래퍼 생성자에서 `__args__` 를 인스턴스 repr 이 아니라 **생성자 인자**로 설정
(`raise E`→`()`, `raise E("x")`→`("x",)`).

검증: 독립 매트릭스 6종(bare/instance/다중 상속/tuple except/attrs/args/chaining/re-raise/
비예외 raise) 전부 CPython 3.12.15 와 byte-identical. 회귀: evaluate 225+3+2 불변,
bughunt 잔차 132 불변(신규 발명 없음), no_raise 성능 불변.
참조: `typeobject.c PyType_IsSubtype`, `exceptions.c BaseException_init`, `errors.c
PyErr_GivenExceptionMatches`.

## 2. 규명 — 내장 타입 special method(듀던더) 미노출 (서브시스템급, 미착수)

bughunt p01/p02/p03 잔차의 최대 요인. CPython 은 내장 타입도 special method 를 어트리뷰트로
노출해 명시 호출 가능하지만 SharpPy 는 대부분 AttributeError:

| 호출 | CPython | SharpPy |
|---|---|---|
| `l.__setitem__(0,9)` | `[9,2]` | AttributeError |
| `l.__getitem__(1)` | `2` | AttributeError |
| `l.__contains__(2)` | `True` | AttributeError |
| `l.__delitem__(0)` | `[2]` | AttributeError |
| `"a".__add__("b")` | `ab` | AttributeError |
| `(3).__add__(4)` | `7` | AttributeError |
| `[1].__mul__(2)` | `[1,1]` | AttributeError |
| `l.__len__()`, `d.__setitem__(...)` | 동작 | 동작 (예외적으로 등록됨) |

`l[::2]=x` **문법**(STORE_SUBSCR)은 정상. 깨진 것은 **듀던더 명시 호출** 경로.

범위 평가: `core/SlotDefs.cs` 는 상속용 **이름 목록**이지 디스패치 테이블이 아니다.
교정하려면 list/dict/str/int/float/set/tuple 각 내장 타입의 어트리뷰트 조회에
special method 래퍼(기존 CLR 구현에 바인딩, 인스턴스는 bound / 클래스는 unbound 시그니처)를
타입별로 붙여야 한다. 파일 다수·시그니처 다양·바인딩 의미 민감 → **포인트 픽스가 아닌 전용
작업**. 무리하게 이번ターン에 붙이지 않았다.

## 3. 잔차 카탈로그 (132줄, 심각도순)

- 높음(코어 동작): 내장 듀던더 노출(위). p03의 확장슬라이스/`in`/`map lazy`/`iter(v2)`/`zip
  strict`/`set unhashable` 대부분이 여기 또는 별도 프로토콜 부재.
- 중간: `iter(callable, sentinel)` 2-인자 미지원, `map` 비게이지(즉시 실행), `zip(strict=)`
  무시, set 미해시가능 검증 누락(`{[1]}` 허용), `reversed(range())` 회귀(`09c52e4`),
  `float.hex()`, `round(int, 음수)`, `str(-0.0)`, f-string `{x=}`.
- 낮음: 에러 메시지 문구(`tuple.index`, NameError suggestion), `%` 포맷 세부.

## 4. 권장 착순

1. 내장 듀던더 노출 (단일 최대 격차. 타입별 진행: list → dict → str → int/float → set/tuple).
2. 게이지 프로토콜: `map/filter` 지연, `iter(callable, sentinel)`, `zip(strict=)`.
3. set/dict unhashable + `reversed(range())` 회귀.
4. 숫자 포맷/round.

각 항목은独立 재현 → 수정 → evaluate/bughunt/perf 회귀 → 커밋.
