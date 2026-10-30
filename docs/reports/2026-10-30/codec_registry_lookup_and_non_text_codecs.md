# P0-34 pre-work 코호트 1 후속 — 코덱 등록부(registry): 이름→CodecInfo, 그리고 non-text 코덱

날짜: 2026-10-30 · 브랜치 `work/bug-hunt` · 기준 CPython 3.12.15 (`e848e4b0`) · 프로브 `tests/bughunt/p73_codec_registry.py` (116 케이스, diff 0)

## 문제

코호트 1으로 **인코딩/디코딩 자체**는 하나씩 남았지만, 이름을 코덱으로 바꾸는 층이 없었다.
CPython은 그 층이 따로 있다:

| CPython | 자리 | SharpPy (이 전) |
|---|---|---|
| `_Py_normalize_encoding` | `Objects/unicodeobject.c` | 없음 (원본 철자 그대로 표 조회) |
| `_PyCodec_Lookup` + search cache | `Python/codecs.c` | 없음 (하드코딩 표) |
| `PyCodec_Register` / `PyCodec_Unregister` | `Python/codecs.c:49,56` | `register`만 있던 척, `unregister` 없음 |
| `CodecInfo` (tuple 서브클래스) | `Lib/codecs.py` | 없음 |
| `_is_text_encoding = False` | `Lib/encodings/rot_13.py:45` | 없음 |
| `encodings/<name>.py` 326개 별칭 | `Lib/encodings/aliases.py` | 별칭 없음 |

관측된 결과 (CPython 3.12.15와 나란히 측정, `.artifacts/tmp/reg_spec*.py`):

```text
codecs.lookup('rot13')          CPython: CodecInfo          SharpPy: LookupError: unknown encoding: rot13
codecs.encode(b'ab','hex')      CPython: b'6162'            SharpPy: TypeError: encode() argument must be str, not bytes
codecs.decode('ab','rot13')     CPython: 'no'               SharpPy: TypeError: decode() argument must be bytes, not str
'ab'.encode('rot13')            CPython: 'rot13' is not a text encoding; use codecs.encode() to handle arbitrary codecs
                                SharpPy: unknown encoding: rot13
codecs.lookup('LATIN1').name    CPython: 'iso8859-1'        SharpPy: LookupError
codecs.lookup('utf_16_le').name CPython: 'utf-16-le'        SharpPy: LookupError
```

`codecs` 라이브러리 자체가 등록부를 전제하므로, 이건 코덱 알고리즘 버그가 아니라 **누락된 인프라**였다.

## 만든 것

### `core/PyCodecRegistry.cs` (신규)

- `Normalize` — CPython `_Py_normalize_encoding` 그대로: 영문자와 숫자, `.`는 소문자로 보존,
  그 밖의 문자 **연속**은 `_` 하나, 앞뒤 구분자 없음. 이게 등록부로 들어가는 키다.
  `latin1`/`LATIN1`/`latin-1`/`iso_8859-1` → `latin1`, `utf_16_le` → `utf_16_le`.
  측정으로 확정한 점: 등록한 search function에는 **원 철자가 아니라 정규화된 이름**이 전달된다.
- `Register` / `Unregister` — callable 검사(`argument must be callable`), 등록 순서대로 search,
  `unregister`는 identity로 전부 제거 후 **cache 전체** 폐기(`Python/codecs.c:56`와 같이).
- `Lookup` — 정규화 → cache → builtin → 등록 함수 → `unknown encoding: <호출자 원 철자>`.
  실패 메시지가 정규형이 아니라 호출자가 쓴 철자라는 점을 고정.
- `BuiltinSearch` — 별칭(아래) → 정규 이름 → 구현된 코덱이면 `CodecInfo` 구성, 없으면 미해결.
- `BuildCodecInfo` — `codecs.CodecInfo`를 **Python에서 호출**해 만든다. `CodecInfo`는
  `tuple` 서브클래스라 native 모방 객체로는 `isinstance(ci, tuple)`를 만족할 수 없어서다.
  `name`, `_is_text_encoding`, `incremental*`/`stream*` 슬롯을 kwargs로 넣는다.
- `EncodeAny`/`DecodeAny` (`_codecs.encode`/`decode` = `codecs.encode`/`decode`) — text 검사 **없음**.
  CPython에서 두 함수는 임의 코덱 진입점이고, `_codecsmodule.c`가 코덱 함수의
  `(result, consumed)` 중 `result`만 돌려준다. `codecs.encode(b'ab','hex') == b'6162'`가 성립하는 이유.
- `EncodeText`/`DecodeText`/`RefuseNonTextEncoding` — text 경로(`str.encode`, `bytes.decode`,
  `str(bytes, enc)`)용 문지기. non-text 코덱이면 CPython 메시지를 내고, 등록부에 아예 없는 이름이면
  호출자가 자기 메시지를 쓰도록 조용히 물러난다.

