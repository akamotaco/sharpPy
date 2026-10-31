# 수명·gc·weakref 정책 (2026-10-30 확정, 2026-10-31 개정)

이 문서는 "객체의 수명을 누가 결정하는가"에 대한 SharpPy의 확정 결정과, 그 결정이
CPython과 **어디에서 같아지고 어디에서 영원히 달라지는가**를 기록한다. 코드의 주석은 이 문서를
참조하므로, CPython 소스와 비교할 때는 여기의 앵커 표를 먼저 읽으면 된다.

확정 시점: 2026-10-30, 브랜치 `work/bug-hunt`, 커밋 `3215eff`(gc/`__del__`) 및 그 직전 논의.
참조 구현: CPython 3.12.15 (pin: `cpython-v3.12.15`, `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`).

**2026-10-31 개정 (사용자 승인).** §1의 세 문장은 그대로다. 바뀐 것은 그 아래의 수단이다.
실측으로 틀린 것이 확인된 서술을 고쳤고, CLR finalizer 금지를 "Python 코드 실행 금지"로
좁혔다. **구현 전이므로 "개정 설계"는 아직 코드에 없다.** 작업 지시서:
[`plans/lifetime_foundation/README.md`](plans/lifetime_foundation/README.md).

| 이전 서술 | 실측 | 개정 |
|---|---|---|
| `__del__`은 종료까지 대기한다 | 버린 객체 300,000개 중 **7,887개만** 호출. CLR이 수거하면 약참조가 죽어 호출이 사라진다 (`core/PyFinalizer.cs:105`) | 대기가 아니라 **유실**이다. Guard로 고친다 |
| 재부활 시 `__del__`이 다시 불린다 | heap class는 **1회만** (`Objects/object.c:364`, `__slots__ = ()`도 동일) | 재부활 지원은 재등록을 안 하는 것으로 끝난다 |
| 종료 순서는 생성 순서다 | **모듈 dict 순서**다 (`q2_shutdown_order.py`). p76은 두 순서가 우연히 같았다 | 종료 sweep을 dict 순서 우선으로 고친다 |
| 종료 시점은 양쪽 답이 일치한다 | 제너레이터 `finally`는 종료 시에도 **실행되지 않는다** | 제너레이터도 Guard 대상 |
| 첫 weakref 시 finalizer 래퍼는 `self`가 없어 불가 | Guard가 대상을 강참조하면 `self`가 남는다 (스파이크) | §4.3 철회 |
| VM이 대상을 붙잡으면 앵커가 안 선다 (미검증) | 29개 시나리오 모두 회수, Tiered JIT 켜고 꺼도 동일 | §5.1 검증 완료 |

---

## 1. 결정

> **수명(lifetime)은 CLR GC가 소유한다. 참조 카운팅은 도입하지 않는다.**
> **해제(release)는 GC에 의존하지 않고 소유자가 명시한다.**
> **동작 표면(출력·문구·타입·예외 family)은 계속 CPython과 동일하게 유지한다.**

"결과를 CPython과 동일하게 할까, C# 친화적으로 할까"는 하나의 선택이 아니다. 관측량을 세
방으로 나누고 방마다 답을 정한다.

| 방 | 무엇 | 동일성 | 수단 |
|---|---|---|---|
| **A. 순수 결정적 결과** | 같은 입력 → 같은 출력. 연산, 에러 문구, 타입, `is_tracked`, `get_referents`, `__del__` 배포 semantics, **`gc.collect()` 이후에 관측한 `__del__`·weakref·제너레이터 `finally`** | **동일해야 한다** (프로브로 고정) | CPython 소스 이식, `gc.collect()` 앵커 |
| **B. 수명 시점 관측** | 대상이 도달불능이 된 *순간*을 프로그램이 읽는 경우: `gc.collect()` 이전의 `__del__`, weakref 콜백, `ref()`, `len(weak_dict)`. 순환 참조에서의 callback/`__del__` 순서 | **동일 불가** (원리적) | 여기만 C# 친화. "다음 GC + 안전점"까지 축소하되 **유실은 허용하지 않는다** |
| **C. 자원 상태** | 파일 잠금, handle 수, 캐시 크기, 종료 후 잔여물 | **동일하게 만든다** | 명시적 해제(release bus). **B로 위장하면 안 된다** |

