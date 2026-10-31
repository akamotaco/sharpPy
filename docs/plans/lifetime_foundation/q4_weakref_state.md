# Q4 핵심 (C1): weakref 수명 설계

**담당: Claude.** 표면 작업은 [`local_ai_queue.md`](local_ai_queue.md)의 L2, L3이다.
상태: **미구현.** 트리는 Q3 완료 상태이고 Q4 코드는 커밋되지 않았다.

이 문서는 2026-10-31 점검으로 다시 썼다. 이전 판은 `git show 85bd172:docs/plans/lifetime_foundation/q4_weakref_state.md`에
있으며, 그 판의 "확정된 CLR 사실"과 "동작했던 설계"는 **따르면 안 된다** (아래 판정표).

이 영역은 합격 프로브가 우연히 통과할 수 있고, 지시서 작성자와 구현자가 둘 다 여기서 틀렸다.
그래서 아래의 모든 항목에 등급을 단다.

| 등급 | 뜻 |
|---|---|
| **검증** | 재현 파일이 있고 두 티어 모드에서 같은 결과가 나왔다 |
| **가설** | 코드 분석이나 추론이다. 구현 전에 재현부터 한다 |

## 1. 이전 판 주장의 판정

| # | 주장 | 판정 | 재현 |
|---|---|---|---|
| 1 | 가드 안의 `WeakReference<T>`는 가드와 같은 패스에 무효화된다 | **맞다.** 지시서 Q4 4항의 `List<WeakReference<PyWeakRef>>`가 틀렸다 | `spike/guard_weak_list.cs.txt` |
| 2 | `trackResurrection: true`가 대상을 살려 둔다 | **틀렸다.** long 핸들이 있어도 대상은 수거된다 | `spike/stage2_real_vm.py` |
| 3 | short 핸들은 재부활 뒤 복구되지 않는다 | 맞다. 다만 대상에 short를 쓰지 않으면 무관하다 | CLR 문서 동작. 재현 없음 |
| 4 | `ReRegisterForFinalize`로는 2단계가 오지 않는다 | **틀렸다.** 재부활 포함 2단계가 도착한다 | `spike/stage2_real_vm.py` |
| 5 | 가드 finalizer 실행은 대상 도달 불능의 증거다 | 맞다. 그 뒤 재부활 여부는 2단계가 판단한다 | 설계상 |

**2, 4의 재현 (실제 VM, 기본 티어와 `DOTNET_TieredCompilation=0`).** 기준 `85bd172`의 scratch worktree에서
`python spike/instrument_stage2.py <worktree>`로 로그를 넣고 빌드한 뒤 `SP_LONG=1`로 실행한다.

```
plain                                    res (재부활 후 keep.clear())
-- collect 1                             -- collect 1
STEP armed->finalized long alive 1/1     STEP armed->finalized long alive 1/1
__del__                                  __del__ resurrect
STEP finalized->cleared long alive 1/1   COLLECT done long alive 1/1
COLLECT done long alive 0/1              -- collect 2 ... long alive 1/1
                                         -- collect 3
                                         STEP finalized->cleared long alive 1/1
                                         COLLECT done long alive 0/1
```

**이전 측정이 왜 달랐는가:** 알 수 없다. WIP의 `PyFinalizer.cs`가 소실돼서 재현할 수 없다. **가설:** 무언가가
가드를 강참조했다. `spike/guard_weak_list.cs.txt`의 이전 판은 지역 변수 하나가 가드를 한 패스 더 붙잡아
`pass 2: nothing`을 냈다. C1 구현 중 2단계가 안 오면 이것부터 본다. `TryDequeue(out var g)`는 도우미로 분리한다.

## 2. ref 목록 측정

`spike/guard_weak_list.cs.txt`, 두 티어 모드 동일. **검증.**

