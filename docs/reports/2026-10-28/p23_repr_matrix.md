# p23 — callable repr matrix (2026-10-28)

## 목표
호출 가능한 객체(builtin function / method_descriptor / slot wrapper / bound
method / user function·method / lazy iterator)의 `repr()`을 CPython 3.12.15와
일치시킨다. `repr(len)`이 `<builtin_function_or_method object at 0x..>`로 나오는
것이 출발점이었다.

## CPython 형식 (Objects/descrobject.c, funcobject.c, classobject.c 측정 + 소스)
- module-level builtin: `<built-in function NAME>`
- bound builtin method: `<built-in method NAME of TYPE object at 0xID>`  (ID = 바인딩된 self)
- method_descriptor(비바인드): `<method 'NAME' of 'TYPE' objects>`
- wrapper_descriptor(slot, 비바인드): `<slot wrapper 'NAME' of 'TYPE' objects>`
- method-wrapper(bound slot): `<method-wrapper 'NAME' of TYPE object at 0xID>`
- user function: `<function QUALNAME at 0xID>`
- bound method / classmethod: `<bound method QUALNAME of repr(self)>`
- `len.__class__`/`__name__`/`__qualname__`/`__self__` 등 object/함수 속성 노출

## C#/CLR 관찰 및 수정
- `repr()`는 C# `ToString()`이 아니라 `ToRepr()`(PyString)를 쓴다. `PyBuiltinFunction`
  는 `ToString()`만 있고 `ToRepr()`가 없어 기본 object repr로 빠지고 있었다.
- `PyBuiltinFunction.GetAttribute`가 `__name__`/`__call__` 외 **전부 throw**해서
  `len.__class__`가 `AttributeError`였다(모든 object는 `__class__`를 가져야 함).
  → 미지 이름은 `base.GetAttribute`로 fallback, `__qualname__` 추가.

수정 파일:
- `core/PyBuiltin.cs`
  - `PyBuiltinFunction`: `ToRepr`/`ToStr` → `<built-in function NAME>`;
    `GetAttribute` base fallback + `__qualname__`; isinstance 테이블의 잔존
    `Console.WriteLine` 디버그 제거.
- `core/PyFunction.cs`
  - `ReprName`(= `BindErrorName(CodeObject.DisplayQualName)`) 도입.
  - `PyFunction.ToRepr` → `<function QUALNAME at 0x..>` (이전: bare name).
  - `PyBoundMethod.ToRepr` → `<bound method QUALNAME of repr(self)>`.
  - `PyBuiltinBoundMethod.ToRepr` → `<built-in method NAME of TYPE object at 0xID>`.
- `core/PyDescriptor.cs`
  - `PyMethodDescriptor.ToRepr` → `<method 'NAME' of 'TYPE' objects>`.
  - `PyNewMethodDescriptor.ToRepr` → `<built-in function __new__>` (`<method...>` 회피).
  - `PyBoundMethodDescriptor.ToRepr` → `<built-in method NAME of TYPE object at 0xID>`.
  - `PyWrapperDescriptor.ToRepr` → `<slot wrapper 'NAME' of 'TYPE' objects>`.
  - `PyBoundWrapperDescriptor.ToRepr` → `<method-wrapper 'NAME' of TYPE object at 0xID>`.
- `type/PyStringMethod.cs`
  - bound str method 주소를 `{hash:x8}` → `0x{_instance.GetHashCode():x}`.
  - `GetAttribute`에 `__self__`/`__name__`/`__qualname__`/`__objclass__`/`__call__`.
- `type/PyIterator.cs`
  - `PyEnumerateIterator.ToRepr` → `<enumerate object at 0x..>`.

## 측정
- repr 매트릭스(`.artifacts/tmp/reprmat.py`, 주소 정규화) diff: 21줄 → **10줄**.
- `tests/bughunt/p23_repr_matrix.py`(주소 정규화, 일치 카테고리만): stdout diff 0.
- `tools/run_bughunt.py`: **30/30 matching**.
- `tools/evaluate.py`: **225 + 3 + 2** (불변).
- Godot 4.7.2 replay: **82 matched, 0 other**.
- perf: list_comp 첫 측정 +84.6% 스파이크 → 재측정 +0.2/-1.1/-2.1%, str_fmt
  +3.7~9.4%로 노이즈 밴드 회귀. repr은 hot path(repr()/속성 접근 시에만)라 중립.

## 남은 한계 (측정 기록, p24 후보)
- **`super()`**: SharpPy는 `type(super(D,D()))`가 `object`, repr `<object object at 0x..>`.
  CPython은 `<class 'super'>` + `<super: <class 'D'>, <D object>>`. 전용 `super` 타입 필요.
- **iterator 타입명**: `type(iter([]))`=`iterator`(CPython `list_iterator`),
  `type(reversed([]))`=`reversed`(CPython `list_reverseiterator`). source별 iterator
  클래스/이름 필요. (`enumerate` repr은 고침.)
- **slot wrapper vs method_descriptor 분류**: SharpPy가 `list.__len__`을 method_descriptor로
  저장해 `<method '__len__' of 'list' objects>`로_repr(CPython `<slot wrapper...>`).
  `object.__eq__`도 owner 없는 wrapper_descriptor로 `<wrapper_descriptor object at>`.
  CPython slotdefs 분류와对齐 필요.
- **method-wrapper 이름 바인딩**: `len.__repr__`이 `<method-wrapper object at 0x..>`
  (CPython `<method-wrapper '__repr__' of builtin_function_or_method object at 0x..>`).
- **type getattr 에러 메시지**: `int.__len__` → SharpPy `'type' object has no attribute
  '__len__'` vs CPython `type object 'int' has no attribute '__len__'`.
