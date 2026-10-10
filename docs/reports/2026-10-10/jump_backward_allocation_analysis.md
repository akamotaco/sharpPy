# 47차: JUMP_BACKWARD의 Release 디버그 배열 할당 제거

45·46차 변경은 `826f64e`로 커밋했다. 일반 루프의 JUMP_BACKWARD 호출 경로를 조사하고 실제로 실행되는 불필요한 할당 하나를 제거했다.

## 호출 경로와 수정

일반 PyCodeObject의 JUMP_BACKWARD는 PyJumpBackwardUtil.CalculateJumpBackwardTarget(int, int, List)를 호출한다. 이 overload의 실제 계산은 `(currentInstrPos + 1) - opArg`로 이미 상수 시간이다. 주석의 “비최적화 모드에서 byte offset 변환” 설명은 실제 본문과 맞지 않는다. 별도의 offset 변환 overload나 Quickened 경로를 일반 루프의 병목으로 오인하지 않는다.

계산 후 PyVM은 점프 목적지가 RETURN/RAISE인지 디버그 경고를 출력하기 위해 매번 3개 enum의 배열을 만들고 Contains를 호출했다. 경고 출력만 DEBUG_LOG 안에 있고 배열 생성과 검사는 Release에도 남아 있었다.

전체 진단 블록을 DEBUG_LOG로 제한하고 배열 검사는 패턴 비교로 바꿨다. 유효 범위 검사와 실제 점프 계산, stack 및 generator 상태는 그대로다. Release에서 역방향 점프당 40B 배열 할당이 사라졌다. 새 런타임 의존성은 없다.

## 회귀

generator_wide_jump_regression.py는 300/14,000문장의 분기와 반복을 생성하고 양쪽 분기에서 next/send/throw/close, 예외 처리, finally 실행 결과를 확인한다. 실제 CPython 3.12.15에서 큰 함수는 연속 EXTENDED_ARG 2개를 사용하며 FOR_ITER 인수 70018, POP_JUMP_IF_FALSE 70000, JUMP_BACKWARD 70022/70064를 포함한다. 결과는 수정 전후 모두 일치했다. 점프 오류를 새로 수정한 것으로 보고하지 않는다.

- 전체 92개 평가: 87개 완전 일치, 기존 변동값 제외 일치 3개, C# 전용 검사 2개 실패 보고 없음.
- 일반 Release 빌드 통과(기존 tokenizer nullable 경고 1개, 오류 0개).
- Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드 경고/오류 0개. 기존 17개 fixture와 C# helper 검사, 신규 generator fixture 실행 통과.
- 신규 fixture는 기본 평가 목록에 추가했다. 큰 source는 테스트 실행 중 생성하므로 반복되는 14,000줄을 저장소에 복제하지 않는다.

## 측정

826f64e 기준과 변경본을 같은 standalone harness로 비교했다. .NET 8.0.31 Release, TieredCompilation=0, 순차 전후 3쌍, 프로세스당 warmup 10회와 측정 10회, 루프 10,000회. 아래는 30개 측정의 중앙값이다.

| workload | 전(ms) | 후(ms) | 전 할당(B) | 후 할당(B) |
|---|---:|---:|---:|---:|
| addition | 5.39835 | 5.24040 | 1,201,280 | 801,280 |
| function_call | 14.05160 | 13.75345 | 8,082,488 | 7,682,488 |

두 workload 모두 400,000B 감소했다. 시간은 각각 약 2.9%, 2.1% 단축됐지만 모든 Python 프로그램의 속도 향상률로 일반화하지 않는다. 원시 측정과 검증 요약은 round47-validation.json, 전체 출력은 `.artifacts/round47-tests.json`에 보존했다.

## 다음

현재 기본 경로의 큰 점프·generator 경계를 확인했다. 다음은 실행 instruction의 메타데이터 포함 크기와 실제 복사 비용을 측정하고, 컴파일 IR과 실행 표현 분리의 실익을 판단하는 것이다. Quickened 경로와 adaptive specialization은 검증·구현 완료로 취급하지 않는다. 이번 변경은 미커밋 상태다.
