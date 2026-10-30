# P0-34 pre-work 코호트 1 — 코덱 경계: 인코딩 하나, 디코딩 한 implementation

날짜: 2026-10-30 · 브랜치 `work/bug-hunt` · 기준 CPython 3.12.15 (`e848e4b0`)

## 문제

`str`을 바이트로, 바이트를 `str`으로 바꾸는 경로 다섯 곳이 **각자** 코덱 표를 갖고 있었다.

| 경로 | 옛 구현 | 결과 |
|---|---|---|
| `str.encode` (descriptor) | `encoding switch` 5개 → `Encoding.GetBytes` | `'é'.encode('ascii')` → `b'?'` |
| `PyString.Encode` | CPython식 거부 보유 | 호출자 없음 (사본) |
| `bytes(str, enc)` builtin | 4개 이름 표 → `GetBytes` | `bytes('é','ascii')` → `b'?'` |
| `str(bytes, enc, errors)` builtin | 3개 이름 표 | `str(b'ab','utf-16-le')` → LookupError |
| `bytes.decode` | utf-8/ascii 수작업 + 3개 이름 표 | `b'ab'.decode('utf-16-le')` → LookupError |
| `_codecs.*_encode` | codec별 `Encoding` 직접 호출 | lone surrogate → `b'\xef\xbf\xbd'` |

.NET 인코더는 인코딩하지 못하는 것을 조용히 대체 문자로 바꾸므로, 라이브 경로는 전부
**조용한 데이터 손실**이었다. CPython은 거부한다. 반면 `PyString.Encode`에는 이미 CPython과
같은 거부가_CodeBytes 단위로 구현돼 있었는데 **아무도 부르지 않았다** — 약한 사본이 라이브였다.

## 측정한 CPython 동작 (전부 인터프리터에서 직접)

```
'é'.encode('ascii')      !! UnicodeEncodeError: 'ascii' codec can't encode character '\xe9' in position 0: ordinal not in range(128)
'aéêb'.encode('ascii')    !! UnicodeEncodeError: 'ascii' codec can't encode characters in position 1-2: ordinal not in range(128)
chr(0xD800).encode('utf-8')    !! 'utf-8' codec can't encode character '\ud800' in position 0: surrogates not allowed
chr(0xD800).encode('latin-1')  !! 'latin-1' codec can't encode character '\ud800' in position 0: ordinal not in range(256)
'é'.encode('ascii','ignore')   -> b''          'é'.encode('ascii','replace') -> b'?'
chr(0x10400).encode('ascii','replace') -> b'?'  (한 문자 = '?' 하나)
'e'.encode('ascii','bogus')     -> b'e'        (핸들러는 실패할 때만 참조된다)
chr(0xD800).encode('utf-8','surrogatepass') -> b'\xed\xa0\x80'
chr(0xDC80).encode('utf-8','surrogateescape') -> b'\x80'
'ab'.encode('utf-16')     -> b'\xff\xfea\x00b\x00'      'ab'.encode('utf-16-le') -> b'a\x00b\x00'
'ab'.encode('utf-32')     -> b'\xff\xfe\x00\x00a\x00\x00\x00b\x00\x00\x00'
''.encode('utf-16')       -> b'\xff\xfe'
chr(0x10400).encode('utf-16-be') -> b'\xd8\x01\xdc\x00'
_codecs.utf_8_encode('ab') -> (b'ab', 2)        (소비 단위는 문자)
```

CPython 소스 기준: `Objects/unicodeobject.c` `unicode_encode_ucs1` (ascii/latin-1의 범위 거부와
run 위치), `PyUnicode_AsUTF8String`/`_PyUnicode_EncodeUTF8` (surrogate 정책),
`Objects/codecsmodule.c`의 `s|s:utf_8_encode` 파서 (getargs.c 계열 arity 메시지).

## 고친 것