### 별칭 표

CPython `Lib/encodings/aliases.py`(326항)에서 **생성**해 넣었다(`.artifacts/tmp/gen_aliases.py`).
손으로 나열하지 않은 이유는 양쪽 다 고쳐지기 때문이다: `latin1`은 정규형 `iso8859-1`을 답하고,
CPython에 별칭이 없는 `koi8`는 정답이 LookupError가 된다. `utf-8-sig`만 예외로 두 행을 직접 뒀다 —
CPython은 그 이름을 별칭으로 갖지 않고 정규화된 이름으로 모듈을 직접 임포트하기 때문이다.

### non-text 코덱

`rot-13`, `hex`, `base64`를 실제 코덱으로 등록(`_is_text_encoding=False`).
`hex`/`base64`는 CPython과 똑같이 `binascii`를 부르고, `binascii`는 새로 만든
`core/PyBinascii.cs`가 `hexlify`/`unhexlify`/`b2a_base64`/`a2b_base64`를 제공한다
(`Lib/encodings/hex_codec.py`, `base64_codec.py`가 이 모듈을 import한다).
CPython의 두 가지 다른 거부 문구도 측정해 분리했다:

```text
binascii.hexlify('x')   -> TypeError: a bytes-like object is required, not 'str'
binascii.b2a_base64('x')-> TypeError: expected bytes-like object, not str
```

### 사라진 일곱 번째 디코더 사본

`modules/_CodecsModule.cs`의 `*_decode` 진입점 일곱 개가 각자 `Encoding.GetString`을 불러 있었다.
그 사본들은 약한 쪽이었다: `utf_16_decode`는 BOM을 결과에 남겼고(`codecs.getdecoder('utf-16')`로
인코딩한 값을 되돌리면 `'\ufeffab'`가 됐다),잘못된 바이트는 error handler 대신 대체 문자로 삼았다.
전부 `Decoded()` 헬퍼로 모아 유일한 디코더 `PyStringNewArguments.DecodeBytes`에 넘긴다.
측정: `codecs.getdecoder('utf-16')(b'\xff\xfea\x00') == ('a', 4)` — 마크를 소모한다.

## 코드량

```text
core/PyCodecRegistry.cs   +431   (신규: 정규화·등록부·CodecInfo·gate·코덱 정의)
core/PyBinascii.cs        +168   (신규: hex/base64 원시 4개)
modules/_CodecsModule.cs   ~156  (lookup/register/unregister/encode/decode 재배선,
                                  *_decode 사본 삭제, arity 메시지)
type/PyString.cs            +25  (Encode default → registry, ResolveCodec 미확인 → gate)
type/PyBytes.cs              +3  (bytes.decode → gate)
core/PyModule.cs             +6  (binascii 등록)
```

## 게이트

- 빌드: 오류 0
- `tools/run_bughunt.py`: **78/78 matching** (p72 encode strict 유지, p73 신규 116 케이스 diff 0)
- `tools/evaluate.py`: `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}`
- Godot 4.7.2 mono replay (Release DLL): **82 matched, 0 other**
- perf: `str_fmt +8.1%`, 나머지는 노이즈 대. 코덱 경로는 벤치마크가 건드리지 않고,
  핫 경로(`str.encode` 네이티브 코덱 분기)는 그대로 `PyString.Encode`로 간다.

## 남은 것 (docs/known_gaps.md → P1 codec registry follow-ups)

1. `CodecInfo`의 repr/tuple 서브클래스 지위. SharpPy는 tuple 서브클래스의 `__new__`/`__repr__`를
   honoring하지 않아 repr이 평범한 tuple이다. 코덱이 아니라 interpreter의 문제.
2. 코덱별 `StreamReader`/`StreamWriter`/`Incremental*` 클래스가 없어 `codecs`의 base 클래스를 넣는다.
   생성은 되고 이름은 같지만 module이 다르고 base `IncrementalEncoder.encode`는 NotImplementedError.
3. 미구현 코덱 family(cp1252/cp1251/big5/shift_jis/koi8-r/iso8859-*/mac_roman/unicode_escape/
   raw_unicode_escape/utf_7/punycode/idna/quopri/uu/zlib/bz2). charmap 계열은
   `ResolveCodec`이 코드 페이지를 아는데 엔트리 함수가 없어 surface로 나오지 않는다.
4. `binascii.Error` 전용 타입(지금은 `ValueError`로 표시, 속성도 없음).
5. `_codecs` 함수 표면 36 vs 45(`utf_7_*`, `mbcs_*`, `code_page_*`, `oem_*`, `readbuffer_encode`),
   `dir(codecs)` 81 vs 90 — 3번과 같은 원인.