핵심 문장 두 개:

1. **B 방은 예외 구역으로 명시 선언한다.** 애매하게 두면 "어느 실행에선 같고 어느 실행에선
   다른" 상태가 되고, 그것은 프로브로 고정할 수 없어 영구 미해결이 된다.
2. **C 방은 B가 아니다.** 파일이 잠기거나 handle이 쌓이는 것은 diff가 아니라 실패다. 그러니
   "GC가 알아서"에 맡길 수 없고, 참조 카운팅 재도입으로도 풀지 않는다(기관은 다르되 결과는
   같게 만든다).

---

## 2. CPython의 기관 (pinned 소스 앵커)

비교할 때 이 순서로 읽으면 된다.

### 2.1 소멸과 `__del__`

|anchor|내용|
|---|---|
|`Include/object.h:690` / `:705`|`Py_DECREF` 가 0 에 도달하면 `_Py_Dealloc(op)` 으로 넘긴다|
|`Objects/object.c:2623 _Py_Dealloc`|`tp_dealloc` 으로 위임. **소멸 시점이 참조 수의 함수**라는 사실의 근원|
|`Objects/typeobject.c:1903`|`subtype_dealloc` → `PyObject_CallFinalizerFromDealloc`. 히프 타입(사용자 클래스)의 `__del__` 지점|
|`Objects/genobject.c:137`|히프 타입이 아닌 쪽 호출 사례(제너레이터). `tp_finalize` 이 타입별 dealloc 안에서 직접 불리기도 함|
|`Objects/object.c:357 PyObject_CallFinalizer`|`tp_finalize` 호출. `:362` 주석 `tp_finalize should only be called once`, `:364` `_PyGC_FINALIZED` 검사로 **재부활해도 다시 부르지 않음**, 호출 후 `_PyGC_SET_FINALIZED`|
|`Objects/object.c:374 PyObject_CallFinalizerFromDealloc`|재부활(resurrection) 처리. `:398` "tp_finalize resurrected it!"|
|`Objects/typeobject.c:1901`|`subtype_dealloc` 의 `/* Maybe call finalizer; exit early if resurrected */`|
|`Objects/typeobject.c:9071 slot_tp_finalize`|타입에서 `__del__` 을 찾아 호출 → **오버라이드가 이기고 `super().__del__()` 가 기저에 도달**하는 이유. `:9077`/`:9091` 진행 중 예외를 저장·복원|
|`Objects/typeobject.c:1964` → `:1980`|`subtype_dealloc`: `__del__` 다음에 `PyObject_ClearWeakRefs`. **refcount 경로에서는 `__del__` 안에서 약참조가 살아 있다**|
|`Modules/gcmodule.c:1284` → `:1290`|순환 경로는 반대: `handle_weakrefs` 다음에 `finalize_garbage`. 실측도 반대 순서|
|`Objects/genobject.c:70 _PyGen_Finalize`|죽는 제너레이터를 닫는다. `:399-404` handler 깊이가 1이면 생략|
|`Objects/typeobject.c:9499`|`TPSLOT(__del__, tp_finalize, slot_tp_finalize, ...)` — `__del__` 이 `tp_finalize` 에 매핑되는 표. 타입 생성 시 slot 정합(slotdef/update_one_slot)으로 채워짐|

### 2.2 weakref

|anchor|내용|
|---|---|
|`Include/cpython/object.h:195 tp_weaklistoffset`|타입이 opt-in 하면 인스턴스 안에 weaklist 헤드가 존재. **지원 타입/불가 타입이 갈리는 유일한 이유**|
|`Objects/weakrefobject.c:938 PyObject_ClearWeakRefs`|대상 소멸 시 호출되어 ref를 죽이고 콜백을 부른다. **콜백 시점이 참조 수에 붙는 이유**|
|`Objects/weakrefobject.c:300 / :794 / :851`|`cannot create weak reference to '%s' object` (ref 생성, proxy 생성 등 진입점별)|
|`Objects/weakrefobject.c:152`|`weak object has gone away` — 죽은 ref 해시|
|`Objects/weakrefobject.c:396`|`weakly-referenced object no longer exists` — 죽은 proxy 접근 (`ReferenceError`)|
|`Objects/weakrefobject.c:107 weakref_dealloc`, `:908 PyWeakref_GetObject`|ref 자체의 수명, 그리고 `ref()` 의 기관|
|각 타입 dealloc (`funcobject.c:852`, `codeobject.c:1744`, `classobject.c:238`, `genericaliasobject.c:33` …)|`ClearWeakRefs` 호출 위치. weakref 가능 타입 목록의 1차 자료|

