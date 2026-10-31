# 가드의 세 모서리를 고쳤다 (P0-50, L1)

날짜: 2026-10-31. 기준 `85bd172`, 작업 브랜치 `work/bug-hunt`.
지시서: [`docs/plans/lifetime_foundation/local_ai_queue.md`](../../plans/lifetime_foundation/local_ai_queue.md) L1.

Q2(`b619ee7`)가 통과 프로브를 깨고 들어온 결함 세 건이다. 세 건 모두 **합격 프로브를 통과한 코드**에
있었고, 프로브가 건드리지 않은 경로(종료 스윕 순서, 디스크립터, `__del__ = None`)에서만 드러났다.

## 1. `Live` 슬롯이 낡아서 종료 시 `__del__`이 유실됐다

`RemoveFromLive`는 swap-remove다. 마지막 항목을 빈 자리에 옮긴 뒤 **그 가드의 필드**에 새 번호를
써야 하는데, 옮긴 항목의 가드는 이미 도달 불능일 수 있어서 `WeakReference.TryGetTarget`이 실패한다.
그르면 번호가 낡은 채로 남고, 뒤에 그 가드가 처리될 때 **남의 항목**을 지운다. 지워진 가드는 종료
스윕(`SnapshotLive`)에서 사라져 `__del__`을 받지 못한다.

`SnapshotLive`의 중간 `RemoveAt(i)`도 같은 종류였다. 중간 삭제는 뒤 항목의 번호를 전부 어긋나게 한다.

**고친 것.** 번호를 가드에서 **행**으로 옮겼다. 행은 자기 번호를 아므로 옮길 때 약참조를 풀 필요가
없다. 중간 삭제는 없앴다 — 죽은 행은 그 가드의 `Step`이 자기 것을 지운다.

```csharp
internal sealed class LiveEntry { internal int Index; internal WeakReference<PyGuard> Ref; }
```

`PyGuard.Slot: int` → `PyGuard.Entry: LiveEntry`. 삭제된 경로: `Target(Live[slot])`로 재번호를 시도하던
분기, `SnapshotLive`의 `else Live.RemoveAt(i);`, `guard.Slot = -1` 초기화 세 곳.

**측정.** `probes/l1_live_slots.py` (재현:
`<python> tools/run_bughunt.py --probes C:/Users/.../lifetime_foundation/probes --filter l1_`).
dropper 2000개 + survivor 조합에서 SharpPy 출력 **2053줄**, 참조 인터프리터 **2053줄**, `diff` 빈 결과.
수정 전 값은 지시서에 3/3 실행으로 기록된 2048줄(2건 유실)이다 — 이 값은 지시서 측정이고, 내가
다시 잰 것은 아래 일치 결과다.

## 2. `__del__`을 클래스 dict 값 그대로 불렀다

`hook.Call(new PyObject[] { target }, null)`은 바인딩을 건너뛴다. CPython은 `slot_tp_finalize`
(`Objects/typeobject.c:9071` `slot_tp_finalize`)에서 `lookup_maybe_method`(`:2188`)를 쓰고 결과를
`call_unbound_noarg`(`:2240`)로 부른다. 그 소스의 실제 규칙은 이렇게 읽힌다:

| dict 값 | `lookup_maybe_method` | 호출 |
|---|---|---|
| 평범한 `def` (METHOD_DESCRIPTOR, `:2195`) | unbound=1, 임시 PyMethod를 만들지 않는다 | `PyObject_CallOneArg(func, self)` → self 전달 |
| `staticmethod` | unbound=0, `tp_descr_get` → 평범한 함수 | 인자 0개 |
| `classmethod` | unbound=0, `tp_descr_get` → 바운드 메서드 | 인자 0개 |
| `__get__`이 있는 객체 | unbound=0, `tp_descr_get`의 결과 | 인자 0개 |
| 디스크립터가 아닌 호출 가능 객체 | unbound=0, `tp_descr_get` 없음 → 그대로 | 인자 0개 |
| `None` | unbound=0 | 호출 → `TypeError` → `PyErr_WriteUnraisable(del)` (`:9084`) |

