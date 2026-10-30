# 코덱 subsystem: SharpPy와 CPython 3.12.15 구현 비교

날짜: 2026-10-30 · 비교 대상: SharpPy `0fbf8a2` (`work/bug-hunt`) vs pinned CPython `v3.12.15`
(`e848e4b0`, `Desktop/work/cpython-v3.12.15`) + 실제 인터프리터 `sharppy-test-py312`

P0-34 pre-work 코호트 1(코덱 경계)을 끝낸 시점에서, "같은 동작을 CPython과 얼마나 같은 방식으로,
어디서 다른 방식으로 하는가"를 정리한다. 기준은 AGENTS.md의 규칙: **메커니즘은 달라져도 되고
관측 결과가 달라지면 버그다.**

## 1. CPython 쪽 구조 (소스에서 확인)

- 인코딩은 code point마다 **한 번** 스캔하고, 실패 지점에서 핸들러를 부른다.
  - `Objects/unicodeobject.c:6817 unicode_encode_ucs1(unicode, errors, limit)` — ascii(limit 128)와
    latin-1(256)가 같은 함수. `const char *encoding = (limit == 256) ? "latin-1" : "ascii"`,
    `reason = "ordinal not in range(128|256)"`.
  - 실패 시 `collstart`를 잡고 `while (collend < size && PyUnicode_READ(kind, data, collend) >= limit) collend++`
    로 **연속 구간**을 모은 뒤 `raise_encode_exception(&exc, encoding, unicode, collstart, collend, reason)`.
    예외는 `[start, end)` 반개방 구간이고, 표시 단계에서 `end == start+1`이면 "character '\xe9' in position 0",
    아니면 "characters in position 1-2"가 된다.
  - `case _Py_ERROR_REPLACE: memset(str, '?', collend - collstart)` → 문자당 `?` 하나.
  - `unicode_encode_utf8` (`Objects/unicodeobject.c:5296`)는 surrogate를 별도 정책으로 본다 —
    strict이면 `"surrogates not allowed"` (5633/5643/5654), surrogatepass는 CESU로 출력,
    surrogateescape는 U+DC80..U+DCFF만 바이트로 되돌린다.
  - `PyUnicode_AsUTF16String` (`6041`)은 BOM을 붙이고 astral을 surrogate pair로 쓴다.
- 문자 읽기는 `PyUnicode_READ(kind, data, pos)` — PEP 393의 kind(1/2/4바이트) 추상화.
  **len은 항상 code point 수**이므로 `_codecs.utf_8_encode('\U00010400') -> (b'\xf0\x90\x90\x80', 1)`.
- 핸들러는 이름→`_Py_error_handler` 열거 + 미지 이름이면 `PyCodec_LookupError`가 등록 가능 핸들러를
  탐색한다. 즉 **registry 기반**이고, 등록 시점 대신 사용 시점에 해석된다.
- `_codecs` 진입점은 `s|s:utf_8_encode` 형태의 `PyArg_ParseTuple` — 그래서 arity 메시지가
  getargs.c 계열인 `utf_8_encode expected at most 2 arguments, got 3`이고, 컨버터 메시지는
  `utf_8_encode() argument 1 must be str, not bytes`다 (clinic의 `()` 붙은 형태와 다르다).

## 2. SharpPy 쪽 구조 (지금)

- `type/PyString.cs`
  - `Encode(encoding, errors)` → `EncodeContext`(codec별 limit/width/UTF 여부) →
    `EncodeCodePoints(ctx, bigEndian)` 한 루프. 실패 판정·핸들러·메시지가 이 한 곳에 있다.
    `Emit`/`EmitUtf8`/`EmitUnit`이 바이트 쓰기, `RangeError`/`SurrogateError`가 메시지,
    `UnitIndexOfCharacter`가 **character index → UTF-16 unit offset 변환의 유일한 지점**.
  - `PyStringNewArguments.NormalizeCodecName` + `ResolveCodec` + `DecodeBytes` = 디코딩 한 곳.