### 2.2.1 안전점과 식별자

|anchor|내용|
|---|---|
|`Python/ceval_gil.c:806 _PyEval_AddPendingCall`|다른 문맥에서 인터프리터 스레드로 일을 넘기는 CPython 자신의 기관. SharpPy Drain의 원형|
|`Python/ceval_gil.c:903`|`pending->busy` 재진입 방지|
|`Python/bytecodes.c:145`|RESUME의 `eval_breaker` 검사 (`oparg < 2`). 역방향 점프에도 같은 검사|
|`Python/bltinmodule.c:1234 builtin_id`, `:1237`|`id()`는 주소 (`PyLong_FromVoidPtr`). 살아 있는 객체끼리 고유한 근거|

### 2.3 gc 모듈

|anchor|내용|
|---|---|
|`Modules/gcmodule.c:2054 .m_doc`, `:1973 gc__doc__`|`gc` 는 built-in C 모듈. **`Lib/gc.py` 는 없으므로 복사가 아니라 구현**|
|`Modules/gcmodule.c:169`|`gcstate->callbacks = PyList_New(0)` — `gc.callbacks` 는 진짜 리스트|
|`Modules/gcmodule.c:1552`|`invalid generation` (`collect` 의 경계)|
|`Modules/gcmodule.c:1795`|`generation parameter must be less than the number of available generations (3)` (`get_objects` 의 경계. **다른 메시지**)|
|clinic 진입점 vs 자필 `gc_set_threshold`|거부 문구에 모듈 접두가 붙는 경우(`gc.freeze() takes no arguments (1 given)`)와 안 붙는 경우(`set_threshold() takes at least 1 argument (0 given)`)가 갈리는 이유|

### 2.4 종료 순서 — SharpPy가 `__del__` 을 여기 붙이는 근거

|anchor|내용|
|---|---|
|`Python/pylifecycle.c:1573 finalize_modules`|모듈 정리 시작|
|`:1590 finalize_modules_delete_special`|`__module__` 등 재작성|
|`:1609 _PyGC_CollectNoFail`|**1차 컬렉션**|
|`:1630 finalize_modules_clear_weaklist`|약한 참조 목록 정리|
|`:1649 _PyGC_CollectNoFail`|**2차 컬렉션** 이후 잔여 `__del__` 보고|

즉 CPython도 종료 단계에서는 "남은 모든 `__del__` 을 부르고 weaklist를 정리한다".
SharpPy는 종료 sweep에서 같은 결과를 만들어야 한다. 실측상 살아 있는 객체의 순서는 생성 순서가
아니라 **모듈 dict 순서**다 (`docs/plans/lifetime_foundation/probes/q2_shutdown_order.py`).

---

## 3. SharpPy의 대응 구현

**"현재"와 "개정 설계"를 구분해서 읽는다.** 개정 설계는 작업 지시서 Q1~Q4에서 구현한다.

