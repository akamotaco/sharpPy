# p14 속성/반복 프로토콜·complex·슬라이스 (2026-10-28)

`tests/bughunt/p14_attr_iter_numeric.py` 추가 → bughunt **21/21 matching**.

## 확인된 정상 동작 (probe 통과)

`__getattr__`/`__getattribute__`/`__setattr__` 차단, property 클래스 접근,
classmethod 상속, staticmethod, 명시적 `super(C, self)`, `__iter__` 우선,
old-style `__getitem__` 반복, generator return→StopIteration.value,
`tuple +=`/`list +=` identity, enumerate/reversed 계열 — 기존 코드와 일치.

## 수정

1. **complex pow 정수 지수** (`type/PyComplex.cs PowerIntegral`):
   CPython `complex_pow`는 |지수|≤100 정수에서 `c_powi`(반복 곱셈)로 정확 계산.
   `(1j)**2`가 exp/log 경로 due에 `-1+1.22e-16j`였던 것이 `-1+0j`.
2. **complex 포맷** (`FormatComplex`, `TypeDict["__format__"]`):
   - 타입 문자 없음만 repr 스타일(`r`/`R`도 `Unknown format code ... 'complex'`),
     `eEfFgG`만 float 경로, `%`/`n`/`d` 거부
   - repr 스타트: 정수부 `.0` 생략, 실수 +0이 아니면 괄호, `#` alternate 트레일링 닷
   - `format(1+2j, ">10")` → `'    (1+2j)'`, `" "` → `'( 1+2j)'`
3. **complex/float repr 부분 서식** (`FormatNumber`/`FormatImaginary`):
   부분마다 최단 float repr + `.0` 제거 → `1E-05`→`1e-05`,
   `100000000000000000000`→`1e+20`. `repr(1-0j)` 부호비트 `-0j`.
4. **float()/int() 변환 메시지**: `... or a real number, not 'X'`
   (`or a number` 전역 치환: PyObject/PyDict/PyEllipsis/PyInt/PyList/PyNone/PyTuple/PyFloat)
5. **round(non-float)**: `type complex doesn't define __round__ method` (따옴표 없음)
6. **list 슬라이스 대입 non-iterable**: `must assign iterable to extended slice`
   (반복 중 사용자 예외는 그대로 전파, 순수 non-iterable만 메시지 치환)

## 검증

- `.artifacts/tmp/cx.py` 26케이스, p14 프로브 stdout CPython과 diff 0.
- `tools/run_bughunt.py` 21/21, `tools/evaluate.py` 225 + 3 + 2.
- perf probe reps4: 최대 +5.7%(comp_lambda, 노이즈 대역).
