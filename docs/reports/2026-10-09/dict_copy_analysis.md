# 19차: 일반 dict 얕은 복사

기준 commit: `72300ac`. CPython의 관찰 가능한 결과를 유지하면서 C# 저장소의 복사 기능을 사용한다. C 구조를 번역하거나 자체 동작 정책을 추가하지 않는다.

## 변경과 근거

`PyDict.Copy`의 키별 조회·삽입 반복문을 동일 comparer를 사용하는 `Dictionary` 복사와 삽입 순서 목록 복사로 대체했다. 저장된 해시와 key/value 참조를 재사용하고, 목록과 hash 저장소는 독립적으로 생성한다. CLR Dictionary의 열거 순서는 삭제 후 재삽입 시 Python 순서와 다르므로 `_keys`를 별도로 복사해야 한다. iterator 이력은 복사하지 않는다.

[CPython 3.12.15 dictobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/dictobject.c#L3013)의 `PyDict_Copy`는 빈 dict, split table, compact table 및 merge 경로를 구분한다. 이 변경은 C의 메모리 배치를 이식하지 않고 기존 .NET 저장소의 기능을 사용한다. native 단일 key probe에서 복사 중 `ToHash` 호출은 전 2회, 후 0회였다. Python 사용자 정의 `__hash__` dispatch까지 검증한 결과는 아니다.

`PyGlobalsDict.Copy` override는 이번 변경 대상이 아니다. 기존 module 이중 저장소와 cursor 압축 차이는 해결되지 않았다.

## 검증

Release 본체와 harness 빌드 성공. 전체 54개: 출력 일치 48, 기존 출력 차이 4, native no_reported_failure 2. 신규 일반 dict 회귀는 CPython 3.12.15 및 변경 전후 모두 통과했다. 빈 dict, 삭제/재삽입 순서, mixed key, bool/int 동등 key, 얕은 참조 공유 및 저장소 독립성을 검사한다.

추가로 발견한 `dict.copy(dict_subclass)` 거부는 변경 전에도 실패했다. `tests/compatibility/dict_copy_subclass.py`에 재현을 보존한다. 기본 회귀 통과 수에 포함하지 않는다. 기존 same-size mutation/resize 차이를 포함해 모든 dict 동작이 동일하다는 의미는 아니다.

## 성능

동일 Release harness에 기준/변경 DLL만 교체했다. 전후 순서로 3쌍의 별도 process, 각 10 warmup 및 10 sample. 시간은 30개 sample 중앙값, 대괄호는 Q1–Q3이다. 준비와 build는 측정에서 제외했다. native 측정 동안 Python 테스트를 실행하지 않았다.

| 작업 | 전 ms | 후 ms | 전 할당 B | 후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| native 100항목 × 1,000 copy | 7.26585 [6.7710–7.7471] | 0.66625 [0.6280–0.7679] | 12,432,000 | 4,032,000 |
| native 1,000항목 × 1,000 copy | 27.38865 [26.2178–28.3384] | 5.85075 [5.5217–6.3326] | 118,864,000 | 39,120,000 |
| Python VM 100항목 × 10,000 copy 및 조회·합산 | 52.32990 [50.8385–54.5949] | 28.26620 [27.3797–28.8413] | 127,302,616 | 43,302,616 |

Python workload 시간은 약 46.0%, 할당은 약 66.0% 감소했다. 모든 호출 결과는 49,995,000으로 검증했다. CPython에서도 같은 workload의 결과를 검증했으며 시간은 약 2.4ms였다. 따라서 이번 최적화가 전체 인터프리터의 성능 격차를 해결했다는 주장은 하지 않는다.

native 빈 dict 복사는 0.13920→0.16035ms로 중앙값이 증가했으나 IQR이 겹치며 할당은 동일하다. 빈 dict의 성능 개선은 확인하지 못했다. 삭제된 슬롯을 가진 5/100/1,000 크기 저장소도 측정했고 원자료에 포함했다. 서로 다른 native/VM warmup 문맥의 수치는 직접 합산하지 않는다.

[측정 원자료, DLL/source hash, 테스트 상태](round19-copy.json). 전체 테스트 stdout은 로컬 `.artifacts/round19-tests.json`에 있다.

## 다음 작업의 기준

module 저장소는 실제 namespace와 관찰용 dict를 하나의 변경 경로로 연결할 설계가 필요하다. 메서드별 snapshot 보정은 추가하지 않는다. cursor의 임의 압축 임계값도 CPython과 다른 결과를 내므로 완성된 호환 구현으로 취급하지 않는다. 두 문제 모두 기존 실패 fixture를 통과시키고 일반 경로 비용까지 측정한 대체 구현을 선별한다.
