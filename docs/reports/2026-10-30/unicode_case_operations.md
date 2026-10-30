# case 운영을 단순 매핑에서 완전 매핑으로 (P0-31)

2026-10-30. 기준 해석기 CPython 3.12.15, 고정 소스 `v3.12.15`
(`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`).

## 증상이 아니라 코드로 시작했고, 그덕에 항목이 다섯 개 더났다

`"ß".upper()`가 `'ß'`로 나온다는 것에서 출발했다. ASCII 밖의 문자 하나하나에서 CPython의 답을
뽑아보니 항목은 upper/lower 둘이 아니라 아홉 개였다.

| 메서드 | 어긋난 코드 포인트 | 첫 사례 |
|---|---|---|
| `upper` | 623 | `"ß".upper()` → `'SS'` |
| `lower` | 521 | `"İ".lower()` → `'i\u0307'` |
| `swapcase` | 682 | `"ß".swapcase()` → `'SS'` |
| `capitalize` | 627 | `"ß".capitalize()` → `'Ss'` |
| `title` | 670 | `"ǅ".title()` → `'ǅ'`, `"ßx".title()` → `'Ssx'` |
| `casefold` | 807 | `"µ".casefold()` → `'μ'`(U+03BC) |
| `isupper` | 302 | `"Ⅰ".isupper()` → `True` |
| `islower` | 303 | `"ͅ".islower()` → `True` |
| `istitle` | 333 | `"ǅ".istitle()` → `True` |

지불된 대가는 인정한다: 이번 항목은 case 운영 아홉 개를 모두 다시 썼다.

## 원인

`.NET`은 Unicode **단순(simple) 1:1 매핑**과 단순 속성(`char.IsUpper`가 읽는 것)만 제공한다.
CPython은 **완전(full) 매핑**과 `cased` / `case-ignorable` / `uppercase` / `lowercase` /
`titlecase` 속성을 쓴다. 그래서

- 다중 확장(U+00DF → `SS`, U+0130 → `i`+U+0307, U+0149 → `ʼn`, …)이 통째로 빠지고 (103개)
- Other_Uppercase / Other_Lowercase / Lt 문자(로마 숫자, 동그라미 문자, ǅ)에서 속성 판정이 함께 틀린다.
  `char.GetUnicodeCategory`로 만든 `IsCasedChar`(Lu/Ll/Lt)가 정확히 이 구멍이었고, 그래서
  `istitle`까지 같이 어긋났다.
- culture-aware 경로는 애초에 금지. `"İ".ToLower()`가 문화에 따라 다른 답을 주는 것과 같은 메커니즘이다.

그리고 case 운영만 해도 SharpPy 안에 **구현이 네 겹**이었다 — descriptor 클로저, private helper,
public 인스턴스 메서드, 정적 helper. 그 중 `Upper(PyObject[])`/`Lower(PyObject[])`/`TitleMethod`와
`Title()`/`IsUpper()`/`IsLower()`/`IsTitle()`은 호출자가 0명이었다.

## 방법

표는 만들지 않고 **CPython에서 가져왔다.** 알고리즘만 CPython 것을 옮겼다.

- `.artifacts/tmp/gen_unicode_case.py` → `core/UnicodeCaseData.cs`
  - 매핑: 기준 해석기에게 `chr(cp).upper()/.lower()/.title()/.casefold()`를 직접 물어 채웠다.
    해석기가 답할 수 있는 것이 기준이다.
  - 속성: 코드 포인트 단위 속성 API가 Python에 없으므로 고정 `Objects/unicodetype_db.h`에서
    `gettyperecord()`와 같은 경로(`index2[index1[ch>>7]<<7 | ch&0x7f]`)로
    `LOWER 0x08 / TITLE 0x40 / UPPER 0x80 / CASE_IGNORABLE 0x1000 / CASED 0x2000`를 읽었다.
    디코더는 기동 시 자체 점검(U+0041, U+01C5, U+2160, U+24B6, U+0300, U+0027, U+03A3, U+03C2)을 돈다.
    `'`(U+0027)가 case-ignorable로 나오는 것까지 일치한다 — `"Σ'Σ".lower() == "ς'ς"`의 근거다.
  - 블롭: 매핑은 (start, end, value, kind) stride-4 범위(shift / 다중 테이블 인덱스)로 병합,
    속성은 범위 리스트, ASCII 128은 직접 테이블. 256 코드 포인트 블록 인덱스를 얹어검색 단계를
    줄였다.
