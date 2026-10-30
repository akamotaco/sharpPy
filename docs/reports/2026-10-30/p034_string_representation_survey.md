# P0-34 조사: `str` 표현과 코드 포인트 인덱싱 (착수 전)

2026-10-30. HEAD `0e53704`. 기준 CPython 3.12.15 (`v3.12.15`, `e848e4b09ca…`).
**이 문서는 조사와 제안만 담는다. 코드는 변경하지 않았다.**

## 1. 지금 표현

`type/PyString.cs:1719` — `public string Value { get; }`. .NET `string`, 즉 **UTF-16 code unit
배열이 곧 Python 문자열**이다. CPython 3.12는 PEP 393의 3종 저장(latin-1 1바이트 / UCS-2 / UCS-4)을
쓰며 **Python이 보는 길이는 always 코드 포인트 수**다. 이 한 차이에서 아래가 전부 나온다.

### 진입점은 존재한다 (운이 좋았다)

| CPython | SharpPy | 위치 |
|---|---|---|
| `PySequence_Size` / `str.__len__` | `PyString.Length()` | `type/PyString.cs:2741` |
| `unicode_subscript` | `GetItem(PyObject)` → `GetItem(int)` | `2457`, `2471` |
| 슬라이스 | `GetItem`의 `PySlice` 분기, `GetSlice` | `2480`, `2506` |
| 비교 | `string.Compare(..., StringComparison.Ordinal)` 4곳 | `1855`, `1864`, `1873`, `1882` |

### 하지만 그 아래가 흩어져 있다 (`Value.Length` 115지점)

| 메서드 | 지점 | 성격 |
|---|---|---|
| `InitializeStringDescriptors` | **69** | descriptor 클로저 **안에** 길이·인덱스 산술이 인라인 |
| `Find` | 10 | `find/index/rfind` |
| `Count` | 8 | `count` |
| `Encode` | 5 | codec 경계 |
| `GetItem` | 3 | 인덱싱 |
| `IsAlpha/IsDigit/IsAlnum/IsSpace` | 8 | 문자 속성 |
| `AsList`, `ToRepr`, `Multiply`, `FormatPercent`, `Length`, `GetSlice`, `IsTrue`, `IsIdentifier` | 10 | 각자 |

**구조 진단**: `str`에는 시퀀스 코어가 없다. 115지점 중 69지점이 타입 등록 블록 안에 인라인으로
산재해 있다. 따라서 "115곳을 고친다"는 접근은 이번 프로젝트가 반복적으로 처벌해온 **두 번째 병렬
경로 만들기**다. 코어를 만들고 **인라인 산술을 그 코어로 흡수**해야 한다.

## 2. 측정된 어긋남 (`.artifacts/tmp/p34_survey.py`, 49케이스 중 **28 어긋남**)

