# 숫자 subclass 디스패치와 "연산당 구현 하나" (2026-10-30, 밤)

`abs(FSub(2.5))` 하나로 시작해서 같은 계열의 근본 원인을 여럿 닫았다. 전부 측정 우선으로 확정했다.

## 1. `abs` 구현이 네 개였다

| 위치 | 상태 |
|---|---|
| `core/PyBuiltin.cs CallAbs` | p52에서 고쳤지만 **builtins.abs는 여길 안 쓴다** |
| `modules/BuiltinsModule.cs Abs` | 실제 배선. 정확 CLR 타입 검사 + `PyClassInstance` 수작업 |
| `modules/_OperatorModule.cs Abs` | `__abs__` 디스패치 자체가 없음 |
| `modules/MathModule.cs CallAbs` | **CPython에 `math.abs`는 없다** (fabs는 있음) |

하나로 합쳤다(`PyBuiltinFunction.Absolute`). 측정한 의미:

- `__abs__`를 **타입에서** 찾고(instance 속성은 무관, 측정), 있으면 결과를 **그대로** 반환한다.
  `class A: __abs__ → NotImplemented` 일 때 CPython은 `abs(A()) is NotImplemented`이다 — 오류 아님.
  내 p52 구현은 여기서 NotImplemented를 걸러 TypeError를 냈고, 그건 오답이었다.
- `__abs__`가 없을 때만 `nb_absolute` 경로 → `bad operand type for abs(): 'str'`
- subclass는 상속 슬롯이 값을 읽고 결과는 base 타입(`type(abs(ISub(-5))).__name__ == 'int'`)
- 예외는 전파, 숫자가 아닌 반환도 그대로 통과, subclass의 user `__abs__`가 base 슬롯보다 우선

## 2. CPython이 published하는 숫자 슬롯이 SharpPy에 적었다

`dir()` 측정 기준 결측: `int`에 `__index__` `__trunc__` `__floor__` `__ceil__` `__int__` `__float__`
`__rdivmod__`, `float`에 `__add__` `__sub__` `__mul__` `__truediv__` `__floordiv__` `__mod__`
`__pow__`. 전부 `TryLongSelf`/`TryFloatSelf`(= CPython의 `PyLong_Check`/`PyFloat_Check` 대응)로
등록했으므로 subclass 인스턴스에도 안전하다.

## 3. MRO 한 프레임에서 우선순위가 뒤집혀 있었다

`GenericGetAttribute`가 프레임별로 **`TypeDict`를 `ClassDict`보다 먼저** 보고 있었다. `int`에
`__index__`를 등록한 순간부터 `class IO(int)`가 스스로 정의한 `__index__`가 가려져
`i.__index__()`가 3 대신 5를 반환했다. 같은 프레임에서는 클래스 자기 namespace(ClassDict)가
상속/머지된 view(TypeDict)보다 우선이어야 한다 — `__str__`/`__repr__` 특수 케이스는 이미 그 규칙을
문서화하고 있었고 일반 경로만 어기고 있었다.

## 4. `PyNumber_Index` 도 세 개였다

`bin`/`hex`/`oct`(BuiltinsModule 3곳)와 `operator.index`가 각자 `GetAttribute("__index__")`로
풀었다. 측정한 진짜 규칙:

```python
class IO(int):
    def __index__(self): return 3
i = IO(5)
i.__index__()          # 3   (Python 메서드)
int.__index__(i)       # 5   (long의 C 슬롯 = 값)
operator.index(i)      # 5   ← dunder가 아니라 값을 쓴다
bin(i)                 # '0b101'
```

즉 **int subclass는 `nb_index`를 long의 값 슬롯으로 유지**한다. SharpPy는 dunder를 따라
`operator.index`가 3, `bin`이 `'0b11'`이었다. 반면 순수 user 클래스는 `__index__`가 존중된다.
`PyObject.IndexOrThrow`로 단일화하고 실패 문구는 CPython 그대로
(`'FO' object cannot be interpreted as an integer` —SharpPy는 AttributeError가 새었다).
`operator.abs`/`operator.index`의 arity 문구도 측정대로 `_operator.abs() takes exactly one argument (0 given)` 형태로 고쳤다.

## 5. `%` 서식이 숫자 subclass를 거부했다

`%d`/`%i`/`%o`/`%x`/`%f` 계열이 정확 CLR 타입만 봤다. CPython은 `PyLong_Check`/`PyFloat_Check`라
subclass를 받는다: `"%d" % ISub(5)` → `'5'`, `"%.3f" % FSub(2.5)` → `'2.500'`. central helper로 교체.

## 검증

- 신규 probe `tests/bughunt/p57_numeric_dispatch.py` 41케이스 CPython 대비 stdout diff **0**
  (슬롯은 repr 라벨이 아니라 동작으로 비교 — CPython은 `int.__index__`를 slot wrapper,
  `int.__floor__`를 method라고 부른다)
- `tools/run_bughunt.py` **61/61**, `tools/evaluate.py` 225/3/2 불변
- Godot 4.7.2 mono replay **82 matched, 0 other**

## 같은 계열에서 남은 것 (측정돼 있고 ledger에 기록)

1. `divmod(FSub, 2)` / `divmod(2, FSub)` — wrapper 타입이 CLR `DivMod`를 오버라이드하지 않아 dunder에 닿지 않는다
2. `math.floor/ceil/trunc(FSub)` — user dunder가 우선이어야 하는데(`'user-floor'`) 타입 검사에서 죽는다
3. `int(FSub)`가 user `__int__`를 무시(77 대신 2), `float(FSub)`가 user `__float__`를 무시(9.5 대신 2.7)
4. 산술 user dunder 우선순위 — `class F(float): __add__` 가 무시되고 4.5가 나온다
5. `float.__int__`/`float.__float__` 슬롯 미등록