- `core/PyUnicodeCase.cs`: `do_upper`, `do_lower`, `do_title`, `do_capitalize`, `do_swapcase`,
  `do_casefold`, `unicode_isupper_impl`, `unicode_islower_impl`, `unicode_istitle_impl`,
  `lower_ucs4` + `handle_capital_sigma`. 지킨 세부:
  - `capitalize`의 첫 문자는 upper가 아니라 **title** 매핑 (`"ßx".capitalize() == 'Ssx'`,
    `"ǆ".capitalize() == 'ǅ'`).
  - `title`의 `previous_is_cased`는 **원본 문자열**을 본다 (연결 확장을 이전 결과로 판단하지 않는다).
  - case-ignorable은 cased를 통과시키지 않는다 (`"ͅß".isupper() == True`).
  - 시그마는 CPython과 똑같이 세 번 훑는다: `s[i-1]` cased이면 일반, 그 다음 cased가 아니면 최종,
    case-ignorable은 건너뛴다.
  - 서rogate는 CPython처럼 각자가 하나의 코드 포인트다.

## 회귀

- `tests/bughunt/p71_unicode_case.py` — 93케이스, CPython 대비 차이 0.
  출력은 UTF-8 hex. SharpPy는 문자열을 UTF-16 unit으로 인덱싱해서(P0-34) `ascii()`/`len()`이
  case 운영의 결과와 무관하게 비교를 오염시키기 때문이다.
- 풀 매트릭스 26,343비교 + 문자열 코퍼스 4,140비교(시그마 문맥 34종, 임의 혼합 160종, 비-보조 평면)
  — 차이 0.
- 이 회귀들이 성능 손질 중에 두 번 실제로 어긋남을 잡았다: 재사용 버퍼 때문에 시그마 문맥 스캔이
  이전 호출 잔해를 보는 버그, 블록 인덱스에서 범위 **끝** 검사를 빼서 `ß`.upper()가 U+0095를
  돌려준 버그.

## 김에 정리한 P0-29 조각

- str descriptor 46곳의 수신자 오류를 `PyString.AsStrReceiver(self, name)` 하나로 모았다.
  CPython `descr_check`의 문구는 `descriptor 'upper' for 'str' objects doesn't apply to a 'int'
  object`. SharpPy가 쓰던 `requires a 'str' object but received a 'int'`는 CPython이 만들지 않는
  문구였다. **dunder 슬래퍼 래퍼는 예외**: `str.__len__(5)`는 CPython도
  `descriptor '__len__' requires a 'str' object but received a 'int'`라고 답한다. 11곳은 그대로.
- 제로 인자 과잉 인수 19곳에 정의 타입 접두: `str.upper() takes no arguments (1 given)`.

## 비용 (정확성을 사서 온 것)

200-540자 문자열 25,000회, 이전 커밋 worktree와 교차 측정(3-5라운드 best):

| workload | 이전 | 이후 |
|---|---|---|
| ASCII `upper()` (변화 있음) | 0.0653 | 0.0906 (+39%) |
| ASCII `lower()` (변화 없음) | 0.0651 | 0.0874 (+34%) |
| 그리스 `upper()` | 0.0388 | 0.0856 (+121%) |
| 그리스 `lower()` | 0.0392 | 0.1367 (+249%) |
| 그리스 `casefold()` | 0.0716 | 0.1523 (+113%) |
| `capitalize()` 혼합 | 0.0356 | 0.0779 (+119%) |
| `isupper()` ASCII | 0.0199 | 0.0201 (+1%) |

ASCII 경로는 "전부 ASCII인가"를 한 번 확인한 뒤 `ToUpperInvariant`/`ToLowerInvariant`에 위임한다 —
ASCII에서 그 intrinsic이 CPython의 ASCII 매핑과 동일하고 문화 개입이 없으며 벡터화돼 있기 때문이다.
남는 +35%는 그 확인 한 바퀴다. 비-ASCII는 블록 인덱스에도 불구하고 중간 `int[]` + `StringBuilder`가
남는다. 이건 P0-36으로 등록했다. 표준 스위트에는 영향이 없다(case 운영을 부르지 않는다):
old/new 교차 `str_fmt +3.6%`, `list_comp -0.4%`, 나머지 ±0. 중간에 보인 `list_comp +71%`은 같은
바이너리 재측정에서 -0.8%로 돌아온 기계 상태 노이즈였다.

## 이번 세션에서 새로 등록한 것

- **P0-34** `str`이 UTF-16 code unit 인덱싱 — `len("\U0001F600") == 2`, `ord(s[0]) == 0xD800`,
  `"%c" % 0x1F600`이 `ValueError`. case 운영 내부에서는 surrogate를 조합하지만 문자열 자체가 아니다.
- **P0-35** 소스 디코딩 — NUL 바이트를 CPython은 `SyntaxError: source code cannot contain null
  bytes`로, 무효 UTF-8 바이트는 `SyntaxError: Non-UTF-8 code starting with '\xff' in file ... on
  line N`으로 거절한다. SharpPy는 대체 디코딩 후 진행해서 CLR 스택 트레이스를 토한다.
- **P0-36** 위 비용.

## 게이트

- `tools/run_bughunt.py` **76/76**
- `tools/evaluate.py` `{"matching_output": 225, "matching_except_volatile_fields": 3,
  "self_test_no_reference": 2}` (변화 없음)
- Godot 4.7.2 mono Release replay: `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`,
  `REPLAY_RESULT 82 matched, 0 other`
