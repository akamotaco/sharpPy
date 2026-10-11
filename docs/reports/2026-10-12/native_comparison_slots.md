# Native 비교 슬롯 (94차, 작업 1)

문자열의 비지원 피연산자는 NotImplemented를 반환해 반대편 비교에 도달한다. tuple 하위 타입은 기존 비교 특수 메서드 조회를 사용하고, 기본 tuple 비교로 fallback한다. int의 여섯 명시적 비교 descriptor는 정수 payload만 비교하며 float에는 NotImplemented를 반환한다. 사용자 재정의와 일반 혼합 숫자 비교는 이 descriptor와 구분한다.

CPython 3.12.15의 long_richcompare와 실제 실행을 대조했다. 새로운 회귀는 여섯 연산, 반대편 tuple 하위 타입 우선권, 상속 fallback, 큰 정수, bool, 재정의 우회, 인자 오류, callback 예외를 포함한다. 기본 회귀 163개: 출력 일치 158, 기존 변동 필드 정규화 3, native 검사 2. Godot 소스 포함 빌드 오류/경고 0, 기본 17개와 관련 fixture 19개 통과. 시간 개선은 측정하거나 주장하지 않는다.

원시 검증 결과: `round94-validation.json`. 작업 2~6은 아직 미완료다.
