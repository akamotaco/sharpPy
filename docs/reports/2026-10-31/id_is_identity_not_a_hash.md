# P0-47 (Q1): `id()`는 주소가 아니라 정체를 돌려준다

기준: CPython `v3.12.15` pin, 참조 인터프리터 3.12.15. 기준 커밋 `d95c129`.
지시서는 [`../lifetime_foundation/README.md`](../lifetime_foundation/README.md) §Q1.

## 측정됐던 결함

`id()`가 `GetHashCode()`를 돌려줬다. SharpPy에서 해시는 값 기준인 경우가 있어서,
**다른 객체가 같은 id를 신고**했다.

| 관측 | CPython | SharpPy (수정 전) |
| --- | --- | --- |
| `id(a) == id(b)` (값은 같고 `is`는 False) | False | **True** (str/int/float 모두) |
| 살아 있는 list 300,000개의 고유 id | 300,000 | **299,312** |
| `copy.deepcopy` memo 충돌 | 0 | **667** |

memo 충돌 667건은 `copy.py`가 열리면 **에러 없이 틀린 객체**를 돌려주는 수치다.

## 고친 것

- `core/PyObjectId.cs` 신설 — `ConditionalWeakTable<PyObject, Number>`에 첫 호출 때 번호를 부여.
  CPython은 주소(`Python/bltinmodule.c:1234 builtin_id`, `:1237 PyLong_FromVoidPtr`)지만 CLR은
  객체를 옮기므로 주소를 쓸 수 없다. 번호는 재사용하지 않는다(언어는 "동시에 살아 있는 것끼리
  고유"만 요구하므로 이는 stricter).
- `core/PyBuiltin.cs CallId`를 `new PyInt(PyObjectId.Of(args[0]))`로.
- **안 바꾼 것**: repr의 `0x…` 주소 표기 31곳과 `object.__hash__`. CPython도 repr 주소와
  `id()`가 다르다.

## 프로브가 드러낸 기존 결함 두 개 (지시서 §함정)

`id()`를 고치자 `singletons:` 줄이 남았다. `is`를 함께 쟀더니 **원래 SharpPy가 달랐다.**
`PyObjectId`를 고쳐 덮는 대신 **원인을 각각 고쳤다.**

| 관측 | CPython | 원인 | 앵커 |
| --- | --- | --- | --- |
| `() is ()` | True | `PyTuple.Empty`가 **접근마다 new** property, 게다가 `runtime/TupleCache.cs`에 두 번째 빈 튜플이 공존, VM `BUILD_TUPLE`은 0개일 때도 new | `Objects/tupleobject.c:68-73 PyTuple_New` → `:62 tuple_get_empty` |
| `C.__name__ is C.__name__` | True | `PyNameDescriptor.__get__`이 읽을 때마다 `new PyString` | `Objects/typeobject.c:988 type_name` — heap은 `:994 Py_NewRef(et->ht_name)`, static은 `:998 PyUnicode_FromString` |

`type_name`의 갈래가 그대로 관찰 대상이라는 점이 핵심이다: `int.__name__ is int.__name__`은
CPython에서도 **False**다. 그래서 SharpPy도 heap일 때만 저장된 객체를 주고, static은 계속 만든다.
`__qualname__`은 `ht_qualname`으로 별개 필드 — CPython이 별개라 그대로 두 개로 뒀다.

삭제한 중복: `TupleCache.Empty` (경쟁하던 빈 튜플) → 8개 호출점이 `PyTuple.Empty`로 갔다.

## 게이트

| 게이트 | 결과 |
| --- | --- |
| q1 합격 프로브 | 일치 |
| bughunt | **88/88** (`tests/bughunt/p83_id_identity.py`로 복사) |
| evaluate | `225 / 3 / 2` |
| Godot 4.7.2 mono 재연 | 82 matched / 0 |
| similarity | exit 0 |

## 성능 (git worktree A/B, 기준 `d95c129`, 교대로 3라운드)

| 작업 | 비 (new/base) |
| --- | --- |
| 빈 튜플 300,000회 | 0.97 / 0.98 / 0.98 |
| `id()` 100,000회 | 0.94 / 0.99 / 0.97 |
| 역방향 점프 1,000만 | 0.91 / 0.99 / 0.98 |
| 2-튜플 300,000 회 | 1.10 / 1.03 / 1.02 → 순서를 바꿔 재실행 1.16 / 0.99 / 1.00 |

`pairs`의 첫 라운드만 높다. 두 순서 모두 **첫 라운드가 낮다** — 프로세스 워밍업이다.
2·3라운드 중앙값은 0.99~1.03이고, `id()`와 빈 튜플은 오히려 빨랐다.

## 남은 격차 (P0-47 잔여)

- `type.__name__ is type.__name__` → CPython False(static이라 매번 만든다), SharpPy **True**.
  내장 타입 이름 문자열이 `core/PyType.cs`의 `StringCache.GetOrCreate`로 인터닝되기 때문이다.
  `is`로만 관찰되는 차이이며, static/heap 갈래를 그 경로에도 적용해야 한다.
- `id()` 값 자체는 CPython과 다른 수다. 요구되는 것은 고유성과 `is` 일치뿐이며,
  repr의 주소 표기는 CPython도 `id()`와 다르므로 계속 별개다.
