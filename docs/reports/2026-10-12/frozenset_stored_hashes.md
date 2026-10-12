# 106차: frozenset 저장 해시와 생성자 소비 순서

후속 1번의 부분 해결이다. mutable set 재진입 오류를 해결 완료로 표시하지 않는다.

CPython v3.12.15 Objects/setobject.c의 frozenset_hash를 대조했다.
원소의 전체 폭 Python 해시를 삽입 시 보존하고 frozenset 해시를 캐시한다.
기존 정렬 배열 생성 및 원소 __hash__ 재호출을 제거했다. copy와 동일 타입
생성자는 원래 객체를 반환한다. set/frozenset 일반 iterable 생성은 항목을
모두 수집한 뒤 해시하던 순서를 소비/해시가 교차하는 순서로 수정했다.

전체 mutable 저장소 교체 후보는 재진입/전체 해시/대수 연산 재현 출력을
맞췄지만, 반복 복사가 초기 약 31%, 조정 후에도 약 6% 느려졌고 큰 테이블
할당이 증가하여 제외했다. 따라서 기존 set_hash_storage_remaining.py는
여전히 미해결이며 CLR 재진입 및 집합 대수의 해시 재호출이 남는다.

채택한 변경의 기존 mutable set 측정(1만 회, 프로세스 3쌍, CPU mask 4):
생성/변경 중앙값 16.13795→16.50585 ms, 복사 11.0779→11.16715 ms.
일반 set 속도 향상은 주장하지 않으며 생성/변경의 작은 지연 관측은 남긴다.
frozenset의 반복 해시는 캐시 조회이고 새 회귀에서 원소 callback 0회를 확인했다.

기본 189개: 출력 일치 184, 지정 변동 필드 3, native CLI 2.
Godot 소스 빌드와 기본 17개+관련 2개를 통과했다.
실행 결과는 round106-validation.json에 보존했다.
