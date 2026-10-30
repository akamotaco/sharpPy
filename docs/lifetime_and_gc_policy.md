# 수명·gc·weakref 정책 (2026-10-30 확정)

이 문서는 "객체의 수명을 누가 결정하는가"에 대한 SharpPy의 확정 결정과, 그 결정이
CPython과 **어디에서 같아지고 어디에서 영원히 달라지는가**를 기록한다. 코드의 주석은 이 문서를
참조하므로, CPython 소스와 비교할 때는 여기의 앵커 표를 먼저 읽으면 된다.

확정 시점: 2026-10-30, 브랜치 `work/bug-hunt`, 커밋 `3215eff`(gc/`__del__`) 및 그 직전 논의.
참조 구현: CPython 3.12.15 (pin: `cpython-v3.12.15`, `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`).

---

## 1. 결정

> **수명(lifetime)은 CLR GC가 소유한다. 참조 카운팅은 도입하지 않는다.**
> **해제(release)는 GC에 의존하지 않고 소유자가 명시한다.**
> **동작 표면(출력·문구·타입·예외 family)은 계속 CPython과 동일하게 유지한다.**

"결과를 CPython과 동일하게 할까, C# 친화적으로 할까"는 하나의 선택이 아니다. 관측량을 세
방으로 나누고 방마다 답을 정한다.

| 방 | 무엇 | 동일성 | 수단 |
|---|---|---|---|
| **A. 순수 결정적 결과** | 같은 입력 → 같은 출력. 연산, 에러 문구, 타입, `is_tracked`, `get_referents`, `__del__` 배포 semantics | **동일해야 한다** (프로브로 고정) | CPython 소스 이식 |
| **B. 수명 시점 관측** | 대상이 도달불능이 된 *순간*을 프로그램이 읽는 경우: 조기 `__del__`, weakref 콜백 발화 시점, 콜렉트 이전의 `ref()`/`len(weak_dict)` | **동일 불가** (원리적) | 여기만 C# 친화. 아래 3개 앵커로 크기 축소 |
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
|`Objects/object.c:357 PyObject_CallFinalizer`|`tp_finalize` 호출. `:362` 주석 `tp_finalize should only be called once`, `_PyGC_FINALIZED` 플래그 검사, 호출 후 `_PyGC_SET_FINALIZED`|
|`Objects/object.c:374 PyObject_CallFinalizerFromDealloc`|재부활(resurrection) 처리. `:398` "tp_finalize resurrected it!"|
|`Objects/typeobject.c:1901`|`subtype_dealloc` 의 `/* Maybe call finalizer; exit early if resurrected */`|
|`Objects/typeobject.c:9071 slot_tp_finalize`|타입에서 `__del__` 을 찾아 호출 → **오버라이드가 이기고 `super().__del__()` 가 기저에 도달**하는 이유|
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
SharpPy가 종료 sweep을 구현한 이유는 **양쪽 답이 일치하는 유일한 지점**이기 때문이다.

---

## 3. SharpPy의 대응 구현

| CPython | SharpPy | 비고 |
|---|---|---|
|`tp_finalize` (타입 생성 시 채움)|`PyClass.DeclaresFinalizer()` — MRO 조회 결과를 클래스에 캐시 | 일반 클래스 인스턴스 생성 비용: 필드 읽기 1회 |
|`PyObject_CallFinalizer` 의 "once" 플래그|`PyFinalizer.Finalized` (집합)| **주의**: `PyObject.GetHashCode()` 는 Python `ToHash()` 를 오버라이드한다. 참조 동일성 comparer 로 교정 예정 (known_gaps P0-37 remainder 9) |
|`deallocate` 시점 호출|`PyFinalizer.RunAtShutdown()` (`FileExecutor.ExecuteFile` / `ExecuteCodeString` 종료 직전)| **B 방 격차의 정체**. 조기 도달 객체는 종료까지 대기 |
|`slot_tp_finalize` 의 타입 조회|같은 방식 (`cls.LookupInMRO("__del__")` 후 self 인자 호출)| A 방: semantics 는 동일 |
|`ClearWeakRefs` + `tp_weaklistoffset` |(미구현) planned: `ConditionalWeakTable<PyObject, List<PyWeakReference>>` + ref 쪽 `WeakReference<PyObject>` | CWT 는 **참조 동일성** 비교라 `ToHash()` 오버라이드와 충돌하지 않음. 인스턴스에 weaklist 필드를 두는 방식은 전 객체 +8B 라 기각 |
|`gc` 모듈 표면|`modules/GcModule.cs` | 표면·문구 일치(프로브 p76). 값은 우리의 진실 |
|`Py_FinalizeEx` 의 2회 컬렉션|종료 sweep 1회 | 재부활 미구현이므로 1회로 충분 |

---

## 4. 기각한 선택지 (다시 시도하지 말 것)

