# 128차: warnings 필터 검사와 오류 순서

후속 4번. 고정 CPython 3.12.15 Python/_warnings.c의 get_filter와 warn_explicit를
비교했다. 두 matcher와 category 검사를 수행한 다음, 일치 여부와 관계없이
실제 int 줄 번호를 ssize 범위로 검증한다. __index__만 있는 객체는 거절한다.
필터 소진 후 defaultaction 타입 검사, 잘못된 action의 오류 메시지와 registry
기록 순서도 수정했다. 반복 도중 전역 filters를 교체해도 기존 list의 추가 항목을
관찰하는 과정은 기존 구현이 이미 일치했고 회귀에 고정했다.

관련 3개 CPython 출력 일치, Godot 소스 환경에서 관련 2개와 기본 검증 통과.
Lib/warnings.py를 Godot 프로젝트에도 복사하여 검증했다. 원자료 round128-validation.json.

str subclass action 검증에서 기존 문자열 subtype이 native 문자열 payload를
갖지 못하는 별도 문제가 드러났다. constructor 인자를 값처럼 사용하는 우회 구현은
하지 않았다. 실패는 compatibility/warnings_action_subclass_remaining.py에 보존했다.
이는 미해결이며 이번 변경 전에도 실패한다. async generator hooks/자동 최종화와
전체 set API 재진입 감사도 완료로 분류하지 않는다.