1. **단일 encoder** `PyString.Encode(encoding, errors)` — code point를 걸어 바이트를 만들고,
   거부를 여기서만 한다. `str.encode` descriptor와 `_codecs.*_encode` 아홉 개, `bytes(str, enc)`
   builtin이 모두 여기로 온다. `utf-8`은 값에 따라 길이가 바뀌므로 자체 쓰기, `utf-16*`는 astral
   문자를 서러게이트 쌍으로, `utf-32*`/`latin-1`/`ascii`는 고정 폭.
2. **에러 핸들러는 실패 시점에 해석** — strict / ignore / replace / surrogatepass /
   surrogateescape(U+DC80..U+DCFF만). 미지 이름은 참조됐을 때만 LookupError.
3. **코덱 별칭 표** — `utf-16`/`utf-32`를 BOM 코덱으로 분리. (`UnicodeEncoding`의 첫 인자는
   BOM 여부가 아니라 big-endian이라, 디코딩 쪽에 `true`를 넘기면 BOM 없는 `b'a\x00b\x00'`를
   big-endian으로 읽고 BOM을 결과에 남겼다.)
4. **단일 decoder 경로** — `str(bytes, enc, errors)`와 `bytes.decode`의 그 밖의 코덱이
   `PyStringNewArguments.DecodeBytes`로 간다. BOM 소비, BE 마크 탐지, `utf-32-be` endian 교정.
   `bytes.decode`의 utf-8/ascii 경로는 바이트 위치 메시지가 더 정확하므로 유지하고, 나머지 위임.
5. **사본 삭제** — 호출자가 없는 `PyString.FromBytes` (자체 4개 이름 표 + `catch (Exception)`로
   LookupError를 ValueError로 덮던 코드) 제거.
6. **메시지 교정** — 거부의 escape 폭(`\xNN`/`\uXXXX`/`\UXXXXXXXX`), run 셀 때 실패 문자를 두 번
   세던 것(1문자 문자열이 "position 0-1"이라 말했다), `_codecs` 진입점의
   `utf_8_encode expected at most 2 arguments, got 3` /
   `utf_8_encode() argument 1 must be str, not bytes`, `encode() argument 'encoding' must be str, not int`.

diff: `+402 / -172` (5 files + 신규 probe). 코드 추가가 큰 이유는 기존 사본들이 각 10줄짜리
`Encoding` 표였던 반면 단일 encoder는 코드포인트 스캔·핸들러·메시지를 한 곳에서 담당하기 때문이다.
**사본 수는 6 → 2** (encode 1, decode 1) + `bytes.decode`의 utf-8/ascii 정밀 경로.

## 검증

- 신규 `tests/bughunt/p72_encode_strict.py`: 106 case, CPython 대비 **diff 0**
- `tools/run_bughunt.py`: **77/77 matching**
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}` (변화 없음)
- Godot 4.7.2 mono Release replay: `SHARPPY_PATH_CHECKS 5`, `SHARPPY_HOST .NET 10.0.12`,
  `REPLAY_RESULT 82 matched, 0 other`
- perf: `str_fmt`/`list_comp`가 최고 +19%/+84%로 깃발됐으나 같은 바이너리 3회 재측정이
  −0.9% / +4.4% / +19.1%, −2.8% / −2.4% / +83.6%로 흩어지고, `bench_suite.py`에는 encode/decode
  호출이 **아예 없다** (grep 0). 수정한 경로가 실행되지 않으므로 측정 노이즈 + 낡은 baseline
  (기존 housekeeping 항목).

## 남는 것 (P0-34 본체에 묶임)

- `_codecs.utf_8_encode(chr(0x10400))`의 소비 수 2 (CPython 1). `PyString.Length()`가 code unit
  기준이라 그렇고, 고칠 자리는 `Encoded()` 한 곳이다.
- `'\ud800\udc00'.encode('utf-16')` — CPython은 "별도 surrogate 코드포인트 두 개"를 거부하지만
  .NET `string`은 그것을 구분할 수 없어 SharpPy는 CESU 바이트를 낸다.
- 코덱 표 범위 (그 밖의 코드 페이지 → LookupError), `surrogateescape`의 비-utf-8 경로:
  `docs/known_gaps.md`에 등록.