| 경우 | `WeakReference<T>` | `GCHandle` Weak | `GCHandle` WeakTrackResurrection | 필요한 답 |
|---|---|---|---|---|
| kept: 다른 곳에서 붙잡힌 ref | False | True | True | True |
| dropped: 대상보다 먼저 죽은 ref | False | False | False | False |
| owned: 대상을 통해서만 닿는 ref (`o.r = ref(o, cb)`) | False | **False** | True | True |

**결정: 가드는 `List<GCHandle>`을 `GCHandleType.WeakTrackResurrection`으로 갖는다.** `GCHandle`은 finalizer가
없는 구조체라 가드와 함께 무효화되지 않는다. 강한 목록은 `ref died first`를 깨므로 쓰지 않는다. 프로브를
완화할 필요도 없다. 핸들은 수동 해제이므로 `Cleared`로 갈 때 전부 `Free`한다. 해제 누락은 영구 누수다.

## 3. 설계

```
PyWeakRef
  _target  : WeakReference<PyObject>(target, trackResurrection: true)   long. 검증 §1
  Dead     : 진실. _target은 수단이다. Step이 켠다
PyGuard
  WeakRefs : List<GCHandle> WeakTrackResurrection                       검증 §2
```

| 항목 | 내용 | 등급 |
|---|---|---|
| 순서 (일반 객체) | Armed: `__del__` → 다음 패스 Finalized: 전부 `Dead` → 최신부터 callback → `Free`, `Owner = null` | 검증 (2단계 도착) |
| `__del__` 없는 대상 | Guard를 `Finalized`로 시작하고 `Live`에 넣지 않는다 | 가설 |
| `__del__` 중의 `wr()` | long이라 `Pin` 없이 살아 있다. `Pin`, `Revive`, `LiveRef`, `Deferred`는 쓰지 않는다 | 가설 (long 생존은 검증) |
| 재부활 | 2단계가 오지 않을 뿐이다. `SweepDeferred` 같은 별도 판정이 필요 없다 | 검증 (2단계 도착) |
| 제너레이터 | **callback이 `close()`보다 먼저다.** `gen_dealloc`이 `:133`에서 weakref를 지우고 `:137`에서 finalize한다 (`Objects/genobject.c:126`). `__del__`의 순서와 반대이므로 Armed에서 처리한다 | CPython 실측, SharpPy 가설 |
| 재사용 | callback 없는 `ref(x)`는 같은 객체, 서브클래스는 공유하지 않는다 | CPython 실측 |
| 서브클래스 | `Keyed(ref)`는 `PyListSubclass`처럼 진짜 레이아웃으로 만든다. 일반 ref에 속성 dict를 두는 WIP 방식은 CPython이 거부하는 대입을 허용하므로 쓰지 않는다 | CPython 실측 |

## 4. 아직 재현하지 못한 위험 (구현 전에 재현부터)

**H1. 처형 대기 창.** GC가 대상을 도달 불능으로 판정한 뒤 Drain 전에 `wr()`이 long 핸들로 대상을 꺼내면
강참조가 생긴다. 그 뒤 Drain이 ref를 `Dead`로 만들면 살아 있는 객체의 ref가 None이 된다. 안전점을 지나지
않는 경로에서는 일어날 수 있다.
관측: `c1_weakref_core.py`의 `disagreements`. 0이 나와도 안전하다는 증거는 아니며, 반복 횟수를 늘려 확인한다.
대안 (미검증): 가드에 대상 short 핸들을 하나 두고, `wr()`이 그것이 죽은 것을 보면
`GC.WaitForPendingFinalizers(); Drain();`한 뒤 다시 답한다. `__del__` 실행 중에는 Drain이 no-op이므로 long의
답을 쓰게 되어 부모의 `__del__`이 자식을 보는 경우가 유지된다. 드문 경로에서만 비용을 낸다.