1. **그림자 참조 카운팅** — 참조를 쓰는 모든 슬롯(VM 스택, 로컬 배열, dict entry, list item,
   속성 슬롯, cell)에 배리어를 둔다.
   기각: 수천 지점 + 쓰기마다 호출 + 그래도 CLR GC 는 동작하므로 장부가 두 개. CPython 을
   C# 으로 다시 쓰는 작업이며, 이 프로젝트의 우선순위(관측 가능한 행동)에 비해 비용이 최대.
2. **대상 객체에 CLR finalizer 연결** — `__del__` 을 `!PyObject` 로.
   기각: finalizer 는 별도 스레드이며 **예외가 나면 프로세스가 종료**된다. 그 시점에 모듈
   globals·stdout·인터프리터 상태는 이미 정리됐을 수 있다. Godot 에서는 네이티브 컨텍스트가
   해제된 뒤 호출될 수 있어 하드 크래시. finalizable 객체는 수거가 한 세대 밀려 전체 메모리
   피크가 오른다. 종료 시 pending finalizer 는 잘릴 수 있어 "반드시 실행"을 보장하지 못한다.
3. **첫 weakref 생성 시 finalizer 래퍼** — 대상이 죽으면 `self` 가 없으므로 `__del__` 을 부를 수
   없다. 구조적으로 불가.
4. **메모리 풀로 CLR GC보다 우선** — 버퍼 재사용으로는 유효하지만 **수명 모델로는 불가**.
   풀 반환은 "마지막 참조"를 알아야 성립하는데 그 정보가 곧 refcount 다. 게다가
   `PyString.Value` 는 BCL `string` 이라 외부 API 가 강참조를 잡는 순간 반환 불가 → use-after-free.
5. **"CPython 과 다르니 그냥 둔다"로 C 방을 처리** — 파일 잠금/handle 은 실패이므로 불가.

---

## 5. B 방의 격차를 줄이는 3개 앵커

1. **`gc.collect()` 는 실제로 `GC.Collect()` + `WaitForPendingFinalizers()` 를 수행한다 (planned).**
   → CPython 과 같은 reconciliation 앵커가 생긴다.
   `del x; gc.collect(); assert ref() is None` 가 통과할 수 있고, 조회 시점에 dead 항목을
   own refs 에서 제거하면 `len(weak_dict)` 도 그 지점에서 정확해진다.
   **검증 요구**: VM 스택/스코프 캐시가 대상을 붙잡으면 성립하지 않는다. `_weakref` 1단계에서
   가장 먼저 프로브로 확인할 것.
2. **종료 시 finalizer sweep** (구현됨) — 결정적이고 프로브로 고정 가능.
3. **release bus** (planned) — OS 개수를 먹는 타입(io/socket/Godot resource)만 `with` exit /
   스코프 종료 / 종료 시 확정 해제. C 방의 결과를 동일하게 만든다.

콜백 시점의 성격: **"살아 있는가" 는 결정적으로 답하고, "언제 콜백이 도는가" 만 격차로 남긴다.**

---

## 6. 실측 근거 (재현 명령)

| 실험 | CPython 3.12.15 | SharpPy |
|---|---|---|
|`scope()` 안에서 지역 객체 버림 (`del1.py`) | `del ran` 이 즉시 출력 | 출력 없음 (종료까지 대기) → **B 방** |
|4000 회 `open(p,'a')` 후 `os.remove(p)` (`lock.py`) | `remove ok` | `remove failed: PermissionError` → **C 방, 현재 실패** |
|같은 방식으로 20000 회 (`lock2.py`) | 통과 | 통과 (handle 은 결국 회수됨) → **누수가 아니라 비결정적 해제**. 재현 시점이 옮겨다니므로 테스트로 고정 불가 |
|`__del__` 2개 객체 종료 순서 (`del3.py`) | 생성 순서, `super().__del__()` 동작, stderr `Exception ignored in:`, exit 0 | 동일 (p76 고정) |
|일반 클래스 인스턴스 300,000 | — | 기준 커밋 대비 `1.00x` |
|`__del__` 선언 클래스 100,000 | — | `1.03x` (WeakReference 등록) |

---

## 7. 금지 규칙 (코드 리뷰 기준)

1. CLR finalizer 에 **어떤 Python 코드도 연결하지 않는다.**
2. `PyObject`/`PyString` 등에 **참조 카운트 필드를 추가하지 않는다.**
3. `weakref` 를 "있으면 좋은 것"으로 구현하지 않는다. **`ref.clear()`, `ref.alive` 는 3.12 에
   없다**(3.13 에서 추가). 만들면 호환성 실패다.
4. 자원 해제를 `__del__`/finalizer 타이밍에 의존시키지 않는다. `with` 나 release bus 를 쓴다.
5. 약참조 등록부(WeakReference 목록)가 무한정 자라지 않게, 관측 시점에 dead 항목을 제거한다.
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

관련 문서: `docs/reports/2026-10-30/gc_module_and_finalizers.md` (P0-37 implementation),
`docs/known_gaps.md` 의 **P0-37 remainder**, `docs/library_surface_policy.md`.
