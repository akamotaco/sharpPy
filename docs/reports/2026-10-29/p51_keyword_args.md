# p51 — P0-20: builtin 키워드 인자 수용을 CPython의 METH_KEYWORDS 표에 맞춤 (2026-10-29)

## 시작 실패 사례
```python
(1).bit_length(x=1)     # CPython: TypeError: int.bit_length() takes no keyword arguments
                        # SharpPy: 1        <- 조용히 값을 반환
(1.5).is_integer(x=1)   # CPython: TypeError: float.is_integer() takes no keyword arguments
                        # SharpPy: False
'a'.upper(x=1)          # CPython: TypeError: str.upper() takes no keyword arguments
                        # SharpPy: 'A'
```
메시지 문제가 아니라 **조용히 틀린 답**이다. 선행 시도(게이트만 추가)는 probe 55개 중 9개를 깨뜨려 되돌렸고 (`08abf9a`), 그 되돌림이 남긴 측정 목록을 근거로 이번에는 감사(audit) 기반으로 진행했다.

## C와 C#/CLR의 차이
CPython은 각 builtin을 `METH_O` / `METH_VARARGS` / `METH_FASTCALL` (+ `METH_KEYWORDS`) 로 선언한다. `PyArg_ParseTuple*` 계열은 `kwd`가 들어오면 즉시
`{qualname}() takes no keyword arguments`를 내고, `METH_KEYWORDS`인 것만 `PyArg_ParseTupleAndKeywords`의 kwdef 표로 키워드를 named parameter 슬롯에 해결한다.
CLR에는 이 구분이 없다. SharpPy `PyMethodDescriptor`는 `acceptsKwargs` 파라미터를 받아 `_acceptsKwargs`에 **저장만 하고 아무 데서도 읽지 않았다** — 죽은 필드 하나가 곧 결함의 정체였다.

## 수정
1. **거부 게이트** — `PyMethodDescriptor.Call` / `CallWithReceiver` / `PyBoundMethodDescriptor.Call`과 `PyStringMethod.Call`에 추가. 텍스트는 `{Type}.{name}() takes no keyword arguments`.
2. **`__new__` 예외** — CPython은 `__new__`를 type-call 기계에서 다른 라벨로 답한다 (`A() takes no arguments`, `tuple() takes no keyword arguments`). 범용 텍스트가 틀리므로 제외하고 P0-21로 등록.
3. **허용 플래그(측정된 것만)** — `dict.update`, `str.format`, `str.format_map`, `str.split`, `str.rsplit`, `str.encode`, `str.expandtabs`, `str.splitlines`, `bytes.decode`, `bytes.splitlines`, `int.to_bytes`, `memoryview.cast`.
4. **키워드 프로젝션** — delegate가 positional 배열만 읽으므로 `PyObject.KwValue` / `PyString.ProjectKeywordSlots`로 슬롯에 투영. 플래그만 붙이면 `to_bytes(length=2, byteorder='little')`가 `expected 1 to 2 arguments, got 0`이 되고 `splitlines(keepends=True)`가 플래그를 조용히 무시하는 것이 확인됐다.
5. **`PyDict.GetItem`은 없는 키에서 KeyError를 낸다** — 부재 키워드를 "없음"으로 다루려면 존재 확인이 먼저다. 안 하면 `KeyError 'length'`가 된다.
6. **`int.to_bytes`를 CPython 시그니처에 맞춤** — `(length=1, byteorder='big', /, signed=False)`. 측정으로 기본값이 length=1임을 확인("minimal"이 아니다): `(5).to_bytes()` → `b'\x05'`, `(70000).to_bytes()` → `OverflowError: int too big to convert`, `(-1).to_bytes()` → `OverflowError: can't convert negative int to unsigned`, `(-1).to_bytes(1, signed=True)` → `b'\xff'`, `(255).to_bytes(1,'big',signed=True)` → `OverflowError`.

## 감사만 알아낸 두 가지
- **`PyString.GetAttribute`가 타입을 가리고 있었다.** switch가 upper/lower/title/strip/lstrip/rstrip/replace/split/join/find/count/encode/isidentifier를 인스턴스 closure로 돌려줬는데, 13개 **전부** `strType.TypeDict`에 descriptor로 등록돼 있다. 가림 때문에 키워드 수용·arity 메시지·descriptor 동일성이 중복 경로에서 결정됐다. **switch를 삭제**하고 `base.GetAttribute`(MRO+descriptor)로 일원화했다. 바인딩 형태는 그대로 CPython과 같다:
  `type('a'.upper).__name__ == 'builtin_function_or_method'`, `<built-in method upper of str object at 0x...>`.
- **그 가림이 틀린 구현 둘을 숨기고 있었다.** descriptor가 권위가 되자 즉시 드러남:
  - `strType.TypeDict['title']`은 `TextInfo.ToTitleCase`(.NET 단어 경계)를 써서 `It's A Test-Case 2Nd`. CPython은 `It'S A Test-Case 2Nd`. → CPython식 instance 구현으로 위임.
  - `PyString.StrEmptySpan`은 역방향 구간에서 음수를 반환. CPython `"abc".count("", 3, 1)`은 0. → `Math.Max(0, ...)`.

## 검증
- 신규 probe `tests/bughunt/p51_keyword_args.py` — 30줄, stdout diff **0** (거부 13 + 허용 14 + 바인딩 확인 2)
- 키워드 매트릭스 2종 (`kw3`, `kw4`) — 각각 diff **0**
- 전 probe 드라이버 (56개 파일, 라인 단위 diff) — **MISMATCH 0**
- `tools/run_bughunt.py` — **55/55 matching**
- `tools/evaluate.py` — `{"matching_output": 225, "matching_except_volatile_fields": 3, "self_test_no_reference": 2}` 불변
- Godot 4.7.2 mono replay (Release DLL) — **REPLAY_RESULT 82 matched, 0 other**

## 성능 (측정값, 추측 아님)
str 속성 조회가 switch → MRO descriptor 조회로 바뀌었으므로 hot path 영향을 A/B로 확인했다. 1.6M 조회/400k 반복 워크로드, 이전(HEAD worktree)과 이후 바이너리를 교대로 실행:

| 바이너리 | 반복 값 (s) | 중앙값 |
|---|---|---|
| 이전 | 3.030 / 2.959 / 2.976 / 2.988 / 2.957 / 2.990 | ~2.989 |
| 이후 | 3.087 / 3.108 / 2.983 / 3.055 / 3.079 / 3.081 / 3.043 | ~3.062 |

→ 약 **+2.4%**. `perf_probe` 공식 스위트에서는 플래그 없음 (`str_fmt +1.0%`, `list_comp +4.3%`, 나머지 노이즈 대). 정확성 우선 원칙에 따라 착지하되 비용을 기록한다. 후속 최적화 후보: 접근마다 MRO를 걷지 말고 descriptor 조회를 캐시.

## 새로 등록된 잔여
- **P0-21** `__new__` 키워드 오류가 CPython의 type-call 라벨을 쓴다 (`A() takes no arguments` / `tuple() takes no keyword arguments`). SharpPy는 현재 범용 descriptor 텍스트.
- **P0-22** `int.to_bytes`가 `(long)` 캐스트라 64비트 초과 배율에서 용량 규칙이 검증되지 않는다 (BigInteger 경로 필요).

## 장부 정정
이전 초안은 `str.split`/`rsplit`/`encode`/`expandtabs`가 키워드를 **거부**한다고 적었다. 측정 결과 **허용**한다. 원인은 interpreter가 아니라 diff를 읽은 것 — 장부에 그대로 정정했다.
