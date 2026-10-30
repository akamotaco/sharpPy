# P0-34 코호트 2 — 좌표계가 한 개가 됐다: len/index/slice/find/pad/order가 문자를 센다

날짜: 2026-10-30 · 브랜치 `work/bug-hunt` · 기준 CPython 3.12.15 (`e848e4b0`) · 프로브 `tests/bughunt/p75_string_coordinates.py` (측정 케이스 125줄, diff 0)

## 착수 전 측정

`.artifacts/tmp/p34_survey.py`(49케이스) 25 diff, `.artifacts/tmp/p34_matrix.py`(45케이스) 29 diff,
`.artifacts/tmp/p34_find.py`(30케이스), `.artifacts/tmp/p34_is.py`(22케이스) 11 diff.

```text
len(S)        CPython 5      SharpPy 7          # S = 'a\U00010000b\U0001F600c'
list(S)       CPython 5개    SharpPy 7개        # 서러게이트 절반이 원소가 된다
S[1]          CPython U+10000 SharpPy U+D800
reversed(S)   CPython 정상   SharpPy IndexError
S[::-1]       CPython 'c\U0001f600b\U00010000a' SharpPy 뒤집힌 절반들의 나열
S.find(...)   CPython 3      SharpPy 4          # 문자 오프셋이 아니라 유닛 오프셋
S.ljust(7)    CPython 2개 채움 SharpPy 0개
sorted([...]) CPython astral 최후미    SharpPy U+E000 앞
ascii(S)      CPython '\U0001f600'  SharpPy '\ud83d\ude00'
'x'.isdigit/isdecimal/isprintable/isidentifier 도 astral에서 전부 틀림
```

원인은 좌표계가 두 개였다는 것: 저장은 UTF-16 code unit(`.NET string`), Python 의미는 code point.
그리고 그 경계를 변환하는 코드가 호출 지점마다 따로 있었다.

## 구조: 변환은 한 곳

`type/PyString.cs`에 `#region Code-Point Coordinates` 추가 — 이게 유일한 변환 지점이다.

| 원시 함수 | 의미 |
|---|---|
| `IsContinuation(text, unit)` | unit이 pair의 낮은 절반인가 (문자 경계가 아니다) |
| `PairWidth(text, unit)` | 그 문자가 유닛 1개인가 2개인가 |
| `CodePointAtUnit(text, unit)` | 그 위치 문자의 code point |
| `HasSurrogatePairs(text)` | pair가 하나라도 있는가 |
| `CodePointCount(text)` | Python이 보고하는 길이 |
| `CodePointToUnit / UnitToCodePoint` | 좌표 변환 |
| `SubstringCodePoints` | code point 좌표 부분 문자열 |
| `StepCodePoints(start, stop, step)` | `s[a:b:c]` 순회 (한 번에 문자 1개, O(n)) |
| `TextWindow` / `AdjustedWindow(args)` | (start, end) 인자를 **두 좌표계로 동시에** 정규화 |
| `FindIndexOf(...)` | find/index/rfind/rindex **하나의** 구현 |
| `Characters(text)` | 문자 배열 (list/set/`(*s)`/그룹 멤버가 공유) |
| `IsLetterCp` 외 | 문자 클래스 판정 (code point 단위) |

핵심 설계: **pair가 없으면 code point index == unit index.** 그래서 `_pairState`(인스턴스별 lazy)가
`1`이면 모든 변환이 비교 한 번으로 끝난다. BMP 텍스트(=거의 전부)에서는 좌표 계층의 비용이 0에 가깝고,
pair가 있는 문자열만 스캔/순회 비용을 낸다.

## CPython 소스와 비교

| Python 의미 | CPython anchor | SharpPy (이전) | SharpPy (이후) |
|---|---|---|---|
| `len(s)` | `PyUnicode_GET_LENGTH` (character 개수) | `Value.Length` (unit) + 재계산 지점 다수 | `Length()` = 문자 개수 |
| `s[i]` | `unicodeobject.c:11368 unicode_getitem` → `PyUnicode_READ(kind, data, index)` | `Value[index]` | `CodePointToUnit` → `PairWidth`만큼 자르기 |
| `s[a:b:c]` | `unicode_slice` / `PySlice_GetIndicesEx` + 문자 복사 | unit 순회 (절반 유출) | `StepCodePoints` 한 구현 |
| `iter(s)` | `unicode_iterator` (`PyUnicode_READ`) | unit별 `Next()` | pair 단위 `Next()` |
| `reversed(s)` | 시퀀스 길이 = 문자 개수 | `ps.Value.Length` (재계산) → IndexError | `ps.Length()` |
| `find/index/rfind/rindex` | `unicodeobject.c:11348` 4개 진입점 → `any_find_slice`(`:9106`) 하나 | 진입점마다 정규화 코드 복사본 4개 | `FindIndexOf` 하나 |
| 창 정규화 | `ADJUST_INDICES`(`:9091`): end는 clamp, **start는 end로 내리지 않음** | start/end 모두 clamp | 동일 규칙 (그래서 `'abc'.find('', 4)`는 -1, `''.find('', 3)`은 3) |
| 빈 패턴 | `end - start < len2` 실패 후 lib_find: 순방향 start, 역방향 end | `-1` (빈 문자열 find가 -1이었음) | 동일 |
| 검색 일치 위치 | code point 비교 (`Objects/stringlib/codecs.h`) | UTF-16 `IndexOf`가 pair 안에서 일치 가능 | `StartsCharacter`로 경계 아닌 일치·너비 불일치 기각 (`S.find(chr(0xDC00))` → -1) |
| `count` | `stringlib/count.h`, 빈 패턴은 `end-start+1` | 별도 `StrEmptySpan`(unit 기준) | 창 공식으로 흡수, `StrEmptySpan` 삭제 |
| `startswith/endswith` | `unicode_startswith` → `PyUnicode_Tailmatch` | unit 창 | 문자 창 + 경계 검사 |
| `ljust/rjust/center/zfill` | `unicodeobject.c` + `stringlib/transmogrify.h` (`left = marg/2 + (marg & width & 1)`) | unit 길이, fillchar는 `char`(astral 거부) | 문자 길이, fill은 문자열, astral fillchar 허용 |
| fill 오류 메시지 | `The fill character must be a unicode character, not int` / `... exactly one character long` | 메서드별 자체 문구 | CPython 두 문구 그대로 |
| `format(s, spec)` | `Objects/stringlib/format.h` (문자 단위 width/precision) | unit 단위 | 문자 단위, fill 파싱도 pair 인식 |
| 비교/정렬 | `unicode_compare_startend`가 문자씩 비교 | `string.CompareOrdinal` (unit) | `CompareText`가 문자씩 비교, pair 없으면 ordinal fast path |
| `ascii()` | `Python/run.c` repr이 code point마다 이스케이프 | unit마다 → `\ud83d\ude00` | `\U0001f600` |
| 문자 클래스 | `IS_ALPHA/IS_DIGIT/IS_SPACE/IS_PRINTABLE` (`Objects/unicodetype_db.h`) | `char.IsXxx(unit)` | `CategoryOf(code point)`, 공백은 CPython 표와 동일한 코드 포인트 집합 |

