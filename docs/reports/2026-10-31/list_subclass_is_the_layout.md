# P0-43b Q2: a list subclass instance *is* the list

기준: CPython `v3.12.15` pin, 참조 인터프리터 3.12.15. 이 문서는 동작이 아니라
**객체 모양**을 다룬다. 저장소는 그대로 두고 표현만 하나 지운다.

## 무엇이 달라졌나

이전: `class C(list)`의 인스턴스는 `PyClassInstance`였고, 값은 `_listStorage`라는
별도 `PyList`를 들고 있었다. 그래서 모든 내장 연산이 "먼저 풀어서" 접근해야 했다.

CPython은 그 자리에 객체 하나만 만든다. `list_new`가 list 레이아웃을 `ob_type`을
서브클래스로 두고 할당하고, `inherit_slots`가 `list_length`/`list_repr`/`list_concat`을
서브클래스의 슬롯으로 심는다(`Objects/listobject.c`, `Objects/typeobject.c:6803`).
둘째 객체는 없고, 통과할 것도 없다.

이제 `PyListSubclass : PyList`가 그 자리다. `x is PyList`가 곧 `PyList_Check`이고,
repr/len/반복/비교/연산은 C# 상속으로 그냥 들어온다. CLR 상속이 볼 수 없는 것은
Python 클래스 dict뿐이라, 이미 tuple/bytes가 쓰던 `SubclassInstanceSupport`를 쓴다.

지운 것:
- `PyClassInstance._listStorage`, 생성 시 할당, `GetListStorage()`
- `PyNativeStorage.ForList`의 3단계 특례 → `self as PyList` 한 줄

## 이 과정에서 CPython이 가르쳐 준 것

`evaluate`가 처음으로 `sharppy_failure`를 냈고, 범인은 `tests/warnings_boundary_regression.py`였다.
`Lib/warnings.py`는 필터를 훑을 때 `list.__len__(active_filters)`, `list.__getitem__(...)`를 쓴다.
CPython `_warnings.c`가 `PyList_GET_SIZE`/`PyList_GET_ITEM`을 쓰는 것과 같은 이유다 —
사용자가 `__len__`/`__iter__`/`__getitem__`을 정의한 리스트를 필터로 넣어도
스캔이 간섭받으면 안 된다.

즉 CPython에는 길이로 가는 **두 갈래**가 공존한다:

| 경로 | CPython | SharpPy |
| --- | --- | --- |
| `len(x)`, `if x:` | `sq_length` 슬롯 → 서브클래스가 `__len__`을 선언했다면 `slot_sq_length` | `PyList.Length()` virtual → `SubclassInstanceSupport.Length`가 선언 것을 먼저 부름 |
| `list.__len__(x)` | `list_length`(layout을 직접 읽는 C 함수) | `PyList.RawCount()` (virtual 아님) |

`PyListSubclass.Length()` override가 `list.__len__`까지 삼켜서 AssertionError가 새어 나왔다.
고치는 방향은 우회로 추가가 아니라 **두 갈래를 CPython처럼 나누는 것**이었다:

```csharp
internal int RawCount() => _items.Count;          // list_length
internal PyObject RawIterator() => new PyListIterator(this);
internal bool RawContains(PyObject item) => ...;  // list_contains

public override int Length() => RawCount();       // 슬롯 쪽은 여기에 위임
```

이렇게 하면 일은 한 번만 구현되고, dispatch만 두 갈래다. `PyList.cs` 안에서
storage에 대한 virtual 호출 6개가 모두 raw 호출로 바뀌었다(0개).

## 선언된 dunder가 이기는 규칙

`update_one_slot`은 타입 생성 시 한 번 결정한다: 클래스 dict에 `__len__`이 있으면
그 슬롯은 그 함수를 부르는 래퍼가 되고, 아니면 베이스의 C 함수가 그대로 남는다.
CLR 서브클래스는 기본이 "베이스의 C# 동작"이므로 같은 결정을 프로토콜 메서드마다 내야 한다.
네 컨테이너 서브클래스가 이를 각각 재발명하지 않도록 `SubclassInstanceSupport`에 한 번만 담았다
(`Repr`/`Length`/`Contains`/`Iterate`/`IsTrue`). `__repr__`이 str이 아닌 값을 돌려주면
CPython처럼 `__repr__ returned non-string (type X)`로 거부한다.

## 게이트

| 게이트 | 결과 |
| --- | --- |
| 빌드 | 오류 0, 경고 0 |
| bughunt | 86/86 |
| evaluate | `225/3/2` (중간 단계에서 224+1 실패 → `list.__len__` 우회 규칙을 되찾아 복구) |
| Godot 4.7.2 mono 재연 | 82 matched / 0 |
| perf | `list_comp` baseline 대비 -2.3%, 목록 계열 플래그 없음 |

## 남은 것

- `PyClassInstance`는 아직 `_dictStorage` / `_setStorage` / `_frozenStorage` / `_strStorage`를 든다.
  같은 두 갈래 규칙(raw = 그 타입의 method_descriptor, slot = virtual)으로 하나씩 지운다.
  dict는 `keys/values/items`, `pop`, `update`에서 storage를 읽는 곳이 더 많으니 따로 다루기엔
  호출점 조사가 필요하다.
- 서브클래스 인스턴스가 `PyClassInstance`가 아니게 되면서, "사용자 인스턴스"를
  `is PyClassInstance`로 판정하던 코드는 이제 heap 타입 인스턴스만 잡는다. 컨테이너
  서브클래스가 그 판정에 의존한 적은 bughunt에서 드러나지 않았지만, dict/set 전환 때도 같은 확인을 한다.
