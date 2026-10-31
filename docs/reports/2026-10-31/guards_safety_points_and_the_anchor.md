# P0-48 (Q2): 죽음을 놓치지 않는다 — Guard, 안전점, gc.collect() 앵커

기준: CPython `v3.12.15` pin. 기준 커밋 `ad4281c`. 지시서는
[`../lifetime_foundation/README.md`](../lifetime_foundation/README.md) §Q2.

## 원래 실패

| 관측 | CPython | SharpPy (수정 전) |
| --- | --- | --- |
| 버린 `__del__` 객체 300,000개 전달 | 300,000 | **0** (전부 종료 때 몰아서, 프로브 창에는 nothing) |
| `q2_retention` 30개 시나리오 | 29개 1 | **전부 0** |
| 종료 순서 | 모듈 dict 삽입 순 | 생성 역순 섞임 |
| `gc.collect()` | 실제로 수집 | 강제 GC가 파일이 열려 있을 때만 발생 |

## 메커니즘 (CPython ↔ CLR)

CPython은 객체가 자기 죽음을 알고 그 자리에서 finalizer를 부른다
(`Objects/object.c:357 PyObject_CallFinalizer`, `Objects/typeobject.c:1901 subtype_dealloc`).
CLR에서는 수거만 죽음을 알며, 그건 finalizer 스레드다. 그래서 알림을 둘로 쪼갰다.

```
~PyGuard()            →  큐에 enqueue + PyPending.Flag      (finalizer 스레드, Python 없음)
RESUME / JUMP_BACKWARD →  PyFinalizer.Drain()                (인터프리터 스레드, 안전점)
```

- 안전점 두 곳은 CPython과 같은 위치다. `Python/bytecodes.c:145-146`은 RESUME에서
  `eval_breaker && oparg < 2`를 본다 — `send`로 재개한 프레임은 끊지 않는다.
  `JUMP_BACKWARD_NO_INTERRUPT`에는 넣지 않았다(이름 그대로 CPython도 안 본다).
- `PyGuard.Owner`는 **강참조**다. `ConditionalWeakTable`이 value를 key 생존에 종속시키는 성질로
  객체와 가드가 같은 생명을 갖고, 가드가 큐에 들어가면 둘 다 살아나 `__del__`에 `self`를
  넘긴다. 격리 스파이크로 확인한 설계(300,000 인도 유실 0).
- Drain 재진입은 no-op (`Python/ceval_gil.c:898` "we don't do recursive pending calls").
- `__del__` 실행 전후로 진행 중이던 예외를 저장·복원한다. CPython도
  `slot_tp_finalize`가 `:9076 PyErr_GetRaisedException` / `:9090 PyErr_SetRaisedException`로
  그렇게 한다 (프로브 J). SharpPy의 예외 상태는 프레임에 있으므로 프레임을 스냅숏한다.
- 단 한 번 규칙은 상태 머신이다(`Armed → Finalized → Cleared`). 상태를 **먼저** 바꾼 뒤 호출하므로
  `__del__` 안에서 안전점에 닿아도 두 번 불리지 않는다 (프로브 C 재부활).
- `gc.collect()` 순서는 `Modules/gcmodule.c:1425 start → 수집 → :1427 stop`을 따른다 (프로브 M).
  `gc.disable()`은 Drain을 막지 않는다 — CPython에서 disable은 순환 수집기만 멈춘다 (프로브 K).
- 종료 순서는 `__main__` dict 삽입 순, 나머지는 생성 순 (실측된 순서, 프로브 `q2_shutdown_order`).

## 삭제·교체된 것

- `PyFinalizer`의 `Pending`(약참조 목록)·`Finalized`(강참조 집합)·`Entry` 삭제. `Pending`이
  약참조라 CLR이 먼저 수거하면 호출이 사라졌고, `Finalized`는 최종화된 객체를 영영 못 죽게 했다.
  known_gaps P0-37 잔여 5(O(n) 종료 목록)도 함께 사라졌다(`Live`는 swap-remove O(1), 순서는
  `Sequence`가 보존).
