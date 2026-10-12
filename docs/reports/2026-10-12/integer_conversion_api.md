# 108차: int의 bytes와 base API

후속 3번의 기존 integer_conversion_remaining.py 출력이 CPython 3.12.15와 일치한다.
bytes/bytearray의 ASCII 숫자, base의 __index__, keyword base와 검증 순서를 연결했다.
일반 int 호출, builtin 위임, 명시적 int.__new__가 공통 변환 함수를 사용한다.
int 타입 호출의 임시 builtin wrapper를 제거하고 int 하위 타입 생성의 중복
PyInt 생성을 제거했다. bytes 오류에는 Unicode 공백/숫자 정규화를 적용하지 않는다.

확대 회귀에서 bytes(existing_bytes)의 누락과 legacy PyBytesObject의 오류 repr도
발견했다. 전자는 원본 반환, bytearray는 복사, 후자의 int 오류는 bytes 표기로 수정했다.
오류 종류·문구, NUL/비ASCII 입력, base callback 순서까지 비교했다.

기본 197개 실행에서 새 회귀의 empty bytes 오류 표기 1개만 달랐고 수정 후
해당 회귀 재실행이 일치했다. 나머지 196개는 원래 실행에서 통과했다.
Godot 소스 빌드 및 기본 17개+관련 3개 통과.
원래 실패와 수정 후 결과는 round108-validation.json에 함께 보존했다.

기존 int 하위 타입 문자열 생성 workload(1만 회/10 samples/3쌍/CPU 4)의
중앙값은 58.16055→59.08715 ms이며 프로세스 범위가 겹친다. 속도 개선은
주장하지 않는다. 105차 기준 비교이므로 106~107차 변경도 포함한다.
나머지 정수 format, 컴파일 literal 제한은 이 API 수정과 별개로 미해결이다.