참조 인터프리터 실측(`probes/l1_del_hook.py` stdout)도 같다: `callable object: [('hook args', 0)]`,
`staticmethod: ['static']`, `classmethod: [('classmethod', 'WithClassMethod')]`,
`custom descriptor: [('__get__', True), 'bound by __get__']`.

**고친 것.** 새 바인딩 코드를 만들지 않고 기존 바인더를 썼다.

```csharp
target.BindSpecialMethod(hook).Call(Array.Empty<PyObject>(), null);
```

`BindSpecialMethod`(`core/PyObject.cs:744`)는 `IDescriptor`면 `Get(instance, owner)`를, 아니면 값을
그대로 돌려주므로 위 표의 네 디스크립터 행과 None 행이 그대로 재현된다. 평범한 `def`는 바운드
메서드를 거쳐 0-인자가 되므로 결과만 같다 (CPython이 피하려 하는 것은 임시 `PyMethodObject`일 뿐이고
그것은 최적화다). 호출 가능 객체가 `self`를 받지 않는다는 점은 직관에 반하지만 측정값이다.

## 3. `__del__ = None`이 부모를 불렀다

`FindFinalizerHook`은 `own is PyNone`이면 `continue`로 건너뛰었다. CPython은 MRO에서 **처음 찾은
이름**에서 멈추고 그 값을 부른다 — `lookup_maybe_method`는 `_PyType_Lookup` 한 번이지, "None이 아니면
계속"이 아니다. 그래서 SharpPy는 `['P']`을 만들고 CPython은 `[]`이다.

**고친 것.** `own is PyNone` 조건을 삭제했다. None도 훅이고, 2번의 바인딩·호출 경로를 지나면서
`TypeError: 'NoneType' object is not callable`로 보고된다. stderr 실측
(`.artifacts/bughunt/p93_del_hook.sp.txt`): `Exception ignored in: None` +
`TypeError: 'NoneType' object is not callable`. 이제 `_finalizerHook == null`이 "선언 없음"의 유일한
뜻이다.

## 변화량

| 파일 | 추가 | 삭제 |
|---|---|---|
| `core/PyFinalizer.cs` | 53 | 13 |
| `type/PyClass.cs` | 7 | 1 |

추가된 62줄 중 33줄이 주석(앵커와 실패 재현 기록). 실행 코드 순증가는 +14줄 상당.

## 게이트

| 게이트 | 결과 |
|---|---|
| 빌드 | 오류 0, 경고 0 |
| `probes --filter l1_` | **2/2 일치** |
| `probes/l1_live_slots.py` stdout | SharpPy 2053줄 = CPython 2053줄, `diff` 빈 결과 |
| `tools/run_bughunt.py` | 94/94 일치 (사본 p92/p93 포함) — L2.1에서 재확인 |
| `tools/evaluate.py --timeout 90` | `225 / 3 / 2` |
| `tools/cpython_similarity.py --root .` | exit 0 |
| Godot 4.7.2 mono 재연 | **82 matched, 0 other**, `REPLAY_THREADS 2 main=2` (단일 스레드 전제 유지) |

합격 프로브 사본: `tests/bughunt/p92_live_slots.py`, `tests/bughunt/p93_del_hook.py`.

## 남은 것

- **P0-51**: 클래스 객체는 등록 경로를 지나지 않아 메타클래스 `__del__`이 돌지 않고, 멈춘 코루틴의
  `finally`도 실행되지 않는다. 이번 변경으로 가려지지 않았다 (`probes/gap_lifetime_recorded.py`).
- `Step`/`Drain`/상태 전이는 건드리지 않았다. C1(weakref 수명 핵심)이 그 표면을 소유한다.
- 성능: 일반 객체 경로는 `Attach`/`AddToLive`만 거치므로 변화 없다. 종료 스윕은 죽은 행을 더 이상
  중간에서 지우지 않아 목록이 죽은 행을 잠시 들고 있지만, 스윕은 프로세스당 한 번이다.