| CPython | SharpPy | 비고 |
|---|---|---|
|`tp_finalize` (타입 생성 시 채움)|`PyClass.DeclaresFinalizer()` — MRO 조회 결과를 클래스에 캐시 | 일반 클래스 인스턴스 생성 비용: 필드 읽기 1회 |
|`PyObject_CallFinalizer` 의 "once" 플래그|현재: `PyFinalizer.Finalized` (강참조 집합). 개정 설계: `PyGuard` 상태| 강참조 집합은 최종화된 객체를 죽지 못하게 하므로 개정 설계에서 삭제 |
|`deallocate` 시점 호출|현재: `PyFinalizer.RunAtShutdown()`뿐이며 그전에 수거되면 **유실**. 개정 설계: `~PyGuard`가 큐에 넣고 안전점 `Drain()`이 호출| 시점 격차는 B방, 유실은 결함 |
|`slot_tp_finalize` 의 타입 조회|같은 방식 (`cls.LookupInMRO("__del__")` 후 self 인자 호출)| A 방: semantics 는 동일 |
|`ClearWeakRefs` + `tp_weaklistoffset` |(미구현) 개정 설계: `ConditionalWeakTable<PyObject, PyGuard>`, ref 쪽은 `Dead` 플래그 + `WeakReference<PyObject>(target, trackResurrection: true)` | CWT 는 **참조 동일성** 비교라 `ToHash()` 오버라이드와 충돌하지 않음. 인스턴스에 weaklist 필드를 두는 방식은 전 객체 +8B 라 기각. **short 약참조 금지**: `__del__`이 볼 수 있는 자식까지 죽어 보인다 (실측) |
|`Python/ceval_gil.c` pending call + `eval_breaker`|(미구현) 개정 설계: RESUME·JUMP_BACKWARD의 정적 플래그 + `PyFinalizer.Drain()`| finalizer 스레드는 enqueue만 하고 Python은 인터프리터 스레드에서만 돈다 |
|`id()` = 주소|현재: `GetHashCode()`라 **값이 같은 객체끼리 같고 충돌한다**. 개정 설계: 첫 호출 시 단조 증가 번호 (CWT)| 압축 GC에서는 주소를 쓸 수 없다 |
|`iobase_finalize`(`Modules/_io/iobase.c:298`) 의 close-on-death|`core/PyResourceBus.cs` | C 방. stream 만 캡처한 release action + 4 개 발화점 + 삭제/이름변경 1 회 재시도. **결과는 동일, 시점은 관측점** (P0-38). Python `close()` dispatch 와 `ResourceWarning` 은 잔여 |
|`gc` 모듈 표면|`modules/GcModule.cs` | 표면·문구 일치(프로브 p76). 값은 우리의 진실 |
|`Py_FinalizeEx` 의 2회 컬렉션|현재: 종료 sweep 1회. 개정 설계: 대기 큐 → `__main__` dict 순서 → 나머지는 생성 순서, 새 대상이 없을 때까지 반복 | `__del__`이 새 객체를 만들 수 있으므로 1회로는 부족 |

---

## 4. 기각한 선택지 (다시 시도하지 말 것)

1. **그림자 참조 카운팅** — 참조를 쓰는 모든 슬롯(VM 스택, 로컬 배열, dict entry, list item,
   속성 슬롯, cell)에 배리어를 둔다.
   기각: 수천 지점 + 쓰기마다 호출 + 그래도 CLR GC 는 동작하므로 장부가 두 개. CPython 을
   C# 으로 다시 쓰는 작업이며, 이 프로젝트의 우선순위(관측 가능한 행동)에 비해 비용이 최대.
2. **CLR finalizer 안에서 Python 코드 실행** — `~PyObject()`에서 `__del__`이나 weakref callback을
   직접 호출하는 방식. (2026-10-31 범위 축소: 금지 대상은 *Python 실행*이다.)
   기각: finalizer 는 별도 스레드이며 **예외가 나면 프로세스가 종료**된다. 그 시점에 모듈
   globals·stdout·인터프리터 상태는 이미 정리됐을 수 있다. Godot 에서는 네이티브 컨텍스트가
   해제된 뒤 호출될 수 있어 하드 크래시. 종료 시 pending finalizer 는 잘릴 수 있어 "반드시
   실행"을 보장하지 못한다.
   **허용:** finalizer가 객체를 큐에 넣고 플래그만 세우는 것. Python은 안전점에서 인터프리터
   스레드가 실행한다. 수거가 한 세대 밀리는 비용은 `__del__`을 선언했거나 weakref가 걸린 객체만
   낸다. 종료 시에는 finalizer를 믿지 않고 등록부를 직접 순회한다.
3. ~~**첫 weakref 생성 시 finalizer 래퍼**~~ — 2026-10-31 철회. "`self`가 없어 구조적으로 불가"는
   틀렸다. Guard가 대상을 강참조하면 Guard의 finalizer가 실행될 때 대상도 살아 있다. 번호는 다른
   문서의 참조 때문에 유지한다.