`bytes.center`도 같은 transmogrify 공식을 쓰는데 `totalPad / 2`만 쓰고 있었다
(`b'ab'.center(5, b'-')` → CPython `b'--ab-'`, SharpPy `b'-ab--'`). 맞췄다.

## 중복 제거 실적 (감사 목표의 수치)

| 지표 | 이전 | 이후 |
|---|---|---|
| 리포지토리 `Value.Length` 지점 | 153 | **107** |
| `type/PyString.cs` 내부 | 70 | **26** |
| find/index/rfind/rindex 정규화 복사본 | 4 | **1** |
| `str` 길이 재계산 지점 | 다수(`reversed` 포함) | `Length()` 한 곳 |
| 죽은 문자 클래스 사본 | `IsDigit/IsAlpha/IsAlnum/IsSpace` 메서드 4개(호출자 0) | 삭제 |

남은 26곳은 인코딩 루프·내부 검색·repr처럼 **원래 unit이 맞는 단위**인 곳과 디스크립터 인라인이다.
다음 코호트가 그중 Python 좌표가 필요한 것을 계속 흡수한다.

## 게이트

- 빌드 오류 0
- `tools/run_bughunt.py`: **80/80 matching** (p75 신규, 125줄 diff 0)
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- Godot 4.7.2 mono: `SHARPPY_GODOT_PASS`, replay `82 matched, 0 other`
- perf (기준 커밋 `bff3f11` worktree와 교대 4라운드, 최소값):

  | 워크로드 | old | new | 배율 |
  |---|---|---|---|
  | `str_pct` (30000회 `%` 포맷) | 0.090 | 0.090 | 1.00x |
  | `len_loop` (새 문자열 len 60000회) | 0.057 | 0.055 | 0.96x |
  | `bmp_methods` (find/count/startswith/upper/strip/join) | 0.027 | 0.029 | **1.07x** |
  | `join_slices` | 0.001 | 0.001 | 1.00x |
  | `list_comp` | 0.044 | 0.042 | 0.95x |

  `bmp_methods` +7%만 실측 차이로 남는다(창 정규화 + 쌍 정렬 검사). astral 워크로드와 `%` 포맷은 변화 없음.

## 남은 것 (docs/known_gaps.md 등록)

1. **인접한 고아 서러게이트** — CPython은 `chr(0xD800)+chr(0xDC00)`를 code point 둘로 보관하지만
   이 저장 표현은 같은 `.NET string`을 U+10000 하나로 읽는다. `len()` 1, `find(chr(0xDC00))` -1.
   PEP 393식 저장을 채택하지 않은 데 남은 본질적 한계.
2. **`'\U0000d800'` 이스케이프가 파서를 죽인다** — CPython은 허용(`len` 1), SharpPy는
   `RuntimeError: A valid UTF32 value is ... should not include surrogate codepoint values`
   (`PyParserRuntime.ProcessEscapeSequences`가 `Char.ConvertFromUtf32`에 넘겨 CLR 예외가 그대로 노출).
   그래서 p75는 고아 서러게이트를 `chr()`로만 만든다.
3. `isdigit`는 Nd만 본다. CPython은 digit 속성(Other_Digit: 승수 문자 ²³¹, 동그라미 숫자 ①…)도 참으로 본다.
4. `isprintable`은 Cn(미배정)을 거르지 못한다. .NET에 미배정 전용 카테고리가 없어 표가 필요하다.
5. `isidentifier`는 XID_Start/XID_Continue 표 + NFC 요구 대신 카테고리로 근사한다. astral 첫 문자는 이제 맞고,
   NFC 정규화 요구는 여전히 미충족.
6. `Value.Length` 잔존 107지점(코호트 3~5 대상): `type/PyBytes.cs` 36, `modules/_SreModule.cs` 6 등.
