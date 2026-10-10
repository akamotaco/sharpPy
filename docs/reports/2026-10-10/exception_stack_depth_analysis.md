# 52차: 예외 스택 깊이와 unwind

이전 generator 재개 수정을 `1393896`으로 커밋했다. 이번 변경은 검토를 위해 작업 트리에 남긴다.

## 원인과 수정

CFG는 예외 처리기의 보존 깊이를 고정 0/1로 지정했고, VM은 최근 값 세 개를 임의로 복구했다. 루프 iterator와 미완성 표현식의 피연산자를 구분하지 못해 `100 + (yield i)`에서 throw 후 재개가 실패했다.

새 ExceptionStackDepth 분석기는 SETUP 의사 명령을 제거하기 전에 도달 가능한 경로의 값 스택 깊이를 계산한다. 합류 지점 불일치는 컴파일 오류로 드러낸다. SETUP_FINALLY/CLEANUP은 현재 깊이, SETUP_WITH는 현재 깊이-1을 보존하며 VM은 그 깊이까지 pop한다. 예외 처리기가 없는 코드는 분석 배열과 queue를 만들지 않는다. 컴파일 시 O(instructions + edges) 분석이며 실행 루프에 추가 계산을 넣지 않는다.

CPython v3.12.15(`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`)의 Python/flowgraph.c `_PyCfg_Stackdepth`, Python/compile.c의 스택 효과 및 try 컴파일, Python/ceval.c `exception_unwind`와 대조했다. 로컬 원문은 `.artifacts/cpython-locals/v3.12.15`에 있다. SharpPy의 FOR_ITER는 소진 iterator를 직접 pop하고 END_FOR는 marker이므로 이 두 명령의 내부 효과는 CPython과 다르게 계산한다.

깊이 검증으로 드러난 기존 컴파일 오류도 수정했다.

- 조건부 return을 try 전체 종료로 오인하던 코드: 정상 종료 jump를 항상 생성하고 실제 도달 불가 코드는 CFG에서 제거한다.
- except 내부 예외를 finally에 곧바로 보내던 코드: cleanup 후 바깥 finally로 전파한다.
- exceptional finally label을 SETUP_CLEANUP 앞으로 옮기고 정상 finally 전에 남은 보호 범위를 종료해 중복 실행을 방지한다.
- async for/with에서 CompileYieldFrom이 이미 넣는 None을 중복 생성하던 코드를 제거한다. async 검증은 기존 fixture의 컴파일 범위이며 전체 async 실행 지원을 의미하지 않는다.
- WITH_EXCEPT_START의 Exception 검사로 누락되던 GeneratorExit를 BaseException 검사로 처리한다. __exit__ 호출 전에 스택을 복구해 호출 실패 시에도 cleanup에 필요한 값을 보존한다.

## 검증

실제 CPython 3.12.15와 기본 96개 평가: 출력 완전 일치 91개, 사전에 지정한 주소·시간 필드 제외 일치 3개, C# native 검사 2개. 신규 두 fixture는 이전 커밋 빌드에서 실패하고 수정 후 통과한다.

exception_stack_depth_regression.py는 조건부 return, 중첩 루프의 표현식 yield와 throw/send/close, except/else 예외의 finally 실행, finally의 단일 실행, with의 예외 억제·GeneratorExit·__exit__ 실패를 검사한다. 기존 expression 재현도 기본 평가에 편입했다.

compare_exception_tables.py에 `--check-yield-depth`를 추가했다. 보호된 YIELD_VALUE의 depth/lasti는 중첩 루프에서 `(2, false)`, with에서 `(2, true)`로 CPython과 일치한다. 코드별 table 존재·범위·보호 opcode 검사도 통과했다. 바이트코드 전체 배치나 모든 명령의 table 동등성을 주장하지 않는다.

Godot 4.7.2에서 SharpPy 소스를 직접 포함해 빌드하고 기존 17개 및 추가 7개 fixture와 native range helper를 실행했다. 모두 통과했다. 모바일 실기 검증은 하지 않았다.

## 성능

이전 커밋을 별도 archive에서 Release 빌드했다. .NET 8.0.31, TieredCompilation=0, workload당 10,000회, warmup 10회, 측정 10회, 이전/이후 3쌍을 순차 실행했다. 아래 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 | 이후 | 10,000회 전체 할당(이전 → 이후) |
| --- | ---: | ---: | ---: |
| addition | 4.307 ms | 4.208 ms | 801,280 → 801,280 B |
| exception_handling | 100.745 ms | 99.372 ms | 9,841,280 → 8,641,280 B |

예외 workload 시간 약 1.4% 감소는 작으므로 큰 속도 향상을 주장하지 않는다. 임시 Stack/List 제거로 예외당 120 B, 10,000회에서 1,200,000 B 감소한 점은 일정하다. addition 시간 변화는 환경 변동을 포함한다. 새 분석기의 컴파일 비용은 별도 격리 측정하지 않았다. 원시 측정 및 depth 자료는 [round52-validation.json](round52-validation.json)에 보존했다.

## 다음 우선 과제

`tests/compatibility/exception_context_after_handler.py`에서 처리 완료된 ValueError가 뒤의 TypeError.__context__에 남는 기존 문제를 재현했다. CPython은 None이며 이전 커밋·현재 모두 실패한다. 이 재현은 기본 통과 목록에 넣지 않았다. 다음에는 PUSH_EXC_INFO/POP_EXCEPT와 CurrentException 갱신 순서를 대조해 예외 상태 복원을 수정한다. 구형 스택 효과 표의 정리와 컴파일 비용 측정도 후속 항목이다.
