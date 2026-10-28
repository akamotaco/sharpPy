# p20 — isinstance/issubclass __instancecheck__/__subclasscheck__ protocol (2026-10-28)

## 동기
p19에서 남긴 결손: `isinstance(open(f), io.IOBase)` 등 **ABC virtual subclass** 검사 실패.
원인은 SharpPy가 `isinstance`에서 metaclass `__instancecheck__`를 우회한 것.

## 스코프 (`tests/bughunt/p20_instancecheck.py`)
custom `__instancecheck__`/`__subclasscheck__` 메타클래스, ABCMeta `.register()` virtual
subclass(native/user), `__subclasshook__`, abstractmethod, io.IOBase virtual, 에러 precedence.
CPython 3.12.15 기준 (Objects/abstract.c recursive_isinstance/object_issubclass, Modules/_abc.c).

## 수정 (모두 측정으로 특정)
1. **isinstance `__instancecheck__` 미호출** (`core/BuiltinsModule.cs`
   `ObjectRecursiveIsInstance`)
   - `type(cls).__instancecheck__`를 기본 MRO 검사 **전에** 호출(CPython 순서).
   - metaclass가 정확히 `type`이면 조회 생략(핫경로: `isinstance(x, int)` 등 무비용).
   - ABCMeta/custom metaclass만 훅 호출.
2. **`_abc_subclasscheck`가 native 타입 거부** (`modules/AbcModule.cs`)
   - `if (!(subclass is PyClass))` → native `PyType`도 허용. 기존엔 TypeError를
     호출자가 삼키고 MRO 폴백해서 `A.register(int)` 후 `issubclass(int, A)`/`isinstance` 실패.
3. **`__subclasshook__` 미호출** (`AbcModule.cs` `AbcSubclassCheckInternal`)
   - registry 후 `cls.__subclasshook__(subclass)` 호출: True→subclass, False→아님(확정),
     NotImplemented/없음→MRO 폴백 (CPython _abc.c check).
4. **issubclass 훅 결과 bool coercing** — `__subclasscheck__` 반환을 `PyObject_IsTrue`로
   변환(CPython). `"sc"` 반환 시 `'sc'`가 아니라 `True`.
5. **issubclass 에러 precedence** (`BuiltinsModule.cs` `IsSubclass`)
   - CPython 순서로 재구성: tuple → `__subclasscheck__`(custom metaclass) →
     arg1(derived) → arg2(cls). arg2 문구를 장문
     `issubclass() arg 2 must be a class, a tuple of classes, or a union`으로.
   - `issubclass(1,42)`→"arg 1", `issubclass(int,42)`→"arg 2 ...(장문)", `issubclass(1,int)`→"arg 1" 일치.
   - 훅 내부 예외는 전파(삼키지 않음).

## 검증
- p20 stdout diff **0** (custom dunder, register native/user, __subclasshook__, io.IOBase
  virtual isinstance, abstractmethod, 에러 precedence).
- p19에서 보류한 `isinstance(open(f), io.IOBase/TextIOBase)` 이제 통과.
- bughunt **27/27**, evaluate `225 + 3 + 2` 불변.
- Godot replay(Release DLL) **82 matched, 0 other**.
- perf: suite는 isinstance/issubclass **미호출(grep 0)** → 데드코드. str_fmt/list_comp 플래그는
  HEAD(p19)도 str_fmt +21~27%/list_comp +0.6~23.7% 동일 밴드 = 만성 노이즈. p20 중립.

## 남은 한계 (별도 클러스터)
- **collections.abc parity**: `Lib/collections/abc.py`의 Iterable/Mapping/Sized/Hashable…가
  bare class(ABCMeta 아님, `__subclasshook__` 없음) → `isinstance([1], Iterable)` 등 구조 검사 실패.
  프로토콜은 갖췄으니 abc.py를 진짜 ABC로 바꾸면 동작. p20 프로브에서는 제외(측정 기록).
- builtin 함수 repr 5종 매트릭스(`repr(len)` 등) — 여전.
- `type.__module__`(메타클래스 자기 자신) — 여전.
