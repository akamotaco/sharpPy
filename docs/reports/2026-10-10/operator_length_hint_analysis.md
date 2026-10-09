# 39차: _operator.length_hint 공통 경로 연결

38차는 `1cbc300`으로 커밋했다. `_operator.length_hint`의 모든 오류를 기본값으로 바꾸던 구현을 제거하고 38차의 내부 LengthHint를 사용하도록 바꿨다. 수정된 Python 라이브러리 파일은 교체하지 않았다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Modules/_operator.c, Modules/clinic/_operator.c.h와 Objects/abstract.c를 대조했다. 기본값은 대상 길이 조회 전에 __index__로 변환하고 native 인덱스 범위를 검사한다. 음수 기본값은 허용하며 실제 길이나 힌트가 없을 때 그대로 반환한다. bool 기본값도 반환 결과는 int다. 인자 개수 오류와 keyword 거부 메시지도 실제 CPython과 맞췄다.

## 검증

새 회귀는 기본값·len·hint 순서, 기본값 변환 실패 시 대상 callback 미실행, 성공한 len의 우선순위, NotImplemented 및 TypeError fallback, 다른 예외 전파, 음수·bool·초대형 기본값, positional 인자 수와 keyword를 검사한다. 기준 DLL은 새 회귀와 기존 최소 재현에 실패했고 최종 DLL은 실제 CPython과 일치한다. 두 회귀를 기본 평가에 포함했다.

Release 빌드 성공. 기본 83개는 raw 일치 78, 지정 변동 필드만 다른 3, native no_reported_failure 2다. Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드와 선별 회귀 12개 통과. 별도 SharpPy DLL 참조 없이 기존 GODOT 심볼 및 DEBUG 제한 제외 조건을 유지했다. [전후 실행·CPython 출처/hash·Godot 로그](round39-operator-length-hint.json)를 보존했다.

결과 정수는 기존 SmallIntCache를 사용한다. 별도 성능 측정은 하지 않았으므로 함수 전체의 무할당이나 속도 향상은 주장하지 않는다. 새 의존성 없이 공통 프로토콜 경로를 사용한다.

## 다음 범위

예외 args에서 시작한 길이 힌트 연결은 이번 선별 범위까지 완료했다. 다음에는 기존 미해결 range·memoryview의 큰 step 및 stride 표현을 다시 살핀다. 일반 iterator와 특수 메서드 조회의 다른 차이, 사용자 정의 예외 전체 동작은 아직 별도 범위다.