| 케이스 | CPython | SharpPy | 원인 분류 |
|---|---|---|---|
| `len("a\U00010400b\U0001F600c")` | `5` | `7` | 길이 |
| `[ord(c) for c in S]` | `[97, 66560, 98, 128512, 99]` | `[97, 55297, 56320, 98, …]` | 반복·chr |
| `S[1]` | `0x10400` | `0xD800` (서로게이트 절반) | 인덱싱 |
| `S[1:4]` | 4코드 포인트 | 3코드로 잘림 | 슬라이스 |
| `S[::-1]` | 대역 순서가 코드 포인트 기준 | 서로게이트가 뒤집혀 `U+FFFD`로 붕괴 | 슬라이스+인코딩 |
| `"".join(reversed(S))` | 정상 | `efbfbd` 연속 | 위와 동일 |
| `S.find(E)` / `S.index(E)` | `3` | `4` | 검색이 UTF-16 오프셋 반환 |
| `S.upper()` 길이 | `5` | `7` | 길이 (case 자체는 이제 정합) |
| `ljust/center/rjust/format('>7')` | 코드 포인트 기준 여백 | UTF-16 기준(모자람) | 패딩 |
| `format(S, '.3')` | 앞 3코드 포인트 | 앞 3유닛(절반 서로게이트) | 정밀도 |
| `len(chr(0x10400))` | `1` | `2` | 생성 |
| `ord(chr(0x10400))` | `66560` | `TypeError: ord() expected a character…` | 생성+ord |
| `b"\xf0\x90\x90\x80".decode('utf-8')` 길이 | `1` | `2` | 디코딩 |
| `"\U00010400".isalpha()` | `True` | `False` | `char.IsLetter`가 서로게이트 절반만 봄 |
| `max(S)` / `sorted(S)` | 코드 포인트 | 서로게이트 절반 단위 | 정렬·최댓값 |
| `enumerate(S)` 마지막 인덱스 | `4` | `6` | 길이 |
| `(G*2)[1:3]` | `G` | `U+FFFD` 두 개 | 곱+슬라이스 |
| `"\ud800"` 인코딩 | (surrogatepass 시 `eda080`) | 조용히 `efbfbd` | **조용한 대체** (P1 codec과 별개 아님) |
| `sorted([chr(0x10FFFF), chr(0xE000), chr(0xFFFF), chr(0x41)])` | `['A','\ue000','\uffff','\U0010ffff']` | `['A','\U0010ffff','\ue000','\uffff']` | **순서 자체가 다름** |
| `min(["\U00010000", "\ufffe"])` | `'\ufffe'` | astral 쪽을 고름 | 위와 동일 |

이미 맞는 것(회귀로 지키해야 함): BMP 정렬 순서, `==`/딕셔너리 키 순서, `find`의 BMP 오프셋,
`startswith/endswith`, `split/partition/join`의 BMP, `%s`, `encode('utf-8')` 길이(완전한 문자열),
case 운영(P0-31에서 닫음).

**새로 확인한 부수 결함 2개**
1. `utf-16-le`/`utf-32-le` 미지원 (`LookupError: unknown encoding`) — P1 codec 항목과 같은 뿌리.
2. 정상 Python `TypeError` 뒤에도 `--- C# Exception Details ---` 블록이 따라 나온다 (표시 오염).

## 3. 선택지

### A. PEP 393 3종 저장 (latin-1 / ucs2 / ucs4)
- 의미 일치: 가장 완벽. 길이·인덱스·순서·정렬이 전부 자연스럽게 맞는다.
- 비용: `PyString` 재작성 + `Value` 소비자 전수(PyString.cs 197곳, `PyBuiltin` 71, `PyVM` 63,
  `compile.cs` 20, codec/regex/stdout 경계). BCL 문자열 API(`IndexOf`, `Regex`, `Encoding`)와
  매 경계에서 변환이 생긴다.
- 평가: **현 단계에서 감당 불가**. A로 가려면 위 소비자들을 먼저 코어 API 경유로 정리하는 별도
  공수가 선행이다.

### B. UTF-16 저장 유지 + **코드 포인트 시맨틱을 한 곳**에서 (권장)
- `core/UnicodeText.cs` 신규: `HasAstral`, `CpCount`, `CpAt(i)`, `CpToUnit(i)`, `UnitToCp(j)`,
  `Slice(units, from, to)`, `Compare(a, b)` (코드 포인트 사전순).
- **속도**: BMP 전용 문자열(현실 텍스트의 대부분)은 `HasAstral`이 false라 단위=코드 포인트이며
  모든 연산이 지금과 동일한 경로를 탄다. 탐지는 `IndexOfAnyInRange('\uD800','\uDFFF')`(벡터화).
  컴파일 상수 문자열은 그 자리에서 플래시를 박아 실행 시 탐지조차 없게 할 수 있다.
- astral 문자열은 `int[]`(또는 단위→코드 포인트 인덱스)를 **지연 계산**해 캐시.
- 라우팅 대상: `Length`, `GetItem`, `GetSlice`, `__iter__`/`AsList`/`reversed`, `ord`, `chr`, `%c`,
  `Find/Count/Index/StartsWith/EndsWith`, `Split/Partition/Replace/Strip`, 패딩
  (`ljust/rjust/center/zfill`), 포맷의 `width/precision`, 비교 4곳.
