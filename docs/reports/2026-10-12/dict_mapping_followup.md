# 107차: dict cursor/view와 module mapping

후속 2번의 기존 실패 재현 5개를 해결했다. dict iterator는 별도 키 목록의
cursor 대신 실제 저장소의 entry 위치를 읽는다. resize/압축 후 CPython과
같은 항목을 읽고 values/items 순회에서 저장 키의 재해시를 없앴다.
keys/items view의 비교와 집합 연산을 추가했다. fromkeys의 일반 iterable
누락도 확대 회귀에서 발견하여 기존 iterator 프로토콜로 연결했다.

module namespace의 공유 view를 ConditionalWeakTable로 유지하여 함수 안의
전역 대입/삭제와 globals()의 변경이 같은 저장소에 반영되도록 했다.
copy의 삽입 순서, popitem의 실제 binding 삭제, 보존된 view의 갱신을 검증했다.
일반 함수 지역 변수에는 이 전역 namespace 조회를 추가하지 않았다.
외부 C# 코드가 공개 Dictionary를 직접 변경하는 경로까지 모두 통제한 것은 아니다.

기본 195개 실행에서 새 확대 회귀 1개만 fromkeys(range) 누락으로 실패했다.
이를 수정한 재실행은 일치했다. 나머지 194개는 원래 실행에서 통과했다.
최종 Godot 소스 빌드와 기본 17개+관련 6개도 통과했다.
원래 실패 기록과 수정 후 결과를 round107-validation.json에 함께 보존했다.

기존 dict workload 1만 회/10 sample/3쌍 CPU 4 비교:
생성 7.7853→7.71365 ms, 순회 7.7244→7.7162 ms.
프로세스 중앙값 범위가 겹치므로 속도 향상은 주장하지 않는다.
이 비교 기준은 105차이며 106차 변경도 포함한다.
