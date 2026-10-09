# 13차 dict iterator snapshot 제거

12차는 `cdb6194`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 구현과 CPython 비교

keys/values/items iterator가 snapshot 배열·list·LINQ enumerator를 생성하지 않고 PyDict의 순서 목록 enumerator를 보관한다. struct enumerator를 필드로 보관해 별도 boxing도 피한다. values/items는 Next 시점의 저장 값을 읽으며 items tuple은 실제로 읽은 항목만 생성한다. 일반 `iter(dict)`도 같은 key iterator를 사용한다.

생성 시 크기를 기록하고 Next에서 크기 변경을 감지한다. RuntimeError를 한 번 발생시킨 size 오류 상태는 이후 원래 크기로 되돌려도 유지한다. 정상 exhaustion은 이후 변경과 무관하게 StopIteration을 유지하고 원본 dictionary/list 참조를 해제한다.

공식 고정 CPython [Objects/dictobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c)의 dictiter_iternextkey/value/item, di_used와 di_dict 종료 처리 및 di_pos/len 검사를 확인했다. actual CPython 3.12.15와 [회귀](../../../tests/dict_iterator_regression.py)를 비교했다. 값 교체, growth/shrink 감지, sticky size failure, 정상 exhaustion 후 변경, 일반 dict iteration을 검증한다. 기존 [live value iterator fixture](../../../tests/compatibility/dict_view_iterator_live_values.py)도 이제 통과한다.

## 미해결 범위

현재 List enumerator는 모든 순서 목록 변경에 invalidation을 적용한다. 같은 크기에서 미방문 키를 삭제하고 새 키를 넣으면 CPython은 새 키를 방문할 수 있지만 현재 구현은 keys changed RuntimeError를 발생시킨다. [same-size fixture](../../../tests/compatibility/dict_iterator_same_size.py)는 CPython에서 통과하고 기존/최종 sharpPy에서 모두 실패한다. 따라서 완전한 CPython iterator mutation 호환성을 주장하지 않는다. 안정적인 entry cursor 또는 tombstone 저장소가 필요하다.

values/items는 저장 키로 Dictionary를 다시 조회하므로 CPython처럼 entry에서 직접 값을 읽는 구조가 아니다. 사용자 정의 키의 hash/동등성 부작용·재진입은 이번 검증 범위에 포함하지 않았다. length_hint, reverse iterator, view set 연산 및 module namespace의 stale 저장소도 후속 범위다.

## 회귀 결과

Release 본체와 harness 빌드 성공. 전체 평가 48개: 출력 일치 42, 기존 출력 차이 4, native no_reported_failure 2. [전체 결과](round13-tests.json), [수정 전 focused](round13-before.json), [수정 후 focused](round13-focused.json). 후자의 동일 크기 fixture 실패는 별도로 보존하며 성공 회귀 집계에 포함하지 않았다.

## native probe

`--dict-iterator-probe`와 `--dict-iterator-probe --traverse`를 추가했다. 100개 키의 dict에서 각각 iterator 생성+첫 Next(10,000회), 생성+100 Next(1,000회)를 측정한다. Python VM과 종료 StopIteration은 측정하지 않는다. 특히 첫 Next 측정을 전체 순회 성능으로 해석하지 않는다.

기준 cdb6194 소스를 git archive로 추출해 별도 Release 빌드했다. 동일한 최종 harness를 기준/최종 DLL과 함께 실행했다. 모드별 전후 process 3쌍을 교차 실행하고 각 process에서 warmup 10회 및 sample 10개를 수집했다. 모든 빌드는 측정 전에 완료했다. 숫자는 총 30 sample 중앙값이며 raw 자료 및 두 DLL hash는 [probe 결과](round13-probe.json)에 보존했다.

| 생성+첫 Next 10,000회 | 전 ms | 후 ms | 전 할당 B | 후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| keys | 2.88130 | 0.43160 | 26,080,000 | 720,000 |
| values | 11.26115 | 0.51050 | 26,080,000 | 720,000 |
| items | 15.73875 | 0.61635 | 98,640,000 | 1,440,000 |

| 생성+100 Next 1,000회 | 전 ms | 후 ms | 전 할당 B | 후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| keys | 2.30270 | 1.78420 | 2,608,000 | 72,000 |
| values | 4.80175 | 4.43215 | 2,608,000 | 72,000 |
| items | 7.39305 | 6.34660 | 9,864,000 | 7,272,000 |

모든 sample의 할당값은 각 행에서 동일했다. keys/values는 약 97.2% 감소하고, items는 첫 Next 약 98.5%, 전체 100개 방문 약 26.3% 감소했다. full traversal의 items tuple 할당은 여전히 남는다. 시간 수치는 이 native workload에 한정된 관찰이며 일반 Python 애플리케이션 개선율로 확대하지 않는다.

## 다음 후보

same-size mutation을 허용할 안정적인 순서 entry cursor를 우선 검토한다. 단순 version guard만으로 CPython 동작을 복제할 수 없다. 이 저장 구조를 namespace 통합과 연결해야 하며 일반 dict의 조회/삽입/삭제 비용 및 메모리를 함께 측정해야 한다.
