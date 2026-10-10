# 53차: 처리 중인 예외 상태 복원

52차 변경을 `d38f35f`로 커밋했다. 이번 변경은 작업 트리에 남긴다.

## 수정

예외 테이블로 handler에 진입하기 전에 CurrentException을 새 예외로 덮어쓰고 있었다. 그 결과 PUSH_EXC_INFO가 저장하는 이전 예외가 실제로는 새 예외였고, POP_EXCEPT 뒤에도 처리 완료된 예외가 남았다. 이 조기 대입을 제거하고 PUSH_EXC_INFO가 이전 상태를 저장한 후 갱신하도록 했다. 검사 타입도 PyBaseException으로 맞춰 GeneratorExit 처리 상태를 보존한다.

인자 없는 raise는 RERAISE가 아니라 CPython과 같은 RAISE_VARARGS 0을 생성한다. RERAISE는 compiler cleanup에 필요한 스택 기반 명령이다. RAISE_VARARGS 0은 LastException 대신 현재 처리 중인 CurrentException을 사용하고 기존 traceback을 보존한다. 활성 예외가 없을 때 메시지도 실제 CPython 3.12의 `No active exception to reraise`와 일치시켰다.

원문 대조: 로컬 CPython v3.12.15의 Python/bytecodes.c PUSH_EXC_INFO·POP_EXCEPT·RAISE_VARARGS, Python/ceval.c do_raise. 소스 commit은 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`이다.

새 컬렉션이나 호출별 상태 객체는 추가하지 않았고 조기 상태 대입 하나를 제거했다. 이번에는 성능 벤치마크를 수행하지 않았으며 속도 개선 수치를 주장하지 않는다.

## 검증

- 기본 평가 98개: 출력 완전 일치 93개, 지정된 주소·시간 필드 제외 일치 3개, C# native 검사 2개.
- 기존 context 재현과 새 exception_state_regression.py를 기본 목록에 편입했다. 중첩 handler 이후 바깥 예외 재발생, 처리 종료 후 bare raise, 반복 처리 사이의 context, exceptional finally와 GeneratorExit 처리 중 context를 검사한다.
- 이전 커밋을 별도 archive에서 빌드해 두 회귀 fixture가 이전 버전에서 실패함을 확인했다.
- Godot 4.7.2에서 소스 포함 빌드 후 기존 17개 및 추가 9개 fixture와 native range helper가 통과했다. 모바일 실기 검증은 수행하지 않았다.

[검증 요약](round53-validation.json)에 결과와 fixture 해시를 보존한다. 전체 출력은 로컬 `.artifacts/round53-tests.json`, Godot 로그는 `.artifacts/round53-godot-*.log`에 있다.

## 다음 작업과 한계

exception_state_in_callee.py는 처리기 안에서 호출한 함수가 인자 없는 raise로 호출자의 예외를 다시 던지는지 검사한다. 실제 CPython은 통과하지만 이전 커밋과 현재 모두 실패한다. 이전 구현은 활성 예외 없는 RERAISE를 무시해 정상 반환했고, 현재는 RuntimeError를 발생시킨다. 이 fixture는 기본 통과 목록에 넣지 않았다. 다음 단계는 호출자와 피호출자, generator suspend/resume 사이의 handled exception 공유 규칙을 맞추는 것이다.

추가 관찰: GeneratorExit라는 builtin 이름이 노출되지 않는 별도 문제가 있다. 이번 generator 회귀는 BaseException으로 close의 주입 예외를 잡아 검사한다. generator.close가 내부 예외를 일괄 RuntimeError로 바꾸는 기존 처리도 별도 검토 대상이다. 이번 결과를 전체 예외 호환성 완료로 해석하지 않는다.
