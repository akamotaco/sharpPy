# 20차: dict 서브클래스 copy

19차 최적화는 `38f6956`으로 커밋했다. 이번 변경은 `dict.copy`가 Python dict 서브클래스를 거부하는 오류를 수정한다. 기존 `GetDict`로 실제 저장소를 얻으며, 일반 `PyDict`는 이전과 같은 직접 `Copy()` 호출을 유지한다.

## CPython과 비교한 동작

CPython 3.12.15 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 [PyDict_Copy](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c#L3013), [dict_merge](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c#L2811), [PyMapping_Keys](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/abstract.c#L2394)를 확인했다.

- 빈 실제 저장소는 콜백 없이 빈 dict로 복사한다.
- 기본 iteration을 상속하면 `keys`/`__getitem__`/`__len__` 재정의를 호출하지 않고 최적화된 저장소 복사를 사용한다.
- 타입의 `__iter__`를 재정의하면 `keys()`와 `__getitem__()`으로 복사한다. `__iter__` 자체는 호출하지 않는다. 인스턴스 속성은 판정에서 제외한다.
- `keys()`의 반환값이 정확한 list이면 그대로 사용한다. 그 밖의 iterable은 값 조회 전에 materialize한다. 따라서 list의 값 조회 중 append와 generator의 실행 순서가 서로 다르다.
- 키 수집 중 예외와 값 조회 중 StopIteration은 그대로 전파한다. 비 iterable 반환의 TypeError 메시지도 비교했다.

새 캐시나 별도 저장소는 추가하지 않았다. 기존 type-only uncached lookup을 사용하므로 클래스 속성의 런타임 변경도 관찰한다. 기존 `AsList`는 iterator 예외를 TypeError로 바꾸므로 이 경로에서는 사용하지 않는다.

## 검증 및 비용

새 회귀 `tests/dict_copy_subclass_regression.py`는 위 분기와 다단계 상속, shallow reference, 순서, 독립 저장소, 부적합 receiver를 actual CPython 3.12.15와 비교한다. 기준 DLL은 실패하고 변경 후에는 통과했다. 기존 compatibility 재현도 통과한다.

Release 본체/harness 빌드 성공. 전체 55개는 출력 일치 49, 기존 출력 차이 4, native no_reported_failure 2로 새 실패가 없다. 일반 dict 회귀도 통과한다.

19차의 일반 Python `dictionary_copy` workload를 동일 harness, 전후 process 3쌍, process별 10 warmup/10 sample로 다시 측정했다. 100항목 dict를 10,000회 복사하고 조회·합산하며 각 실행 결과 49,995,000을 검증했다.

| 항목 | 전 | 후 |
| --- | ---: | ---: |
| 중앙값 ms | 26.57995 | 26.86095 |
| Q1–Q3 ms | 26.3885–26.8501 | 26.5739–27.7749 |
| 할당 B | 43,302,616 | 43,302,616 |

시간 중앙값은 약 1.1% 증가했지만 분포가 겹친다. 속도 개선이나 성능 동일성을 확정하지 않는다. 측정한 일반 경로의 할당은 동일하며 19차의 복사 최적화는 유지된다. 서브클래스 경로는 기준 구현이 실패하므로 성공한 작업끼리의 전후 성능 비교를 주장하지 않는다.

[원자료, DLL hash, 테스트 상태](round20-copy-subclass.json). 전체 stdout은 로컬 `.artifacts/round20-tests.json`에 있다.

이번 검증은 copy의 위 분기에 한정한다. 모든 Python descriptor/iteration slot의 구현 완료를 뜻하지 않는다. module 이중 저장소와 일반 dict의 resize 중 cursor 결과 차이는 여전히 남아 있으며, 메서드별 snapshot 보정 없이 저장소 설계에서 해결할 대상으로 유지한다.
