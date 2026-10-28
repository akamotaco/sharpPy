# p21 — collections.abc structural protocol + registration (2026-10-28)

## 동기
p20 이후 남은 결손: `isinstance([1], collections.abc.Iterable)` 등 **구조적 ABC 검사** 실패.
`Lib/collections/abc.py`는 bare class(ABCMeta 없음), `Lib/_collections_abc.py`는 CPython
그대로의 `_check_methods`인데 CLR에서 성립하지 않았다.

## C vs C#/CLR — 이 클러스터의 핵심 (사용자 지적)
CPython은 "이 타입이 `__X__`를 구현했나"를 순수 Python `_check_methods`로 판정한다:
`for B in C.__mro__: if name in B.__dict__`. 이게 성립하는 이유는 C에서 **모든 프로토콜
동작이 slot-wrapper 객체로 `type.__dict__`에 저장되고 `object`에서 상속**되기 때문이다
(`object.__dict__`에 `__hash__`/`__eq__`, `list.__dict__['__hash__']`는 literally None,
`str.__dict__`에 자체 `__hash__`).

SharpPy는 CLR에서 그 동작을 `PyObject` 서브클래스의 **CLR 가상메서드로 직접 구현**하고
대부분을 `type.__dict__`에 미러링하지 않는다. 실측:
- `hasattr(str, '__hash__')` → SharpPy **False** (CPython True)
- `dict.__dict__`에 `__len__`/`__iter__` **없음**
- `object.__dict__`에 `__hash__`/`__eq__`/`__repr__` **없음**

→ 순수 Python `in B.__dict__` probing은 CLR에서 **근본적으로 취약**. "pure-Python
`_collections_abc.py`를 이식하면 동작한다"는 p20 가설은 CLR에서 틀렸다.

## C#-idiomatic 번역 (측정으로 설계)
1. **native capability 원시 `_abc._method_supports(C, name)`** (`modules/AbcModule.cs`)
   - CPython의 per-type slot table을 C#로 번역: MRO own-dict(`PyClass.ClassDict`/
     `PyType.TypeDict`) 스캔 + **CPython 실측 기반 native builtin 표**(`_nativeCaps`).
   - CPython의 "absent"와 "`__dict__[name] is None`"(unhashable list)은 둘 다
     `_check_methods`에서 NotImplemented로 수렴 → 여기선 둘 다 False.
   - 표는 `.artifacts/tmp/capslots.py`로 CPython slot을 실측 생성(object/type/int/str/
     list/dict/set/…/dict_keys…/memoryview), 추측 아님.
   - user/Generic 타입은 MRO TypeDict 스캔 + object 기본 `__hash__`.
2. **`register()` 조상 전파** (`AbcRegister`)
   - CPython은 `MutableSet.register(X)` 시 `isinstance(X(), Set/Sized/Iterable/…)`도 True
     (실측 확인: 조상 레지스트리에 전파, Sequence는 조상 아니라 False). SharpPy는 자체
     registry만 봐서 실패 → register 시 조상 MRO 전 레지스트리에도 추가.
3. **`Lib/_collections_abc.py` 재작성**: core 프로토콜 ABC(Hashable/Iterable/Iterator/
   Sized/Container/Callable/Collection/Reversible/Generator + async/Awaitable/Buffer)는
   `metaclass=ABCMeta` + `__subclasshook__`(`cls is` 가드) + native 위임 `_check_methods`.
   composite(Sequence/MutableSequence/Set/MutableSet/Mapping/MutableMapping/views/
   ByteString)은 CPython대로 **비(非)구조적** + concrete mixin + `register()`.
   `collections/abc.py`는 CPython처럼 `from _collections_abc import *` 재수출.
4. **CLR 팩토리 함수 회피**: SharpPy에서 `range`/`memoryview`는 global이 type이 아니라
   `builtin_function_or_method` → `type(range(0))`/`type(memoryview(b''))`로 등록.
5. **`Sequence` mixin 원문 일치**: `__contains__`(순회), `__reversed__`(제너레이터,
   None 아님), `index`(while 루프)를 CPython 원문으로 교체(내가 처음 손으로 쓴 버전 오류).

## 검증
- **ABC 매트릭스 0-diff**: 15 ABC × 18 타입 = **285개 isinstance 전부 CPython 일치**
  (`isinstance(x, Iterable/Sized/Container/Hashable/Callable/Collection/Reversible/
  Sequence/MutableSequence/Set/MutableSet/Mapping/MutableMapping/Generator)`),
  negative(unhashable·non-sequence) 포함.
- register 전파·user duck-type·ABC 서브클래싱+mixin(index/count/pop/remove/reversed/
  get/setdefault/update/keys/items/values)·set 대수·abstract 인스턴스화 가드·`__all__`·
  io.IOBase continuity → p21 stdout diff **0**.
- bughunt **28/28**, evaluate `225 + 3 + 2` 불변.
- Godot Debug/Release 오류 0, replay **82 matched, 0 other**.
- perf: ABC는 hot path 아니고 suite도 isinstance 미사용 → 전부 ±9.2% 이내(만성 밴드),
  >15% 플래그 없음.

## 남은 한계 (측정 기록)
- `method_supports` native 표에 없는 **비(非)materialized 커스텀 타입**(파일·일부 view)은
  구조 훅에서 위임 후 미스 가능(레지스트리로 보완). 완전한 general capability 추론은
  PyType→CLR 역매핑이 없어 실측 테이블로 대체.
- SharpPy는 `range`/`memoryview` global이 factory function → CPython과 타입 노출 자체가 다름
  (ABC는 인스턴스 타입으로 우회 등록). 타입 자체 재설계는 p11/내장타입 클러스터.
- `Sequence`에 `__abc_tpflags__`(Py_TPFLAGS_SEQUENCE)는 미구현(observable isinstance에는 영향 없음).
- `ByteString` deprecated 3.12 경고 동작 미검증.
