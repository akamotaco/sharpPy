# 14차 동일 크기 변경을 위한 cursor 이력

13차는 `3a98a0d`로 커밋했다. 이번 변경은 아직 커밋하지 않았다.

## 구현 및 검증 범위

List enumerator의 일괄 invalidation 대신 숫자 위치와 남은 항목 수를 사용하는 cursor를 도입했다. 순서 목록은 평소 compact list로 유지한다. cursor 상태가 존재하는 동안 삭제가 발생하면 최초 한 번만 이력을 복사하고 삭제 항목을 tombstone으로 표시한다. 새 항목은 이력 끝에 추가한다. 따라서 미방문 키를 삭제하고 새 키를 넣는 작은 사례에서 새 키를 읽을 수 있다.

여러 iterator가 이력을 공유하고 각자 위치/remaining을 갖는다. dict의 모든 내부 Add/RemoveAt/Clear 경로를 helper로 통합했다. clear는 entry 위치를 초기화하고 popitem은 trailing tombstone을 줄인다. 기존 크기 오류의 sticky 상태와 정상 exhaustion은 유지한다.

키 목록에서 상태로 향하는 참조는 WeakReference다. 살아 있는 iterator는 상태를 강하게 참조하지만, iterator가 모두 없어지면 dictionary만으로 이력을 유지하지 않는다. 회수는 GC 시점에 의존하며 다음 접근에서 아직 회수되지 않은 상태를 다시 사용할 수 있다. iterator를 오래 보관한 채 같은 크기 삭제·삽입을 반복하는 경우를 위해 이력 길이가 `2 * live key count + 16`을 넘으면 compact 목록으로 되돌린다. 이때 숫자 위치는 유지한다.

actual CPython 3.12.15와 [새 회귀](../../../tests/dict_cursor_mutation_regression.py)를 비교했다. keys/values/items의 미방문 키 교체, 방문한 키 삭제 후 예상보다 많은 entry 감지, clear/update, 복수 iterator, setdefault, popitem tail 처리 및 100회 반복 변경이 일치했다. 기존 [same-size fixture](../../../tests/compatibility/dict_iterator_same_size.py)도 통과한다. 기준 binary는 이 두 fixture가 실패했다.

고정 CPython [dictobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c)의 di_pos/len, dictresize 및 popitem의 dk_nentries 축소를 확인했다.

## 남은 구조 차이

이 구조는 CPython의 전체 entry 저장소 구현이 아니다. 위의 이력 상한은 자체 메모리 정책이며 CPython hash table 용량·resize·compaction 기준과 다르다. [resize compaction fixture](../../../tests/compatibility/dict_iterator_resize_compaction.py)는 CPython에서 통과하고 기준/최종 sharpPy에서 실패한다. 따라서 same-size mutation의 모든 결과가 동일하다고 주장하지 않는다.

첫 삭제의 이력 생성은 O(n) 복사이며 이후 삭제도 이력을 탐색한다. 긴 mutation에서는 자체 압축으로 cursor가 다른 항목을 건너뛸 수 있다. 이 경로의 메모리 상한과 동작 사례를 확인했지만 장시간 메모리 stress 측정이나 사용자 정의 키의 hash 재진입을 검증한 것은 아니다. values/items는 여전히 hash 저장소를 다시 조회한다. module namespace와 view set 연산은 기존 미해결 항목이다.

## 최종 회귀

Release 본체/harness 빌드 성공. 전체 49개: 출력 일치 43, 기존 출력 차이 4, native no_reported_failure 2. [전체 결과](round14-tests.json), [기준 focused](round14-before.json), [최종 focused](round14-focused.json). resize compaction 실패는 전체 성공 회귀 집계와 분리했다.

## 비용 평가

기준 commit을 git archive로 추출해 Release 빌드하고 동일한 harness로 전후 process 3쌍을 교차 실행했다. 각 process는 warmup 10회와 sample 10개를 사용했다. 최종 DLL을 측정했고 builds는 측정과 분리했다. [native raw 및 DLL hash](round14-probe.json), [12개 VM workload raw/IQR](round14-workloads.json).

| Native probe | 전 할당 B | 후 할당 B | 전 중앙값 ms | 후 중앙값 ms |
| --- | ---: | ---: | ---: | ---: |
| dict 생성/삽입/pop 10,000회 | 3,440,000 | 3,520,000 | 0.92485 | 0.95170 |
| keys 생성+100 Next 1,000회 | 72,000 | 64,000 | 1.76015 | 2.31610 |
| values 생성+100 Next 1,000회 | 72,000 | 64,000 | 4.32580 | 4.66160 |
| items 생성+100 Next 1,000회 | 7,272,000 | 7,264,000 | 5.86740 | 6.52770 |

dict 생성당 8B가 늘고 반복 iterator의 객체 크기는 줄었다. native 순회 중앙값은 keys 약 31.6%, values 약 7.8%, items 약 11.3% 증가했다. iterator 최초 상태/weak reference 생성 비용은 warmed-up 동일 dict의 반복 생성 probe에 대부분 포함되지 않으며 삭제 이력 복사 비용도 이 probe에 포함되지 않는다. 따라서 이번 변경은 속도 최적화 성공으로 평가하지 않는다.

일반 VM workload의 시간 IQR은 전후 모두 겹쳤다. dictionary의 총 할당은 3,926,592→3,926,600B, locals/frame 관찰은 각각 8B, attributes/class 관찰은 각각 32B 증가했다. 나머지 고정 할당 workload는 동일했다. inplace_addition의 기준 일부 sample은 할당값이 달라 raw 전체를 보존했으며 이번 구조 변경으로 설명하지 않는다. 전체 VM 속도 개선이나 완전한 무회귀를 주장하지 않는다.

변경 목적은 검증된 mutation 호환성 확대다. namespace 통합의 저장소 후보로 검토하되, native cursor의 추가 간접 참조와 first-use 비용을 줄이는 후속 검증이 필요하다. CPython의 용량 정책까지 복제할 필요와 구현 비용도 별도로 평가한다.

## 측정 도구 오류 기록

초기 PowerShell 측정 명령에서 단일 인자 배열이 문자열로 풀려 harness에 문자 인자가 전달됐다. 잘못된 '-' 파일 인자로 FileNotFoundException이 발생해 .NET Host 종료가 여섯 번 기록됐다. 해당 측정은 폐기하고 literal 인자로 재실행했다. 위 자료는 재실행 및 최종 코드 측정 결과다. 이 종료를 sharpPy의 interpreter 장애나 앞서 조사한 Terminal XAML 오류로 해석하지 않는다.