- `modules/_CodecsModule.cs` — `_codecs.*_encode` 아홉 개가 `Encoded(args, codec, name)` 하나로 수렴.
- `core/PyBuiltin.cs` — `str(bytes, enc, errors)`는 `DecodeBytes`, `bytes(str, enc)`는 `Encode`.
- `type/PyBytes.cs` — `decode`는 utf-8/ascii만 자체 구현(`DecodeUtf8Python`/`DecodeAsciiPython`,
  바이트 위치 메시지가 `DecodeBytes`보다 정확)하고 나머지는 `DecodeBytes`에 위임.

## 3. 같은 점 (동작·의미 단위의 대응)

| CPython | SharpPy | 같은 의미 | 증거 |
|---|---|---|---|
| `unicode_encode_ucs1` limit 128/256 | `EncodeContext.Limit` 0x7F/0xFF | 범위 거부 | p72 `ascii_*`, `latin1_*` |
| `collstart/collend` 구간 | `RangeError`의 `start/end` 스캔 | "character … in position N" vs "characters in position N-M" | p72 `ascii_run`, `ascii_two_runs` |
| `\xNN`/`\uXXXX`/`\UXXXXXXXX` | `RangeError`의 `chDesc`, `AppendEscape` | 값에 따른 폭, 소문자 hex | p72 `ascii_astral`, `h_bs_*` |
| `_Py_ERROR_*` 지연 해석 | 핸들러를 실패 시점에서만 분기 | `'e'.encode('ascii','bogus')`는 성공 | p72 `h_unknown_unused/used` |
| `memset('?', collend-collstart)` | 문자당 `?` 1개 (astral도 1개) | replace 폭 | p72 `h_replace_astral` |
| utf8 surrogate 정책 | `surrogateRefusal` + `surrogatepass`/`surrogateescape` | "surrogates not allowed", CESU, U+DC80..U+DCFF | p72 `lone_*`, `h_pass_*`, `h_escape_*` |
| BOM + pair (`PyUnicode_AsUTF16String`) | `WithPreamble` + `Emit` pair | BOM 코덱/무 BOM 코덱 분리 | p72 `bom_*`, `astral_utf16be` |
| getargs.c arity | `Encoded(...)`의 name 인자 | 두 메시지 계열 구분 | p72 `c_utf8_too_many/not_str` |
| `(bytes, 소비 문자 수)` | `PyTuple{bytes, pyStr.Length()}` | 소비 단위 = 문자 (astral은 P0-34에 막힘) | p72 `c_*` |

`tests/bughunt/p72_encode_strict.py` **118 case diff 0** — 이 표의 모든 줄은 인터프리터 두 개가
같은 출력을 내는 범위 안에서만 주장했다.

## 4. 다른 점 — 출력을 바꾸지 않는 것 (허용)

- **저장**: CPython은 PEP 393 kind(1/2/4), SharpPy는 .NET `string`(UTF-16 unit). 그래서 SharpPy는
  code point 스캔을 매 연산마다 직접 하고, character↔unit 변환을 `UnitIndexOfCharacter` 한 곳에 모아둔다.
- **버퍼**: CPython `_PyBytesWriter`(사전 크기 계산 + collision 회피) vs `List<byte>`.
  CPython도 `codepoints * 4` 정도의 사전 할당을 쓰므로 모양은 같다.
- **핸들러 해석**: CPython은 `_Py_geterror_handler` + `PyCodec_LookupError`(registry), SharpPy는
  `switch` 문자열 비교. 등록 핸들러가 없다는 전제에서 관찰 동일.
- **인수 파싱**: CPython `PyArg_ParseTuple`/clinic, SharpPy `PyMethodDescriptor`+`ProjectKeywordSlots`.
  메시지만 맞췄다.
- **비-Astral 경로 최적화**: CPython은 kind별 specialized 루프, SharpPy는 단일 루프. perf 차이는
  bench suite에 encode 호출이 없어 측정되지 않는다(`bench_suite.py` grep 0건).