**H2. `Cleared` 가드 재사용.** 끝난 제너레이터는 `Detach`로 `Cleared`, `Owner = null`, `SuppressFinalize`
상태다. `Attach`가 이 가드를 그대로 돌려주면 새 weakref의 callback이 오지 않는다. 재부활 뒤 새로 만든 ref도
같은 경로를 지날 수 있다.
관측: `c1_weakref_core.py`의 `finished generator`, `ref made after resurrection`.

**H3. 비용.** weakref 대상마다 finalizable 가드가 생긴다. `WeakValueDictionary` 100,000항목을 기준 대비
측정한다. 허용치는 없고 보고한다. 일반 객체 비용은 여전히 0이어야 한다.

## 5. 작업 순서

1. L1 커밋 확인. `Live`가 고쳐지기 전에는 시작하지 않는다.
2. H2, H1을 최소 구현 위에서 재현한다.
3. `PyGuard` 확장, `PyWeakRef`, `PyWeakRefSubclass`, `_weakref` 등록.
   `ref` 호출에는 `PyType.Call`의 `tp_new` 대응 분기가 필요하다 (WIP의 `ConstructedByNewSlot` 참고).
4. 합격: `c1_weakref_core.py` 일치, 두 티어 모드, 10회 반복 전부 동일. 게이트 통과. 사본 `p94_weakref_core.py`.
5. 로컬 AI 또는 별도 세션이 경계 사례를 점검한 뒤 L3을 연다.

## 6. WIP에서 쓸 것과 버릴 것 (`.artifacts/q4_wip/`, gitignore)

| 파일 | 처리 |
|---|---|
| `weakref.py`, `_weakrefset.py` | 그대로. CPython과 동일 확인 |
| `_WeakrefModule.cs` | 골격과 `PyMethodDescriptor` 등록 방식은 쓴다. 이 방식이 필요한 이유는 측정됐다: 평범한 `PyBuiltinFunction`을 TypeDict에 넣으면 `BindSpecialMethod`가 바인딩하지 못해 `hash(r)`가 깨진다 |
| `PyWeakRef.cs` | `RichCompare`, `ToHash`, `ToRepr`는 쓴다. `Pin`, `Revive`, `_attrs`는 버린다. 생성자는 `true`인데 주석은 "FALSE여야 한다"고 적혀 있다. 주석이 틀렸다 |
| `AtexitModule.cs` | L2에서 수정 |

**WIP의 틀린 앵커.** 지우고 다시 찾는다.

| 틀린 인용 | 사실 |
|---|---|
| `Objects/_weakref.c` | 없는 파일. `Modules/_weakref.c` |
| `Objects/object.c:290 clear_weakrefs` | `Py_DecRef` 줄이다. `PyObject_ClearWeakRefs`는 `Objects/weakrefobject.c:938` |
| `Objects/object.c:1671 subtype_dealloc` | 무관. `subtype_dealloc`은 `Objects/typeobject.c`, weakref 정리는 `:1980` |
| `weakrefobject.c:663`, `:690`, `:699` | proxy 슬롯 줄이다. ref 타입은 `:368 _PyWeakref_RefType`, 멤버는 `:356` |
| `weakref_call`의 `tp_call` | 3.12.15의 `tp_call`은 `PyVectorcall_Call`이다 (`Objects/weakrefobject.c:374`). 호출 본체는 그 타입의 vectorcall에서 찾는다 |

P0-50을 proxy 자리표시자 번호로 쓴 WIP 문구는 P0-57로 바꾼다. P0-50은 L1에 배정됐다.

## 7. 범위 밖

- proxy: Q5, Claude. 슬롯별 프로브부터.
- 재귀 스택 (P0-52): 깊이 100에서 프로세스가 죽는다. 해결이 호스트 스택에 달려 있고 Godot 메인 스레드의
  스택은 바꿀 수 없을 수 있어 설계 판단이 필요하다. Claude가 C1 뒤에 맡는다.
- 순환 참조의 callback/`__del__` 순서: 기록된 B방 격차.
