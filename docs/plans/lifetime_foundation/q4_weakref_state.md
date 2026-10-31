# Q4 weakref: 어디서 멈췄고, 무엇을 측정했는가

상태: **미완료**. 코드는 커밋하지 않았고, 작업 트리는 `661d58c`(Q3 완료)로 되돌렸다.
WIP 파일은 `.artifacts/q4_wip/`(gitignore)에 있다: `type/PyWeakRef.cs`,
`modules/_WeakrefModule.cs`, `modules/AtexitModule.cs`, `Lib/weakref.py`, `Lib/_weakrefset.py`.
되돌린 이유는 `core/PyFinalizer.cs`가 도구 실수로 잘려(0바이트) 원복이 필요했기 때문이다.
즉 지금 트리는 반쪽 구현이 아니라 **완전한 Q3 상태**이고, 게이트는 그 기준으로만 유효하다.

## 측정으로 확정된 CLR 사실 (다시 추측하지 말 것)

1. `WeakReference<T>`는 **참조 대상이 죽지 않아도**, `WeakReference` 객체 자체가 그 패스에서
   도달 불능이면 무효화된다. 가드(`PyGuard`) 안쪽에 들어 있는 약한 항목은 가드가 죽는 바로 그
   패스에서 예외 없이 `TryGetTarget == false`를 반환했다 → 콜백이 조용히 한 번도 돌지 않았다.
   `trackResurrection` true/false 둘 다 같은 증상을 냈다(관측 로그 `DBG clear entry=False`,
   대상은 살아 있었고 `getweakrefs`에도 잡히는 상태였다).
   ⇒ **약한 목록은 가드 위에 쓸 수 없다.** 강한 `List<PyWeakRef>`로 바꿨을 때 콜백이 실제로
   동작했다(`after1: ['del', 'cb_withdel']`, `plain after1: [... 'cb_plain']`).
2. `trackResurrection: true`는 대상을 **살려 둔다**(약한 참조가 대상보다 오래 살면 대상이 회수되지
   않음) → 목표가 콜백을 기다리는 그 컬렉션을 통과하지 못했다.
   ⇒ `PyWeakRef._target`과 가드의생존 오라클은 반드시 **짧은 형태**(false).
3. 짧은 형태는 **부활 후 복구되지 않는다**. 컬렉터가 도달 불능으로 판정한 순간 핸들이 무효화되고,
   `__del__`이 재참조해도 `TryGetTarget`은 false로 남는다.
   ⇒ 부활을 관측했으면 핸들을 새로 갈아끼워야 한다(`PyWeakRef.Revive(target)`).
4. 가드의 CLR finalizer를 `GC.ReRegisterForFinalize`로 재장전해도 **두 번째 passes가 오지 않았다**
   (연속 3회 `gc.collect()`에서 q=0). ⇒ 2단계 최종화(arm → finalize → clear)는 이 구조로 불가.
5. 가드의 finalizer 실행 자체가 "대상 도달 불능"의 증거다. 가드는 `ConditionalWeakTable`의 값이고
   값은 키(대상)가 살아 있는 동안만 도달 가능하므로. 단 `__del__`이 **나중에** 실행되므로
   부활 여부는 그 순간에 물을 수 없다. ⇒ `__del__` 직후에 짧은 오라클을 새로 만들고
   (`guard.LiveRef = new WeakReference<PyObject>(finalizing, false)`), 콜백은 `Deferred` 상태로
   미뤘다가 다음 `gc.collect()` 앵커에서 판정한다.

## 동작했던 설계 (재구성 기준)

- `PyGuard`에 `WeakRefs`(강한 목록), `LiveRef`(짧은 오라클), `State`(Armed/Finalized/Deferred/Cleared).
- `PyFinalizer.AddWeakReference(target, ref)`가 가드를 만들거나 재사용. `owesFinalizer`가 false면
  `State=Finalized`로 시작(Live 목록에 넣지 않음 — shutdown 스윕은 `__del__` 채무만 다룬다).
- `NewWeakReference`: callback이 없으면 같은 대상에 대해 **같은 객체** 반환(CPython 동일, 측정됨).
- `Step`: Armed → `RunFinalizer` → `Owner=null` → **짧은 오라클 새로 설치** → weakrefs가 있으면
  Deferred+AddDeferred, 없으면 Cleared. Finalized(weakref 전용 가드) → 즉시 `ClearWeakReferences`.