4. **메모리 풀로 CLR GC보다 우선** — 버퍼 재사용으로는 유효하지만 **수명 모델로는 불가**.
   풀 반환은 "마지막 참조"를 알아야 성립하는데 그 정보가 곧 refcount 다. 게다가
   `PyString.Value` 는 BCL `string` 이라 외부 API 가 강참조를 잡는 순간 반환 불가 → use-after-free.
5. **"CPython 과 다르니 그냥 둔다"로 C 방을 처리** — 파일 잠금/handle 은 실패이므로 불가.

---

## 5. B 방의 격차를 줄이는 3개 앵커

1. **`gc.collect()` 는 `GC.Collect()` + `WaitForPendingFinalizers()` + `Drain()`을 처리할 것이
   없을 때까지 반복한다 (설계 확정, 미구현).** 현재는 열린 파일이 있을 때만 CLR GC를 강제한다.
   → CPython 과 같은 reconciliation 앵커가 생긴다.
   `del x; gc.collect(); assert ref() is None` 이 성립하고, weakref callback이 실제 사건으로
   실행되므로 `Lib/weakref.py`의 `len(weak_dict)`도 별도 처리 없이 그 지점에서 정확해진다.
   **검증 완료 (2026-10-31)**: 29개 시나리오에서 VM 스택·프레임·예외 경로가 대상을 붙잡지
   않았다. 회귀 시험은 `docs/plans/lifetime_foundation/probes/q2_retention.py`.
   주의: Tier-0 JIT는 `TryGetTarget(out _)`의 숨은 임시 슬롯을 메서드 끝까지 유지해 대상을
   살려 둔다 (실측). 대상을 꺼내는 C# 코드는 `NoInlining` 도우미로 분리한다.
2. **종료 시 finalizer sweep** (구현됨, 개정 대상) — 결정적이고 프로브로 고정 가능.
3. **release bus** (구현됨, P0-38) — `core/PyResourceBus.cs`. OS 개수를 먹는 자원을 약참조로
   감시하고 다음에서 해제한다: `open()` 주도 sweep(기하급 임계치) · `gc.collect()` ·
   **삭제/이름변경이 실패했을 때 1 회 재시도** · 종료 시(finalizer 이후).
   C 방의 결과가 여기서 동일해진다.

   구현에서 지킨 두 가지:
   - release action 은 **대상 객체가 아니라 stream 을 캡처**한다. `this` 를 캡처한 closure 를
     등록하면 등록부가 객체를 강참조하여 약참조가 결코 죽지 않고, 해제는 한 번도 일어나지 않는다.
   - `close()` 오버라이드 dispatch 는 포기했다. CPython 의 `iobase_finalize`
     (`Modules/_io/iobase.c:298`) 은 객체에서 Python `close()` 를 찾아 부르지만, SharpPy 가
     대상이 죽었음을 알아채는 시점에는 객체가 이미 수집되어 dispatch 대상이 없다. 같은 이유로
     weakref 래퍼 방식도 기각되었다 (§4.3).

4. **안전점 Drain** (설계 확정, 미구현) — 할당이 많은 프로그램은 GC가 자주 돌기 때문에
   `gc.collect()` 없이도 `__del__`과 callback이 종료 전에 실행된다. 시점은 B방이지만 유실은 없다.

콜백 시점의 성격: **"살아 있는가" 는 결정적으로 답하고, "언제 콜백이 도는가" 만 격차로 남긴다.**

---

## 6. 실측 근거 (재현 명령)

