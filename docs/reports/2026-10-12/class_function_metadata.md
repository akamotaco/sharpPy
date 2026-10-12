# 109차: class closure/qualname과 함수 키워드 기본값

후속 4번 기존 실패 3개를 해결했다. 클래스 본문에서 이름을 호출할 때도
일반 이름 로드와 같은 closure/namespace 판정을 사용한다. 클래스 qualname은
lexical symbol table에서 부모 클래스 및 함수의 <locals> 경로를 구성한다.

async 함수 정의가 일반 함수의 기본값 컴파일 경로를 사용하도록 중복 코드를
제거했다. __kwdefaults__를 노출하고 타입 검증, None/삭제, coroutine/generator
호출의 runtime 기본값을 연결했다. 삭제 뒤 compile-time 기본값으로 되돌아가는
fallback을 제거했다. metaclass 메서드 closure를 복사하는 기존 경로의
기본값 손실도 고쳤다. async generator가 정의된 전역 namespace도 보존한다.

확대 회귀에서 기본값 표현식 평가 시점, 수정/삭제 후 호출, nested qualname을
검증했다. 최종 전체 201개(196 출력 일치, 3 지정 변동 필드, 2 native CLI) 통과.
이후 keyword-only 인자가 없는 함수의 기본값 lookup 생략은 관련 3개를 다시 검증했다.
Godot 소스 빌드와 기본 17개+관련 4개 통과.

일반 함수 호출 1만 회/10 samples/3쌍 최종 측정은 105차 8.78935→현재 9.09085 ms.
약 3.4% 지연 관측으로 원인 분리가 남는다. 앞선 namespace 등 여러 변경이
포함된 비교이므로 이 변경 단독의 비용으로 단정하지 않으며 최적화 완료로
표시하지 않는다. callback/기본값 의미를 과거 구현으로 되돌리지는 않았다.
원본 실패·수정 후 결과·반복 측정은 round109-validation.json에 보존한다.