- `SweepDeferred()`: `CollectAndDrain`의 각 라운드와 종료 시점에 호출. 목록은 `WeakReference<PyGuard>`
  (강한 목록이면 CWT 때문에 대상이 살려진다). 살아 있으면 `Revive`, 죽었으면 `ClearWeakReferences`.
- `RunFinalizer`는 `__del__` 실행 동안 각 ref에 `Pin = target`을 걸고 finally에서 해제
  (CPython은 dealloc이 대상과 ref를 함께 붙들고 있어서 이 문제가 없다).
- 콜백은 최신 등록 순서로, 예외는 `Exception ignored in:`으로 보고만 한다(측정 일치 확인).
- `_weakref`는 네이티브, `Lib/weakref.py`·`Lib/_weakrefset.py`는 CPython 사본.
  타입 멤버는 `PyMethodDescriptor`/`PyGetSetDescriptor`로 등록해야 한다 — 평범한
  `PyBuiltinFunction`을 TypeDict에 넣으면 `core/PyObject.cs:744 BindSpecialMethod`가 바인딩하지
  못해 self 없이 호출된다(`hash(r1)` crash로 측정). `Lib/weakref.py:89`가 `ref.__hash__`를 읽으므로
  항목은 존재해야 한다.
- `_weakref.ref` 호출 가능하게: `PyType.Call`에 `ConstructedByNewSlot` 분기를 추가(`tp_new` 대응).
  이 분기는 플래시를 켠 타입만 쓰도록 해 float 등 기존 CreateInstance 경로를 건드리지 않았다.
- `KeyedRef`는 `__slots__ = "key"`로 ref 인스턴스에 속성을 저장한다(weakref.py:341). CLR 네이티브
  타입에는 `PyClassInstance` 슬롯 저장소가 없으므로 `PyWeakRef`에 속성 딕셔너리를 두는 방식으로 해결.
  평범한 ref가 임의 속성을 받는 것은 CPython과의 차이(기록 대상).

## 남은 작업 순서

1. `core/PyFinalizer.cs`에 위 항목 재적용(`PyGuard` 확장, `AddWeakReference`, `NewWeakReference`,
   `WeakReferences`, `CountWeakReferences`, `SweepDeferred`, `PinWeakReferences`, atexit 훅).
2. WIP 파일 5개 원복, `core/PyModule.cs`에 `_weakref`/`atexit` 등록,
   `modules/BuiltinsModule.cs`에 `ReferenceError`, `core/PyException.cs`에 `PyReferenceError`,
   `core/PyType.cs`에 `ReferenceErrorType`·`WeakRefType`·`SupportsWeakReference()`·
   `ConstructedByNewSlot`, `type/PyClass.cs`에 `AllowsWeakReferenceSlot()`.
3. 프로브 `docs/plans/lifetime_foundation/probes/q4_weakref.py` 재실행.
   Q3까지 통과 상태(`alive`, `dead`, `same ref object`, `hash survives`, `weak object has gone away`,
   `count`, `ref died first: []`, refusal 표 전체)에서, 다음 두 줄이 남는다:
   - `resurrected: True []` — 위 Deferred 설계로 해결 목표.
   - `callback order: ['third registered', 'second registered', 'first registered']`
     — 강한 목록이라 순서 자체는 지켜진다(강한 목록 상태에서 콜백 실행 확인됨).
4. `ref died first`(자기 ref가 먼저 죽은 경우)는 강한 목록 때문에 CPython과 다르게 콜백이 돈다.
   `docs/known_gaps.md`에 P0-50으로 남기고, 프로브를 통과시키는 선을 어디로 잡을지(통과 후
   `tests/bughunt/p88_*`로 승격)는 그때 판단한다 - q2_gap_late_del.py처럼 통과 기준 두지 않는 방법도 있다.
5. proxy(Q5)는 별도 커밋. `ref.clear()`/`ref.alive`는 3.13 API이므로 추가 금지.

## 게이트 되돌리기 체크

되돌린 트리에서 다시 확인해야 하는 기준: build 0/0, `tools/run_bughunt.py` 92/92,
`tools/evaluate.py --timeout 90` 225/3/2, `tools/cpython_similarity.py` exit 0, Godot replay 82/0.
