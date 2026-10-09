# 23차: 인덱스 변환과 memoryview 재진입

22차 명시적 해제는 `f5c3afe`로 커밋했다. 이번 변경은 bool, int 서브클래스, 사용자 `__index__`를 memoryview의 인덱스/바이트 값으로 사용하는 동작과 예외 순서를 맞춘다.

## 자동 해제 조사 결과

`PyStack`은 CLR List에 참조를 저장하며 `STORE_FAST`/`DELETE_FAST`는 LocalsPlus를 대입/초기화한다. PyObject, scope, stack에는 Python 참조 계수 증감 체계가 없다. 따라서 `DELETE_FAST`나 `del`에 `Release`를 붙이면 list·dict·다른 변수 등에 남은 view까지 해제할 수 있다. 이 잘못된 접근을 방지하도록 list에 view를 보관한 뒤 이름만 삭제하는 회귀를 추가했다.

CLR finalizer만으로도 CPython의 마지막 참조 제거 시 즉시 해제를 보장할 수 없다. 자동 해제의 수정 범위는 stack/locals뿐 아니라 속성·컨테이너·closure·호출/반환·native 저장소의 참조 소유권이다. 이번에는 이 기반 없이 별도 추적 정책이나 강제 GC를 추가하지 않았다. `memoryview_automatic_release.py`의 기존 실패는 **미해결**이며, 전체 수명 설계가 필요한 항목으로 분류한다.

## CPython 근거와 구현

[CPython 3.12.15 _PyNumber_Index](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/abstract.c#L1397)는 int 입력의 원래 값을 사용하고 그 밖의 객체는 타입의 index slot을 호출한다. 기존 `PyObject.TryGetIndex`는 인스턴스 속성을 조회하고 모든 예외를 삼켰다. 기존 type-only lookup과 descriptor binding을 사용하도록 고쳤으며, 메서드가 없는 경우만 null을 반환한다. bool은 기존 작은 정수 캐시를 사용하고 int subclass는 내부 값을 읽는다. 일반 정수 경로를 작게 유지하도록 Python 호출 경로를 분리했다.

memoryview 읽기/쓰기는 이 공통 변환을 사용한다. [memory_ass_sub와 pack_single](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/memoryobject.c#L2609)의 타입/범위 오류 변환을 반영했다. 바이트 쓰기는 C unsigned long에 해당하는 범위로 먼저 변환하고 해제 상태와 0–255 범위를 검사한다. Windows의 unsigned long은 32비트이므로 현재 x64 Windows에서도 uint 범위를 사용한다. 이 순서는 사용자 __index__가 view를 해제하는 경우의 예외 결과에도 영향을 준다.

[memory_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/memoryobject.c#L2578)는 slice 경계의 사용자 코드를 실행하기 전에 자식 view를 등록한다. 부모가 콜백에서 해제돼도 자식은 유효해야 한다. 같은 순서로 생성하고, 경계 변환 실패 시 자식 export를 해제한다. 정수 index/value 콜백 후에는 released 상태를 다시 검사한다.

## 검증

새 `tests/memoryview_index_regression.py`는 actual CPython 3.12.15와 다음을 비교한다: bool, int subclass override 우회, type-only lookup, 잘못된 반환 타입, 사용자 예외, 큰 인덱스 메시지, 바이트 범위 및 오류 변환, index/value 콜백 중 release, slice 생성 중 부모 release, 실패한 slice의 export 정리. 마지막 참조 문제와 별칭 보호는 기존 release 회귀에도 추가했다.

기준 DLL은 신규 회귀에 실패하고 변경 후 통과했다. Release 본체/harness 빌드 성공. 전체 기본 검사 59개: raw 출력 일치 54, 시간·주소 필드만 다른 3, native no_reported_failure 2. 새 실패 없음. 공통 index 함수 변경의 영향은 기존 indexing/slicing을 포함한 전체 회귀로 확인했다.

검증 범위를 모든 숫자 프로토콜로 확장해 주장하지 않는다. 특히 사용자 __index__가 정확한 int가 아닌 strict subclass를 반환할 때 CPython이 내는 DeprecationWarning 경로는 이번에 완성하지 않았다. slice 대입과 자동 해제도 남아 있다.

## 성능 선별

같은 최종 harness에 기준/변경 DLL만 교체했다. 전후 process 3쌍, 각 10 warmup/10 sample, 1,024바이트 exporter. 생성/slice는 10,000회, 읽기/쓰기는 100,000회다. 쓰기는 인덱스 0에 97을 저장하며 결과를 검사한다. setup/build/다른 테스트는 측정 밖이다.

| 작업 | 전 중앙값 ms (Q1–Q3) | 후 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | ---: | ---: | ---: |
| 생성 | 0.20665 (0.2049–0.2398) | 0.21345 (0.2046–0.3010) | 560,000 동일 |
| slice | 0.48925 (0.4824–0.4927) | 0.49045 (0.4829–0.4943) | 560,000 동일 |
| 읽기 | 1.49525 (1.4915–1.5027) | 1.49580 (1.4799–1.5157) | 0 동일 |
| 쓰기 | 3.50735 (3.4988–3.5252) | 2.28610 (2.2626–2.4456) | 0 동일 |

쓰기 중앙값 약 34.8% 감소. 나머지 시간 분포는 겹쳐 개선을 주장하지 않는다. 중간 구현의 쓰기는 약 10% 느려져 그대로 채택하지 않았다. 반복 BigInteger 비교/변환을 정리한 최종 버전은 위와 같고 중간 측정도 보존한다. Python VM 전체 속도 향상이나 사용자 __index__ 호출의 전후 성능 비교는 주장하지 않는다.

[원자료, DLL hash, 검사 상태](round23-memoryview-index.json). 전체 raw stdout은 `.artifacts/round23-tests-final.json`에 있다.