- **흡수 원칙**: 위에서 손대는 곳은 반드시 인라인 산술을 **삭제**하면서 옮긴다. `Value.Length`
  115지점 중 69지점이 `InitializeStringDescriptors` 인라인이므로, 순증하지 않고 줄어드는 것이
  정상이다. 순증하면 설계를 다시 본다.
- 남는 것: 저장소는 여전히 UTF-16이므로 **저장 표현에 의존하는 경계(codec, regex 오프셋, stdout)**
  는 별도 단계로 남는다. 즉 B는 "연산 의미는 닫고 표현 차이는 장부에 남기는" 선택이다.

### C. 코드 포인트 배열(`int[]`)을 1급 저장으로 + `string`은 지연 materialize
- O(1) 인덱싱·자연스러운 순서. 그러나 `Value`가 계산된 프로퍼티가 되어 197+71+63+20소비자에서
  숨은 O(n)이 발생하고, `string` 동일성/`==`에 기대는 코드가 깨진다. **B보다 위험하고 이득은 비슷**.

### D. 문서화만
- 28/49 어긋남을 알면서 남기는 것. 각하.

## 4. 권장 실행 (B, 3커밋)

1. **기초 (코드 추가, 동작 변화 0)** — `UnicodeText` + 단위 테스트성 프로브. 게이트가 전부
   바이트 그대로여야 한다 (죽은 코드부터 시작). 이 순서가 아니면 두 경로가 생긴 채로 걷게 된다.
2. **시퀀스 시맨틱 라우팅** — 위 목록을 옮기며 인라인 산술 삭제. `p34_survey`를
   `tests/bughunt/p72_codepoint_index.py`로 승격해 게이트화. 이 커밋에서 어긋남 28 → 0(또는 아래
   잔여까지 포함해 명시)이 목표.
3. **속성·정렬 마감** — `isalpha/isupper/...`의 astral은 general category가 필요하므로
   `UnicodeCaseData`와 같은 생성기 패턴으로 `Alpha` 속성 범위를 추가(생성 방식은 이미 검증됨:
   기준 해석기에서 `chr(cp).isalpha()` 측정). 비교 4곳을 `UnicodeText.Compare`로 교체.

**남는 잔여(장부 유지)**: regex 매치 오프셋(`_SreModule`의 5개 `Substring`, `match.start()/span`),
traceback 캐럼 열 위치(`compile.cs`), `StringIO.tell()` 좌표 의미, codec의 고립 서로게이트 처리와
`utf-16/utf-32` 계열 (`P1 codec`로 이미 등록), stdout 인코딩.

## 5. 결정이 필요한 지점

1. **B 3커밋으로 진행하는가** (A/C는 각하로 두는가).
2. astral 탐지 비용을 어디에 전가할 것인가: (a) 접근 시 지연 탐지, (b) 생성 시 eager 플래시,
   (c) **컴파일 상수는 컴파일 타임 플래시 + 런타임 문자열만 지연 탐지**(권장).
3. codec 경계(고립 서로게이트 조용한 대체, `utf-16/32` 부재)를 P0-34 3번째 커밋에 합칠か
   P1 codec으로 분리 유지인가.
4. `StringIO.tell()`/regex 오프셋을 이번 범위에 넣을 것인가 (넣으면 범위가 눈에 띄게 커진다).

## 6. 재현

```
PYTHONIOENCODING=utf-8 <cpython-3.12.15> .artifacts/tmp/p34_survey.py > .artifacts/tmp/p34_c.txt
PYTHONIOENCODING=utf-8 <dotnet> bin/Release/net8.0/sharppy.dll .artifacts/tmp/p34_survey.py > .artifacts/tmp/p34_s.txt
```