## 5. 다른 점 — 출력을 바꾸는 것 (남은 버그/갭)

측정으로 확인한 것만 적는다.

1. **registry가 없다.** CPython은 `Lib/encodings` + `_PyCodec_Lookup`으로 코덱을 찾지만 SharpPy는
   하드코딩 switch. 결과적으로 전부 실패:
   - `codecs.lookup('utf-16')` → CPython `CodecInfo`, SharpPy `LookupError: unknown encoding: utf-16`
   - `codecs.getencoder('utf-8')('ab')` → CPython `(b'ab', 2)`, SharpPy LookupError
   - 비-텍스트 코덱: `codecs.decode('ab','rot13')` → CPython `'no'`, `codecs.encode(b'ab','hex')` → `b'6162'`
     (SharpPy는 `TypeError`/LookupError).그리고 `'ab'.encode('rot13')`의 CPython 메시지는
     `'rot13' is not a text encoding; use codecs.encode() to handle arbitrary codecs` — SharpPy는
     `unknown encoding: rot13`.
   - 정준 이름: `codecs.lookup('LATIN1').name == 'iso8859-1'`, `'utf_16_le' → 'utf-16-le'` — SharpPy에 표가 없다.
2. **`_codecs` 이름 표면 35/45.** 없는 것: `utf_7_encode/decode`, `mbcs_encode/decode`,
   `code_page_encode/decode`, `oem_encode/decode`, `readbuffer_encode`, `unregister`.
3. **`namereplace` 미 구현** (`b'\N{LATIN SMALL LETTER E WITH ACUTE}'`) — Unicode 이름 표가 필요. 등록됨.
4. **strict 디코딩의 비-utf8 fidelity**: .NET 디코더는 잘못된 입력을 U+FFFD로 바꾸므로
   `b'\xff\xfe\x41'.decode('utf-32')`류에서 CPython의 `UnicodeDecodeError` 대신 대체 문자.
   `DecodeBytes` 주석과 ledger에 이미 기록된 한계.
5. **astral 관련 두 개는 P0-34 본체**: `_codecs.utf_8_encode(chr(0x10400))`의 소비 수 2(CPython 1),
   그리고 "별도 surrogate 두 개"(`'\ud800\udc00'`)를 .NET `string`이 구분 못 해 CPython은 거부하는
   인코딩이 CESU 바이트가 되는 문제.
6. `surrogateescape`의 비-utf-8 코덱 동작은 측정하지 않았고 지금은 strict 메시지로 거부한다(등록됨).

## 6. 요약

- **인코딩 로직 자체는 CPython과 같은 모양이 됐다**: 한 루프, code point 단위, 실패 지점에서 핸들러,
  구간 집계, 메시지 폭까지. p72 118 case로 고정했고 출력 차이는 없다.
- **차이는 로직이 아니라 인프라에 몰렸다**: 코덱 registry, Unicode 이름/코드 페이지 데이터,
  PEP 393 저장. 이 셋이 남은 어긋남의 전부이고, 각각 ledger에 항목으로 있다.
- 사본 수는 인코딩 6→1, 디코딩 4→1(+`bytes.decode`의 utf-8/ascii 정밀 경로). 메시지·별칭·BOM
  오류는 대부분 "약한 사본이 라이브였고 faithful한 사본은 죽어있던" 형태였다.

## 7. 다음

- 코호트 2 (`type/PyString.cs` 내부 115개 `Value.Length` 흡수, 순삭감이 목표) → 코호트 3
  (parse/compile 열 좌표) → 코호트 4(regex/StringIO 좌표) → 코호트 5(interop escape 형식화).
- 코덱 registry(`codecs.lookup`/비-텍스트 코덱/정준 이름)는 별도 항목으로 두고, P0-34 좌표 work가
  끝난 뒤 순서를 정한다. 지금 `Encode`/`DecodeBytes`가 단일 진입점이므로 registry는 그 위에 얹으면 된다.
