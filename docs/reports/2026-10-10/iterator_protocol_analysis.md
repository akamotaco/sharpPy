# 58차: 사용자 객체의 iterator 프로토콜

57차 변경을 `49a37ea` (`fix: dispatch SEND through Python iterator protocols`)로 커밋한 뒤 진행했다. 이번 변경은 검토를 위해 미커밋으로 남긴다.

## 기준과 수정

기준은 실제 conda CPython 3.12.15 및 CPython commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/abstract.c` (`PyObject_GetIter`, `PyIter_Check`), `Objects/typeobject.c` (`slot_tp_iter`), `Objects/iterobject.c` (`iter_iternext`)다. 마지막 파일은 같은 commit에서 추가로 내려받았다.

`PyClassInstance.GetIterator`가 인스턴스의 `__iter__`를 우선 사용하고 반환값을 검증하지 않았다. 타입 MRO의 특수 메서드를 descriptor 규칙으로 바인딩하도록 바꾸고, 반환 객체에 next 슬롯이 있는지 검사한다. `__iter__ = None`은 sequence fallback을 금지한다. `__next__ = None`은 슬롯 자체는 존재하므로 iter 단계에서는 허용하고 실제 next 호출에서 오류가 난다. 슬롯 확인 중에는 next descriptor를 실행하지 않는다.

CPython은 iter descriptor **조회** 오류를 지우고 getitem 조회를 시도하지만, 바인딩된 iter 메서드 **호출** 오류는 전파한다. 두 경로를 구분했다. 기존 dict subclass의 별도 저장소 경로는 유지한다.

C의 슬롯 테이블을 복제하거나 reflection을 사용하지 않는다. 내부 virtual `IsIterator`로 native iterator의 능력을 표시하고, 사용자 클래스는 현재 타입 MRO에서 next 존재 여부를 확인한다. 타입 변경을 놓칠 수 있는 새 캐시는 만들지 않았다. 별도로 PyObject를 상속하는 deque iterator에도 이 표시 및 자기 자신을 반환하는 GetIterator를 적용했다.

sequence fallback은 인스턴스 getitem 속성을 무시한다. IndexError/StopIteration만 종료로 바꾸고, 종료 시 sequence 참조를 해제해 반복 호출을 막는다. 다른 예외는 그대로 전파하고 인덱스를 유지하므로 재시도가 가능하다. 사용되지 않는 전역 호출 카운터를 제거하고 인덱스는 long 및 최대값 검사로 바꿨다.

## 검증

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 110개: 출력 완전 일치 105개, 지정된 변동 필드 제외 일치 3개, native 검사 2개 통과.
- 신규 iter/sequence 회귀와 기존 invalid iterator 재현은 기준 DLL에서 실패하고 수정 후 통과한다. descriptor 조회 오류 fallback 회귀는 기존 구현도 통과했으며 이번 조회 구조 변경 중 발견한 회귀를 방지한다.
- 인스턴스 특수 메서드 무시, descriptor 바인딩 시점, staticmethod, None 슬롯, 예외 identity, dict subclass, deque iterator, sequence 종료 후 재호출 및 오류 후 재시도를 검사한다.
- Godot 소스 직접 포함 빌드 및 회귀 38개, native range helper 통과. SharpPy 외부 DLL 참조 없음. 모바일 실기 검사는 수행하지 않았다.
- Godot 테스트 프로젝트에는 Python collections 파일이 없어 최초 신규 fixture가 import에서 실패했다. deque 구현을 직접 검사하도록 CPython에도 있는 `_collections`를 사용한 최종 fixture로 변경했고, CPython 비교와 Godot 실행을 다시 통과했다.
- `git diff --check` 통과.

전체 로그는 `.artifacts/round58-full.json`, 수정 전 재현은 `round58-before-targeted.json`, fixture 최종 재검사는 `round58-fixture-final.json`, 측정 원본과 요약은 [round58-validation.json](round58-validation.json)에 있다.

## 성능

.NET 8.0.31, tiered compilation 비활성, 각 프로세스 warmup 10회 후 10개 표본, 전후 교대 3쌍. 각 표본은 1만 회이며 결과 합계도 확인한다. 이전 DLL과 새 DLL을 같은 harness에서 실행했다. 빌드·회귀 실행 완료 후 단독으로 측정했다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| 사용자 iterator의 iter + next | 30.921 | 31.286 | 18,483,392 | 18,243,392 |
| range yield from | 58.448 | 58.534 | 6,962,560 | 6,962,560 |

시간은 프로세스별 중앙값 3개의 중앙값이다. 각각 약 1.2%, 0.15% 증가했지만 전후 프로세스 범위가 겹쳐 유의한 개선·회귀로 단정하지 않는다. 사용자 iter 호출의 빈 인자 배열 할당을 없애 1만 회당 240,000 B를 확실히 줄였다. 반환값 검증은 iterator 획득 시 수행하며 매 반복마다 추가하지 않는다. sequence 경로의 시간 개선은 이번 측정으로 주장하지 않는다.

## 다음 순서와 한계

`PyClass.GetIterator`의 metaclass 경로는 별도 구현이라 같은 반환값 검증이 빠져 있다. `tests/compatibility/metaclass_invalid_iterator.py`에서 CPython은 TypeError를 내지만 현재 SharpPy는 int를 허용하는 것을 재현했다. 이 실패는 기본 통과 목록에 넣지 않았다. 다음은 metaclass iteration의 조회·반환 검증을 조사한다.

일반 반복자 전체의 호환성을 완성했다고 주장하지 않는다. native iterator 타입 정보, sequence length hint, 이전 보고서의 generator throw 및 close lookup 예외 처리 차이도 남아 있다. long 최대 인덱스 경계는 소스와 대조했으나 Python에서 해당 횟수의 반복 실행은 하지 않았다.
