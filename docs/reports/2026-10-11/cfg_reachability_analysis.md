# 64차: CFG 도달 불가 명령 제거와 generator 분류 보존

63차를 `faa5daa` (`perf: avoid redundant constant return stack work`)로 커밋했다. 이번 변경은 검증 후 미커밋으로 남긴다.

## 변경과 근거

CFG가 모든 `Next` 연결을 실행 가능한 fallthrough로 계산하여 반환 뒤 코드와 도달 불가 cycle까지 유지했다. entry에서 실제 jump·fallthrough·exception edge를 따라 predecessor를 계산하도록 바꿨다. 배치 순서인 Next 자체는 유지한다. 이미 제거된 SETUP_* 대신 각 명령의 ExceptBlock을 따라가므로 reachable yield에서 throw/close가 들어갈 handler도 보존한다.

블록 경계 판정에 RETURN_CONST, RERAISE, JUMP, JUMP_NO_INTERRUPT를 추가하고 SEND의 fallthrough를 분리했다. successor는 pseudo 명령 제거 전 인덱스 사전에 변환 후 offset을 조회하던 방식 대신, 이미 해결된 TargetBlock을 사용한다. layout 연결 후 successor를 구성한다. RETURN_GENERATOR와 YIELD_VALUE는 재개 가능한 경로이므로 반환 terminator로 취급하지 않는다.

CPython 3.12.15 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Python/flowgraph.c:mark_reachable`과 `Python/compile.c`의 generator 플래그 결정을 참고했다. CPython은 pseudo block-push가 남아 있는 단계에서 traversal한다. SharpPy는 handler가 명령에 붙은 단계이므로 그 표현에 맞춰 구현했다. opcode 배열·예외 table의 완전 일치를 목표로 삼지 않는다.

도달 불가 yield를 제거하자 기존 IsGenerator의 명령열 검색이 false를 반환했다. CO_GENERATOR를 먼저 확인해 scope 분석 결과를 유지하도록 했다. **async/lambda의 기존 컴파일 경로에는 명령 검색 fallback이 남아 있다.** 플래그만 사용한 실험은 async 회귀를 깨뜨려 채택하지 않았다. 이번 단계에서 모든 함수의 검색을 제거했다고 주장하지 않는다.

## 검증

- .NET Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 120개: 출력 완전 일치 115개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 cfg_reachability_regression.py는 수정 전후 모두 실제 CPython 3.12.15와 동일 출력이다. 반환/raise/break/continue 뒤 코드, exception/finally, 도달 불가 yield가 있는 generator, yield from의 반환값, cleanup yield 이후 next/throw/close를 확인한다.
- C# probe는 네 terminator 뒤 도달 불가 cycle, 실제 InstructionSequence의 RETURN_CONST 경계, yield 재개와 exception handler edge 보존을 검사한다.
- Godot에 SharpPy 소스를 직접 포함해 빌드했고 기본 17개와 추가 32개, 합계 49개 fixture 및 native range helper를 통과했다. 외부 SharpPy DLL이나 신규 의존성을 추가하지 않았다. 모바일 실기 검사는 하지 않았다.
- git diff --check 통과.

최종 로그: `.artifacts/round64-full-final.json`, `round64-native.json`, `round64-godot-*.log`. 요약·측정 원본·발견한 기존 오류는 [round64-validation.json](round64-validation.json)에 보존했다. 앞선 실험 로그의 실패를 최종 통과 결과로 덮어 해석하지 않는다.

예외 테이블 구조 비교는 **FAIL**이다. 모든 code unit의 범위와 offset 왕복은 유효하지만 suspending의 protected yield는 CPython 2개, SharpPy 1개다. 직접 상수 반환 이전에는 실행할 명령이 없어 정상 finally 사본만 필요하며, 이번 변경으로 사용하지 않는 사본이 제거되었다. 실제 next/throw/close 의미는 회귀로 확인했으나 일반적인 모든 table 동등성을 입증한 것은 아니다.

## 크기와 성능

신규 fixture의 final instruction word 수:

| 함수 | 이전 | 이후 |
| --- | ---: | ---: |
| constant | 26 | 2 |
| variable | 15 | 3 |
| exceptional | 158 | 108 |
| loop | 104 | 62 |
| empty_generator | 11 | 6 |
| delegated | 106 | 74 |
| suspending | 94 | 31 |

상수·이름 pool까지 압축하지 않았으며 도달 불가 코드를 파싱하는 비용도 그대로다. 기존 bytecode 표본의 protected는 29→21 word로 감소했다. CPython의 22 word와 맞추기 위해 명령을 추가하지 않았다.

.NET 8.0.31, TieredCompilation=0, 1만 회 workload, 프로세스당 warmup 10회/측정 10회, 전후 교대 3쌍. 빌드·회귀가 끝난 뒤 측정했고 합계도 검사했다. 시간은 프로세스 중앙값들의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전/이후 할당 B |
| --- | ---: | ---: | ---: |
| generator_iteration | 63.023 | 60.185 | 6,962,568 / 6,242,424 |
| constant_cleanup | 18.805 | 18.944 | 9,601,336 / 9,601,336 |

generator 반복은 약 4.5% 짧아지고 720,144 B가 감소했다. 플래그가 있는 generator의 반복적인 LINQ 명령 검색을 건너뛴 효과가 포함된다. constant_cleanup 시간은 약 0.7% 증가했고 할당은 동일하다. 모든 실행이 빨라진다고 일반화하지 않는다. 순수 dead-code 제거만의 실행 시간 개선율로도 해석하지 않는다.

## 새로 확인한 기존 차이와 다음 순서

다음 두 최소 재현은 이번 변경 전 `faa5daa`와 변경 후 모두 실패한다. 기본 통과 목록에 숨겨 포함하지 않고 별도 compatibility fixture로 보존했다.

1. `tests/compatibility/continue_finally_stack.py`: for 안 continue/break와 finally 조합에서 SWAP 스택 부족. CPython은 합계 2와 정리 이벤트 [0, 1, 2, 3]을 반환한다. 다음 수정의 우선순위다.
2. `tests/compatibility/nested_finally_return_duplicate.py`: 안쪽 finally를 지난 뒤 바깥 except에서 return하면 안쪽 finally를 다시 실행한다. CPython 이벤트는 inner, live, outer이고 SharpPy는 inner가 중복된다. unwind frame 수명 관리를 함께 조사한다.

신규 회귀의 local_scope는 도달 불가 대입도 local scope 분석에 남는지를 NameError 계열로 검사한다. SharpPy가 UnboundLocalError 대신 NameError를 내는 기존 정확한 예외 타입 차이는 이번에 해결하지 않았다.

그 뒤 async/lambda/generator의 컴파일 플래그 결정을 통일하고 IsGenerator의 명령 검색 fallback 제거를 검토한다. CFGOptimizer 안의 기존 보수적인 중복 도달성 순회도 후속 조사 대상이다. 이번 단계에서는 추가로 광범위한 optimizer 재작성을 하지 않았다.
