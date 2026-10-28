# p24 — `super()` type / repr / members + inheritance parity (2026-10-28)

## 목표
`super` 객체의 타입·repr·멤버와, super를 통한 **상속 결과(MRO resolution)** 를
CPython 3.12.15와 동일하게 만든다.

## CPython 사실 (Objects/typeobject.c: superobject 통합, 3.12)
- `PySuper_Type`: name `"super"`, base `object`, custom `tp_getattro=super_getattro`,
  `tp_repr=super_repr`, `tp_descr_get=super_descr_get`, `tp_members=super_members`.
- 필드: `type`(__thisclass__), `obj`(__self__, 가용 None), `obj_type`(__self_class__, 가용 NULL).
- `super_repr`:
  - obj_type != NULL → `<super: <class 'TYPE'>, <OBJTYPE object>>`
  - obj_type == NULL → `<super: <class 'TYPE'>, NULL>`
- `super_getattro`:
  - `__class__` → GenericGetAttr(super 클래스)  ※ su->obj 클래스 아님
  - else `do_super_lookup`: obj_type 없으면 generic getattr로, 있으면
    `_super_lookup_descr`(obj_type.__mro__에서 type 인덱스 찾아 **skip**, 이후 클래스 dict 검색)
    - 찾았고 method out-param + Py_TPFLAGS_METHOD_DESCRIPTOR → self를 넘기는 방식
    - 그 외 → `tp_descr_get(obj or None, obj_type)` (staticmethod은 obj 무시, classmethod는 클래스 bind)
    - 못 찾으면 → GenericGetAttr(super) → tp_members(__thisclass__/__self__/__self_class__)
- `super_vectorcall`/`super_init`: `super()`(0: __class__ cell + 첫 local), `super(type)`,
  `super(type, obj)`. arg1 타입 검사 `super() argument 1 must be a type, not X`,
  positional `super() expected at most 2 arguments, got N`, kwargs 거부 `super() takes no keyword arguments`.
- `supercheck`: obj가 type이면 부분류 검사(classmethod), instance면 Py_TYPE, proxy면 __class__.

## C# 관찰
- `PySuper`는 이미 `SuperCheck`/`LookupInMRO`/descriptor protocol을 CPython과 동일하게
  구현 → **기능적 MRO resolution은 이미 정확** (diamond/init 순서, deep, classmethod super,
  property·data, `super(CBase,CBase).who`→AttributeError 전부 일치).
- 결함은 표현·속성·인자, 그리고 **LOAD_SUPER_ATTR의 실제 버그** 하나:
  1. `type(super(...))` = `object`, repr = `<object object at>`,
     `__thisclass__`/`__self__`/`__self_class__` AttributeError.
  2. `super(type)` 1-arg unbound 미지원, arg 에러 메시지 불일치, kwargs가 0-arg로 새듯 함.
  3. **버그**: `super().static_method()`가 self를 인자로 받음
     (`CBase.swho() takes 0 positional arguments but 1 was given`).

## LOAD_SUPER_ATTR 버그 진단 (C ↔ C# 차이)
`super().m()`은 CPython 3.12처럼 `LOAD_SUPER_ATTR` opcode로 컴파일된다(PySuper.GetAttribute 아님).
`LOAD_SUPER_ATTR`은 `superProxy.GetAttribute(name)` 결과를 받은 뒤 instance-mode에서
`superAttr is PyFunction`이면 self에 bind했다. 그런데:
- **regular method**는 PySuper.ApplyDescriptorProtocol이 이미 `PyMethod(self, func)`로 bound 반환
- **staticmethod**는 `staticmethod.__get__`가 plain `PyFunction`을 반환(CPython: method descriptor
  플래그 아님 → self를 넘기지 않음)

즉 bare PyFunction은 "bind하지 말라"는 뜻인데도 LOAD_SUPER_ATTR가 bind → self가 추가 인자.
`getattr(super(),'swho')()`은 정상, `super().swho()`만 실패로 확인.

## 구현
- `core/PyType.cs`: `SuperType = new PyType("super", [ObjectType])`.
- `core/PyBuiltin.cs` `PySuper`:
  - `GetPyType`→SuperType, `GetTypeName`→"super", `ToRepr`→CPython super_repr.
  - `GetAttribute`: MRO 검색 실패 시 generic-getattr 폴백으로 `__thisclass__`/`__self__`/
    `__self_class__` 반환(미 bound면 None). `__class__`는 super 클래스.
- `core/PyBuiltin.cs` `CallSuper`:
  - kwargs 거부 `super() takes no keyword arguments`.
  - `super(type)` 1-arg 지원(obj_type NULL).
  - `super(type, None)` → unbound.
  - 메시지: `super() argument 1 must be a type, not X`, `super() expected at most 2 arguments, got N`.
- `runtime/PyVM.cs` `LOAD_SUPER_ATTR`: instance-mode에서 bare `PyFunction`을 더 이상 self에
  bind하지 않음(regular method는 이미 PyMethod로 bound, bare function은 staticmethod/비-메서드).

## 측정
- super 매트릭스 + `tests/bughunt/p24_super.py`(주소 정규화): stdout diff 0.
- 상속 결과 일치 확인: `diamond.m-order`=['TopInit','LInit','RInit','BaseInit','Top','L','R','Base'],
  deep `E0.g+D0.g+C0.g`, classmethod super `CSub+CBase`, prop/val, object.__init__ passthrough.
- `tools/run_bughunt.py`: **31/31 matching**.
- `tools/evaluate.py`: **225 + 3 + 2** (불변).
- Godot 4.7.2 replay: **82 matched, 0 other**.
- perf: 노이즈 밴드(int_loop +1.3~1.6%, str_fmt +5.2~5.6%, list_comp +0.9~1.3%, call_heavy +5.3~6.4%), REGRESSION 없음.

## 남은 한계
- `LOAD_SUPER_ATTR`의 `PyBuiltinFunction`/`PyBuiltinMethod` bind 분기는 이번 staticmethod
  케이스와 무관해 그대로 뒀다(발생 경로 미측정). 필요 시 CPython method-descriptor 플래그 기준으로
  재검토.
- `super`의 `tp_descr_get`(super 인스턴스를 클래스 어트리뷰트로 두 바인딩)와 strict-subclass
  `super(SuperSub, ...)` 경로, zero-arg `super()`의 `arg[0] deleted`/`bad __class__ cell` 세부는
  미측정 유지.
