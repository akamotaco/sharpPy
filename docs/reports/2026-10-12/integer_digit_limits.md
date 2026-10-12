# 102차: 정수 문자열 제한 제어

CPython v3.12.15의 `Python/sysmodule.c`의 get/set_int_max_str_digits,
`Objects/longobject.c`의 문자열 변환과 실제 3.12.15 실행을 대조했다.

VM에 기본 4300자리 설정을 두고 sys get/set API를 연결했다. 0은 제한 해제,
나머지는 640 이상이어야 한다. C int 범위, __index__, 인자·keyword 처리와
실패 시 이전 설정 보존을 비교했다. sys 모듈 객체 속성이 아니라 VM 상태를
참조한다. 기존 런타임이 singleton VM을 사용하는 구조는 바꾸지 않았다.

비 2의 거듭제곱 진법 입력과 decimal str/repr에 변경된 제한을 적용한다.
출력은 보수적인 bit 길이 검사로 명백히 초과한 큰 정수의 decimal 생성을
피하고, 경계에서는 실제 자리수를 센다. 기본/`d` format도 이 경로를 사용해
기존 Int64 축소와 CLR OverflowException을 없앴다.

검증: 기본 회귀 175개, Godot 소스 직접 빌드 및 기본 17개+관련 3개.
새 회귀는 639/640/641자리, 부호, 0 문자열, binary 예외, 제한 해제 후
5000자리, 설정 오류와 복구를 다룬다. raw 결과는 round102-validation.json.

성능: 기존 작은 문자열 int 하위 타입 생성 10000회, warmup 10,
표본 10개·교차 3쌍·CPU mask 0x4·tiering off.
프로세스 중앙값의 중앙값 59.1691→58.9806 ms, 범위 겹침으로 속도 개선을
주장하지 않는다. 할당은 양쪽 13,121,184 B로 동일하다.

남은 범위: bytes/bytearray int 입력, index 및 keyword base는
`tests/compatibility/integer_conversion_remaining.py`에서 불일치한다.
width/grouping 등 다른 숫자 format 경로, sys.int_info/시작 옵션 및
컴파일 단계 decimal literal 제한까지 모두 구현한 것은 아니다.
일치하는 기본/d format과 나머지 변환 API를 구분해서 추적한다.
