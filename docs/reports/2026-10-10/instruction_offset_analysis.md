# 46차: 예외 핸들러 위치 변환의 선형 순회 제거

VM 구조 검증의 첫 단계로 CACHE·EXTENDED_ARG를 포함한 큰 함수의 점프와 예외 처리를 확인했다. 이번 입력에서 동작 차이는 재현되지 않았지만, 예외 핸들러 위치 변환의 불필요한 O(n) 순회를 확인해 O(1) 계산으로 바꿨다.

## 원인과 수정

PyFrame의 예외 핸들러 검색은 Code.ByteOffsetToInstructionIndex(entry.HandlerOffset)를 호출한다. 이 함수는 최종 Instructions 배열의 처음부터 2바이트씩 누적했다. 최종 배열은 CACHE와 EXTENDED_ARG도 개별 word로 저장하므로 byteOffset / 2로 직접 변환할 수 있다. 앞서 수정한 역방향 InstructionIndexToByteOffset과 같은 표현 규칙이다.

음수·코드 끝 이상 위치·홀수 위치의 거부와 C# 예외 타입/메시지는 유지했다. 잘못된 홀수 위치에 대해 Release에서도 출력하던 내부 디버그 Console 로그는 제거했다. 정상 Python 출력에는 영향이 없다. CFG 단계의 명령어 크기 계산이나 VM 점프 계산식은 변경하지 않았다.

참고한 로컬 CPython v3.12.15 소스는 Python/ceval.c의 예외 핸들러 JUMPTO(handler), Python/bytecodes.c의 EXTENDED_ARG 처리다. SharpPy 내부 구조를 C 표현으로 바꾸지 않고 최종 word 배열의 규칙을 활용했다.

## 검증

- instruction_offsets_regression.py는 300개의 서로 다른 상수를 더하는 분기를 생성하여 for의 전후 점프, if 양쪽 분기, raise/except/finally를 실행한다. 실제 CPython의 해당 함수에는 CACHE 331개, EXTENDED_ARG 52개가 들어 있다.
- 수정 전후 모두 실제 CPython 3.12.15와 출력 일치. 이번 성과는 동작 오류 수정이 아니라 비용 제거다.
- compare_exception_tables.py의 code unit별 table 유무·바이트 범위·보호 BINARY_OP/RAISE_VARARGS 존재·위치 왕복 검사가 통과했다. 전체 바이트코드나 예외 테이블의 byte-for-byte 일치 판정은 아니다.
- 네이티브 probe에서 16/1,024/16,384칸의 CACHE·EXTENDED_ARG·NOP 배열 모든 위치 왕복과 잘못된 위치 거부를 검사했다.
- 기본 91개 평가: 86개 완전 일치, 기존 변동값 제외 일치 3개, C# 검사 2개 실패 보고 없음.
- 일반 Release 및 Godot 소스 포함 빌드는 경고/오류 0개. Godot에서 신규 큰 함수 회귀 실행 통과. 기존 Godot fixture 전체를 이번 차수에 다시 실행한 것은 아니다.

## 측정

.NET 8.0.31 Release, TieredCompilation=0. 코드 끝 위치 변환 10,000회, warmup 5회, 측정 10회의 중앙값(ms). 각 크기에서 전후 모두 할당 0B다.

| 배열 word 수 | 변경 전 | 변경 후 |
|---|---:|---:|
| 16 | 0.0541 | 0.0140 |
| 1,024 | 2.4364 | 0.0141 |
| 16,384 | 36.1939 | 0.0140 |

변환 비용이 코드 길이에 비례하던 현상을 제거했다. 예외 생성·핸들러 탐색·VM 실행을 포함한 전체 성능 개선율은 측정하지 않았다. 원자료는 round46-validation.json, 상세 전체 회귀는 `.artifacts/round46-tests.json`에 보존했다.

## 다음 순서

1. PyJumpBackwardUtil의 별도 위치 변환과 실제 호출 여부를 확인한다. 비활성 Quickened 경로를 활성 경로로 오인하지 않는다.
2. 여러 EXTENDED_ARG가 필요한 점프 및 generator 재개와 예외 경계를 검증한다. 이번 입력이 모든 점프 경우를 검증하지는 않는다.
3. 위치 의미를 확인한 뒤 실행 instruction에서 CFG/소스 메타데이터를 분리할 실익을 측정한다. adaptive specialization은 여전히 미구현이며 켜지 않는다.

45차 검사·문서 변경과 이번 변경은 아직 미커밋 상태다.
