# 113차: set 저장 해시, 재진입, 복사와 소비 순서

Objects/setobject.c의 set_lookkey/set_add_entry/set_merge 및 집합 연산을
로컬 고정 v3.12.15 원본과 대조했다. full-width 해시를 보존하고 충돌 후보만
동일성/동등성 검사한다. lookup은 비교 중 테이블 배치/현재 키가 바뀌면
재시작하고, add는 CPython처럼 참인 비교 결과를 먼저 받아들인다.
복사·pop·집합 간 연산은 저장 해시를 재사용한다. frozenset도 같은 저장소를
사용하며 기존 해시 캐시와 copy identity를 유지한다. 중복 저장소를 제거했다.

BUILD_SET은 원소 표현식 평가 뒤 원래 순서로 해시/삽입한다. 역순 Pop 삽입을
제거했고 임시 원소 배열 없이 스택 구간을 사용한다. generic intersection,
issubset/issuperset/isdisjoint는 CPython의 조기 종료를 따르며 union/update와
difference는 iterable을 순차 소비한다.

복사 비용은 canonical copy 배치를 캐시하고 복사본끼리 배열을 공유하여 줄인다.
키나 테이블을 변경하기 직전에만 분리한다. 단순 배열 분리는 재진입 검사에서
Python 테이블 배치 변경으로 취급하지 않아 불필요한 비교 callback을 반복하지
않는다. resize/clear/교집합 교체는 실제 배치 변경으로 추적한다.
복사본별 Count/fill/finger는 독립적이다.

## 검증과 성능

- 기존 set_hash_storage_remaining.py 출력 일치로 전환, 기본 회귀에 등록.
- 추가 회귀: 0/1/4/5/16/64/200개 집합 복사 후 독립 변경, 삭제/재복사,
  충돌 비교 중 변경, 200회 mutation, iterable 소비 이벤트.
- 전체 209개: 출력 일치 204, 변동 필드 3, native CLI 2.
- Godot 소스 빌드와 기본 17+관련 3개, 경로 5개 통과.

직전 ce3817b 바이너리와 1만 회·10 samples·교차 3쌍·CPU mask 0x4 비교:

| workload | 이전 ms | 이후 ms | 이전/이후 할당 B |
|---|---:|---:|---:|
| 생성/추가/검색/삭제 | 16.50575 | 16.0764 | 6,241,272 / 10,001,272 |
| 64원소 반복 복사 | 11.2365 | 9.29925 | 21,447,928 / 3,051,480 |
| membership | 5.60885 | 5.60505 | 803,384 / 803,368 |

복사 시간 약 17.2%, 할당 약 85.8% 감소. membership 범위는 겹친다.
**작은 집합 생성/변경은 1회당 376 B 증가**한다. CPython 탐색 배치를 유지하는
64-bit hash entry 테이블의 성장 비용이며 메모리까지 모든 경로가 개선됐다는
뜻은 아니다. 시간 회귀는 이번 측정에서 관측되지 않았다. 높은 생성 빈도에서의
할당 개선은 후속 과제로 남긴다. 전체 set API의 모든 overload/오류/하위 타입
동작까지 검증 완료한 것은 아니다.

초기 실패·수정 후 결과 및 원시 측정은 round113-validation.json에 보존한다.
