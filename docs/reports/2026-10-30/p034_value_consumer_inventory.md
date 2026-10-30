# P0-34 사전 작업 1단계: `PyString.Value` 소비자 장부

2026-10-30. HEAD `9054af6` 위에 작업. 기준 CPython 3.12.15.
관련: [P0-34 표현 조사](p034_string_representation_survey.md)

## 왜 하는가

PEP 393식 3종 저장(latin-1/UCS-2/UCS-4)으로 바꾸려면 저장소를 갈아엎는 것보다 **`Value`를 직접
만지는 자릿수를 먼저 없애는 일**이 본체다. 지금은 `.Value`가 곧 UTF-16 버퍼라서, 저장을 바꾸면
`len`·인덱스·비교·패딩·인코딩이 파일 30개 넘게 동시에 어긋난다. 그래서 먼저 **의미를 묻는 API로
라우팅**하고, 그 다음에 저장을 바꾼다.

## 장부 (`.artifacts/tmp/p34_inventory2.py`, 재현 가능)

스캐너는 파일 안에서 `PyString`에 바인딩된 식별자(`PyString x`, `is PyString x`, `as PyString x`)를
모은 뒤 `식별자.Value` 사용만 센다. `PyInt.Value` 같은 다른 타입의 `.Value`는 제외한다. 과소/과대
계수 가능성이 있으니 절댓값보다 **분류**를 본다.

전체 **239지점** (PyString.cs 내부의 receiver 없는 `Value` 사용은 별도 314 언급):

| 종류 | 지점 | 의미 |
|---|---|---|
| pass-through | 108 | 텍스트를 그냥 남에게 넘김 (키, 메시지, 생성자) |
| other | 76 | 대입·반환 등 |
| unit-length | 27 | **`.Value.Length`** — Python `len`과 같은 의미로 쓰이는 자리 |
| compare-equals | 17 | `==` / `string.Compare*` — Python 정렬·동등 의미 |
| bcl-string-call | 6 | `IndexOf/Split/Trim/...` — BCL 유닛 의미 (interop) |
| unit-index | 5 | **`.Value[i]`** — Python `s[i]`와 같은 의미로 쓰이는 자리 |

파일별 상위: `core/PyBuiltin.cs` 52, `modules/_CodecsModule.cs` 24, `runtime/PyVM.cs` 24,
`type/PyString.cs` 17(수식 내), `core/PyType.cs` 13, `core/PyTypeMetaclass.cs` 12, `type/PyDict.cs` 12.

## 이 커밋에서 사라진 중복 (단순 라우팅이 아니라 중복 삭제)

라우팅 대상은 이미 **"두 번째 구현이 있던 자리"**였다. `str`은 자기 시맨틱 메서드를 갖고 있는데도
소비자들이 버퍼를 직접 계산했다.

| 자리 | 전 | 후 |
|---|---|---|
| `PyVM` `len` 빠른길 | `new PyInt(str.Value.Length)` | `str.Length()` |
| `PyVM` starred unpack (`a, b = "ab"`) | 개수 검사·`new PyString(str.Value[i].ToString())` | `str.Length()`, `str.GetItem(i)` |
| `PyASTOptimizationRules` `len("x")` 상수 폴딩 | `str.Value.Length` | `str.Length()` |
| `PyASTOptimizationRules` `n * "x"` 폭발 가드 | `str.Value.Length` | `str.Length()` |
| `PyIOBase.StaticSize` / `next()` 종결 | `s.Value.Length` | `s.Length()` |
| `ord()` | `str.Value.Length == 1`, `(int)str.Value[0]` | `str.Length() == 1`, `str.CodePointAt(0)` |
| str 비교 4곳 (`PyString`) + str subclass 비교 4곳 | `string.Compare(…, Ordinal)` / `string.CompareOrdinal` **8개** | **`PyString.CompareText` 1개** |

`CompareText`에는 지금 순서가 CPython과 다르다는 사실을 주석으로 박아 뒀다 — UTF-16 unit 순서는
astral을 U+D7FF와 U+E000 사이에 놓기 때문에 `sorted([chr(0x10FFFF), chr(0xE000), chr(0xFFFF),
chr(0x41)])`가 어긋난다. **flip은 한 함수에서만 일어난다.** `CodePointAt`도 같은 방식으로, 호출자를
안 움직이고 astral을 고칠 수 있는 자리에 뒀다.

`len(str)` 구현이 4개(VM, AST 폴딩, IOBase, `PyString.Length()`)에서 **1개**로, 텍스트 비교가 8개에서
**1개**로 줄었다. 이 자체가 P1 "한 운영에 구현 하나"의 성과다.

## 남은 코호트 (순서대로)

1. **codec 경계** — `modules/_CodecsModule.cs` 24지점. 디코딩 결과의 "소비한 위치"를
   `pyStr.Value.Length`로 반환하는 자리들 (`new PyInt(pyStr.Value.Length)` ×7)과 `Encode` 5지점.
   유닛/코드 포인트/바이트가 한 수식으로 섞여 있어서개별 라우팅 금지. 고립 서로게이트의 조용한
   `U+FFFD` 대체(`P1 codec`)와 같은 판에서 처리한다.
2. **내부 115지점** — `type/PyString.cs`. 69지점이 `InitializeStringDescriptors` 인라인. 라우팅이 아니라
   **흡수 삭제**가 목표(순증하면 설계 실패로 본다).
3. **parse/compile 좌표** — `runtime/compile.cs` `Substring` 20지점 + `Parser/ast.cs` 5지점. 소스 열
   위치, 상수 서브스크립트, 캐럼 위치. CPython은 코드로 세므로 PEP 393 전환 시 함께 준다.
4. **regex/문자열 IO 좌표** — `modules/_SreModule.cs` `Substring` 5지점, `core/PyStringIO.cs`.
   `match.start()/span()`과 `tell()`의 좌표 계를 정해야 한다.
5. **interop 탈출구 정형화** — `bcl-string-call` 6지점처럼 정말로 UTF-16을 써야 하는 곳을 명시적
   헬퍼 한 곳으로 모은다(그 외 `.Value` 직접 접근은 금지 대상).

## 게이트 (이 라우팅 후 실측)

- `dotnet build -c Release -p:SkipCodeGeneration=true`: 경고 0 / 오류 0
- `tools/run_bughunt.py`: **76/76 matching; mismatches: []**
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3,
  "self_test_no_reference": 2}` (변화 없음)
- Godot 4.7.2 mono Release replay: `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`,
  `REPLAY_RESULT 82 matched, 0 other`
- 성능: 변경이 닿는 `unpack_star` **+0.2〜+1.4%**. `str_fmt`/`list_comp` 플래스는 **变更前 바이너리에서도
  동일하게** 나왔다(+19.9%/+84.6% → 다른 라운드 +7.8%/−4.0%). 즉 기계 상태 노이즈. 2라운드 교차 표:

| workload | old r1 | old r2 | new r1 | new r2 |
|---|---|---|---|---|
| `str_fmt` | +19.9% | +7.8% | +4.8% | +18.6% |
| `list_comp` | +84.6% | −4.0% | −7.2% | +104.5% |
| `unpack_star` | +0.2% | +0.5% | +1.4% | +0.3% |

## 새로 기록한 작은 격차

`eval("%s")`처럼 평가된 소스의 SyntaxError에서 CPython은 `(<string>, line 1)` 위치를 붙이지만
SharpPy는 `SyntaxError: invalid syntax`만 낸다 (장부에 P1로 추가).