- `PyClass._finalizerChecked` bool → 전역 `FinalizerEpoch` + 캐시된 훅. `InvalidateTypeCache`는
  서브클래스로 전파되지 않으므로 버전 태그로는 안 됐고, 재조회는 `LookupInMRO`가 아니라 클래스
  dict 직접 순회여야 했다(메서드 캐시가 늦은 할당 이전의 "없음" 답을 들고 있었다).
- 등록 지점: `PyClass.CreateInstance` 한 곳 → 생성된 **객체의 실제 타입**으로 판단
  (`RegisterIfDeclared(created)`), `object.__new__` 두 경로 추가 (프로브 H, P).

## 추가·삭제량

실행 코드 **+265 / -41**, 주석 **+184 / -85** (core, runtime, type, modules). 주석이 실행 코드와
비슷한 양인 건 이 작업의 산출물이 "어디서 왜 죽음을 아는지"의 앵커이기 때문이다 — 지시서가
요구한 대로 CPython `file:line`을 코드 옆에 뒀다.

## 게이트

| 게이트 | 결과 |
| --- | --- |
| `q2_del_delivery` / `q2_retention` / `q2_shutdown_order` | 모두 일치 → `p84`/`p85`/`p86`로 복사 |
| bughunt | **91/91** (`p76`, `p77` 포함) |
| evaluate | `225 / 3 / 2` |
| similarity | exit 0 |
| Godot 4.7.2 mono 재연 | 82 matched / 0 |
| Godot 스레드 실측 | 재연 82건 전부 단일 관리 스레드(id 2) — §7의 전제 확인 |

## 성능 (git worktree A/B, 기준 `ad4281c`, 교호 5라운드, 중앙값)

| 작업 | 배수 | 판정 |
| --- | --- | --- |
| 일반 클래스 인스턴스 300,000 | **1.00** | 허용 1.02 이하 ✓ (일반 객체 비용 0) |
| 역방향 점프 1,000만 | **0.98** | 허용 1.02 이하 ✓ (안전점 비용 0) |
| `__del__` 클래스 100,000 | **1.35** | 보고 — 가드 할당 + CWT + CLR finalizer 등록 |
| `gc.collect()` (살아 있는 객체 200,000) | 25.8 ms (기준 0.9 ms) | 기준은 **아무것도 하지 않았던 값**. CPython은 같은 힙 4.6 ms |
| `gc.collect()` 1,000회 | 881 ms (기준 0.5 ms) | 회당 0.88 ms. CPython은 회당 0.15 ms |

즉 일반 객체와 루프는 돈이 없고, 비용을 낸 대상은 (1) `__del__`을 선언한 클래스, (2) `gc.collect()`
자체다. 후자는 앵커가 진짜 GC가 된 결과이며 CPython과 같은 자릿수다.

## 남은 격차 (P0-48 잔여)

- `q2_gap_late_del`: 객체를 먼저 만들고 `Late.__del__`을 나중에 붙이면 그 객체는 finalize되지
  않는다. CPython은 `update_one_slot`이 `tp_finalize`를 다시 채우므로 기존 인스턴스도 죽을 때
  불린다. SharpPy는 생성 시점에 가드를 붙이므로 나중에 붙일 가드를 찾을 수 없다(지시서가 합격
  기준에서 제외한 그대로 남긴다).
- `del x` 직후(수집 없이) 시점은 여전히 다르다. 이건 B방이며 이 작업은 유실을 없애는 작업이다.
- `gc.collect()`의 반환값은 여전히 0이다. 실제로 수집하지만 CLR이 몇 개를 죽였는지 세지 않는다.
- stderr의 `Exception ignored in: <function X at 0x…>` 주소 형식이 다르다
  (SharpPy `0xb8e923`, CPython Windows `0x00000213142A6B60`). 하네스는 stderr를 비교하지 않는다.
- 재부활한 객체에서 `Finalized → Cleared` 단계는 다음 수거에서 온다. Q4의 weakref callback을 이
  단계에 붙이면 재부활 상태에서 callback이 먼저 돌 수 있어서, Q4는 `Dead` 플래그 기준으로 판정한다
  (스파이크 §3의 마지막 줄이 예고한 그대로).
