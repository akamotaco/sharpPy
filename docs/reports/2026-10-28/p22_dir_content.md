# p22 — `dir()` content-set parity (2026-10-28)

## 목표
CPython 3.12.15의 `dir()` **내용 집합**과 SharpPy를 일치시킨다. p19에서 정렬
순서(ordinal code point)는 고쳤지만, 내용 집합은 클래스/MRO 병합·object
프로토콜·모듈 `__dict__`가 빠져 있어 크게 어긋나 있었다.

## CPython 사실 (objects/object.c, typeobject.c 측정 + 소스)
- `_dir_object`: `__dir__()` 를 호출하고 결과를 **정렬**해서 반환.
- `type.__dir__`: `self.__dict__` + 베이스 클래스 `__dict__`를 재귀 병합,
  **metaclass는 제외** (`merge_class_dict`).
- `object.__dir__`: 인스턴스 `__dict__` + `__class__` 계층을 `merge_class_dict`로 병합.
- heap(사용자) 타입은 `__dict__`/`__weakref__`를 tp_dict에 가진다. `object` 자체는 안 가진다.
- `__qualname__`/`__type_params__`는 **metaclass `type` 소속**이라 `C.__dict__`/`dir(C)`에 없다
  (`C.__qualname__`은 접근 가능하지만 dir에는 안 보인다).

## C#/CLR 차이 (핵심 통찰)
CPython에서 object의 `__eq__`/`__hash__`/`__str__`/... 은 전부 `object.tp_dict`의
slot wrapper라서 dir/hasattr가 본다. SharpPy는 같은 동작을 **CLR virtual +
operator dispatch**(BinaryOp/CompareOp slot, `ToStr`/`ToRepr` fallback, hash cache,
ABC check)로 구현하고 `TypeDict`에 미러링하지 않는다.

### 시도 실패 → 방향 전환
object dunders를 **실제 descriptor로 TypeDict에 노출**하자:
- `object.__str__`가 base `PyObject.ToStr()`의 `__str__` attribute lookup에 걸려
  `PyList`/`PyDict` repr이 `[...]` 대신 `<list object at 0x..>`로 붕괴.
- `object.__str__`이 `ToStr()`을 호출하면 다시 `__str__` lookup → **무한 재귀**.
- `object.__eq__`/`__hash__`를 넣으면 값 비교/해시 dispatch가 object의 generic
  버전을 찾아 C# override를 **short-circuit**.

즉 functional dunder descriptor는 선행 CLR dispatch 경로를 가로채 회귀시킨다.

### 채택한 설계
- `__dir__`만 **functional descriptor**로 object.TypeDict에 노출 (operator 무관, 안전).
  → `self`가 class면 type.__dir__ 알고리즘, module이면 `ModuleDict`, 아니면
  object.__dir__(instance `__dict__` + class/base dicts).
- 나머지 object 프로토콜 이름(`__eq__`,`__ne__`,`__lt__`,`__le__`,`__gt__`,`__ge__`,
  `__hash__`,`__str__`,`__repr__`,`__sizeof__`,`__getstate__`,`__reduce__`,
  `__reduce_ex__`,`__subclasshook__`)은 **dir() 이름 집합에만 합성**
  (`AddClassDictKeys`에서 `t is ObjectType`일 때). dispatch는 그대로.
- `PyClass`의 dir에서 `__qualname__`/`__type_params__` 제외(CPython metaclass 소속).
- `builtins.dir()` 기본 경로와 ordinal 정렬 유지.

## 코드
- `core/PyType.cs`
  - `InitializeObjectTypeDescriptors`: `__dir__` descriptor만 남김. 나머지 object
    dunder descriptor 실험은 주석으로 이유 기록(회귀 근거 포함).
  - `DefaultDirNames(PyObject)`: class / module / instance 3분기로 이름 집합 계산.
  - `AddClassDictKeys`: PyClass는 ClassDict(+`__dict__`,`__weakref__`, `-__qualname__`/`__type_params__`),
    native PyType은 TypeDict keys, ObjectType이면 표준 프로토콜 이름 합성.
- `core/PyBuiltin.cs`
  - `CallDir`: `__dir__()` 호출 후 PyString 값 기준 ordinal 정렬.

## 측정 결과
- `dir(object)`, `dir(user class)`, `dir(subclass)`, `dir(instance)`,
  `dir(instance with added attrs)`, `dir(module)` **missing=0 extra=0**.
- ordinal 정렬(혼합 대/소문자/dunder) CPython과 일치.
- builtin의 object 프로토콜 멤버십(`__eq__`/`__hash__`/`__str__`/...) CPython과 일치.
- `tests/bughunt/p22_dir_content.py`: stdout diff 0.
- `tools/run_bughunt.py`: **29/29 matching**.
- `tools/evaluate.py`: **225 + 3 + 2** (불변).
- Godot 4.7.2 replay: **82 matched, 0 other**.
- perf: dir()을 호출하지 않는 워크로드라 hot path 무관. str_fmt/closure_calls가
  +28~40%로 플래그됐으나 **HEAD(stash)에서 str_fmt -27.7%↔+23.7%**로 50포인트 진동 →
  환경 노이즈로 판정(dir 변경은 이 경로들 미수행).

## 남은 한계 (측정 기록)
- **builtin concrete-method dir parity**: `dir(int)`는 `__bool__`/`__index__`/`__int__`/
  `is_integer`/`__round__`/`__ceil__`/`__floor__`/`__trunc__`/`__rpow__`/`__rdivmod__`/
  `__getnewargs__`(12) 누락, `dir(str)` 5, `dir(dict)` 4(`__len__`/`__iter__`/
  `__contains__`/`__reversed__`), `dir(list)` 1(`__reversed__`). 이는 "native TypeDict에
  slot/메서드 완전 materialize"의 별도 클러스터.
- `dir(type)` full-set: `__qualname__`/`__flags__`/`__basicsize__`/`__abstractmethods__`/
  `__instancecheck__`/`__subclasscheck__`/`__or__`/`__ror__` 등 type TypeDict materialization
  유예. `dir(Exception)`도 `add_note`/`with_traceback`/`__traceback__`/`__cause__` 등 유예.
- object 프로토콜 dunder가 **dir()에는 보이지만 `getattr`으로는 접근 불가**(설계상 의도적).
  getattr 접근 가능하게 하려면 CLR dispatch와 병존하는 descriptor 재설계가 필요.