| 실험 | CPython 3.12.15 | SharpPy |
|---|---|---|
|`scope()` 안에서 지역 객체 버림 (`del1.py`) | `del ran` 이 즉시 출력 | 출력 없음 (종료까지 대기) → **B 방** |
|4000 회 `open(p,'a')` 후 `os.remove(p)` (`lock.py`) | `remove ok` | `remove failed: PermissionError` → release bus 이후 **양쪽 `remove ok`** (p77 고정) |
|같은 방식으로 20000 회 (`lock2.py`) | 통과 | 통과 (handle 은 결국 회수됨) → **누수가 아니라 비결정적 해제**. 재현 시점이 옮겨다니므로 테스트로 고정 불가 |
|`__del__` 2개 객체 종료 순서 (`del3.py`) | 생성 순서, `super().__del__()` 동작, stderr `Exception ignored in:`, exit 0 | 동일 (p76 고정) |
|일반 클래스 인스턴스 300,000 | — | 기준 커밋 대비 `1.00x` |
|`__del__` 선언 클래스 100,000 | — | `1.03x` (WeakReference 등록) |
|버린 `__del__` 객체 300,000 후 종료 | 300,000 호출 | 7,887 호출 → **유실** |
|제너레이터 `finally` (삭제·break·collect·종료 시) | 4건 실행 | 0건 |
|재부활 후 다시 버림 (`q2_del_delivery.py` C) | `__del__` 1회 | 스파이크에서 1회 |
|`id()` (`q1_id_identity.py`) | 값이 같아도 다름, 300,000개 고유 | 값이 같으면 같음, 고유 299,312개, memo 충돌 667건 |
|스파이크 단위 비용 (상대값) | — | `WeakReference`+`List` 233 ns, finalizer enqueue 125 ns. Guard 자체는 SharpPy에서 측정 필요 |
|닫지 않은 `open` 3000 회 (`p38_bench.py`) | 257 ms | 239 ms. 기준 커밋은 174 ms 였지만, 그것은 handle 3000 개를 finalizer 스레드에 밀어 넣고 백그라운드에서 해체한 값이다. 같은 정직한 작업을 CPython 보다 적게 쓴다 (`closed` 0.97x, `compute` 1.00x) |

---

## 7. 금지 규칙 (코드 리뷰 기준)

1. CLR finalizer 에서 **어떤 Python 코드도 실행하지 않는다.** finalizer가 할 수 있는 일은 큐에
   넣기와 플래그 설정뿐이다. 호출한 CLR 멤버가 Python으로 이어질 수 있으면 위반이다.
2. `PyObject`/`PyString` 등에 **참조 카운트 필드를 추가하지 않는다.**
3. `weakref` 를 "있으면 좋은 것"으로 구현하지 않는다. **`ref.clear()`, `ref.alive` 는 3.12 에
   없다**(3.13 에서 추가). 만들면 호환성 실패다.
4. 자원 해제를 `__del__`/finalizer 타이밍에 의존시키지 않는다. `with` 나 release bus 를
   쓴다. **새로운 자원 보유 타입(IO/socket/Godot resource)을 만들면 생성자에서
   `PyResourceBus.Register` 를 부르되, release action 에 `this` 를 capture 하지 말 것**
   (stream 만 capture 하는 정적 메서드로 분리한다). 쓴다.
5. 약참조 등록부(WeakReference 목록)가 무한정 자라지 않게, 관측 시점 또는 Drain 시점에 dead
   항목을 제거한다.
6. `gc` 의 값을 "모른다"가 아니라 **우리가 truly 답하는 값**(0, 빈 목록)으로 답하고, 그 사실을
   문서에 남긴다. introspection 이 거짓 청신호를 켜는 것을 방치하지 않는다.

---

## 8. 새 CPython 으로 올릴 때 확인할 것

- 3.13+: `ref.clear()`, `ref.alive`, `PyWeakref_GetRef`, `PyWeakref_UnsafeIsAlive` 등 신규 표면.
- `gc.get_stats()`/generation 수 변화, `Py_Finalize` 재작성(free-threading) 으로 인한
  `finalize_modules` 라인 이동.
- `Py_TPFLAGS_HAVE_GC`/`tp_weaklistoffset` 변경 → weakref 가능/불가 타입 목록 재실측 필요.
- 3.13 계열에서는 free-threading 과 allocator 교체로 파일 위치·함수 이름이 이동했을 수 있다.
  이 문서의 앵커는 **3.12.15 pin 기준**이므로, 버전 업 시에는 위 표의 각 줄을 새 소스에서
  다시 grep 해서 확인한 뒤 사용한다(줄 번호를 믿지 말고 함수 이름으로 찾을 것).

관련 문서: `docs/plans/lifetime_foundation/README.md` (개정 설계의 구현 지시서),
`docs/reports/2026-10-30/gc_module_and_finalizers.md` (P0-37 implementation),
`docs/known_gaps.md` 의 **P0-37 remainder**, `docs/library_surface_policy.md`.
