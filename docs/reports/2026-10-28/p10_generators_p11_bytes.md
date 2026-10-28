# p10 generators / p11 bytes·slice 클러스터 (2026-10-28)

bughunt 프로브 2종 추가 후 전체 **18/18 matching**.

## 새 프로브

- `tests/bughunt/p10_generators.py`: next/send/throw/close, GeneratorExit,
  finally 실행, yield from (값 회수·빈 iter·전파), coroutine 비반환,
  무한 제너레이터, 보낸값 누적. 작성 중 발견한 프로브 자체 버그
  (구식 3-인자 throw, 미대기 coroutine warning, send 교대 순서 오류)는
  CPython 기준 동작에 맞춰 수정.
- `tests/bughunt/p11_bytes_slices.py`: bytes/bytearray 연산·메서드, UTF-8
  디코드 실패, bytes %, slice 대입/삭제(step 포함), slice.indices,
  커스텀 `__getitem__`, memoryview 기본면.

## 수정

- **UTF-8/ASCII strict 디코드** (`type/PyBytes.cs DecodeUtf8Python/DecodeAsciiPython`):
  .NET ReplacementFallback이 삼키던 잘못된 바이트 시퀀스를 CPython
  `unicode_decode_utf8` 규칙으로 직접 검증.
  - start byte 거부(`invalid start byte`), 시작 바이트 위치로 보고되는
    `invalid continuation byte`(E0/ED/F0/F4 범위 가드 포함),
    잘림은 `byte ...: unexpected end of data` /
    `bytes in position a-b: unexpected end of data`
  - errors= strict/replace/ignore/surrogateescape/backslashreplace,
    모르는 핸들러는 `unknown error handler name 'x'` (LookupError)
  - ascii: `ordinal not in range(128)`
- **bytes % 서식** (`PyBytes.Modulo`): bytesobject.c bytes_format 부분 구현.
  %s→%b 변환 메시지 고정(`%b requires a bytes-like object...`), %c
  range(256)/OverflowError, %d/%i/%u/%o/%x/%X/%e/%E/%f/%g, flags·width·
  precision, `not enough arguments for format string`,
  `not all arguments converted during bytes formatting`,
  `unsupported format character 'z' (0x7a) at index 1`, bytes-키 dict 명명형.
- **bytes()/bytes 하위클래스 생성** (`PyBuiltin.CallBytes`,
  `PyType.CreateInstance/DerivesFromType`, `PyClass.Call`):
  str 인자에 encoding 없이 → `string argument without an encoding`
  (exact bytes와 bytes 파생형 모두). bytes(iterable)은 일반 이터레이터/딕셔너리
  수용(`cannot convert 'X' object to bytes`).
- **bytes+str concat 메시지**: `can't concat str to bytes` (형식 순서).
- **bytearray.__setitem__** descriptor + `ToByteArrayCopy()`.
- **memoryview**: `tobytes`/`tolist`/`hex`/`__bytes__` descriptor.
- **generator gi_ 속성** (`type/PyGenerator.cs`): gi_name/gi_code/gi_running/
  gi_frame(고갈 시 None)/gi_yieldfrom. gi_frame은 내부 PyFrame 노출
  (frame repr는 아직 CPython 형식과 다름 — probe는 None 여부만 검증).

## 검증

- `tools/run_bughunt.py`: 18/18 (p10 2→0, p11 7→0).
- `tools/evaluate.py`: 225 + 3 + 2 유지 (enum/re 회귀 없음).
- perf probe reps4: 최대 +5.9% (노이즈 범위).

## 알려진 잔여

- frame 객체 repr/속성 완전 패리티(f_code.co_* 등)는 별도 작업.
- memoryview 다차원/c-format, bytes형식 `#` alternate 상세.
- generator gi_yieldfrom은 실제 위임 추적 없음(상시 None).
