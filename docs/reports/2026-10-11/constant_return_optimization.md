# 63차: 상수 반환의 여분 스택 값과 generator 종료 명령 제거

62차를 `9520ba0` (`fix: resolve super frame cells and document bytecode differences`)로 커밋했다. 이번 변경은 미커밋으로 남긴다.

평가 기준은 CPython 3.12와 관찰 가능한 결과·예외·부작용을 유지하면서 실행 비용을 줄이는 것이다. bytecode 명령열·번호·예외 테이블의 완전 일치를 목표로 삼지 않는다.

## 수정

`CompileStatement(ReturnStatement)`는 상수를 CompileExpression으로 스택에 올린 뒤 RETURN_CONST로 다시 반환했다. 그 여분의 값 때문에 loop/with/finally 정리에서도 preserveTos를 켜고 보존 작업을 수행했다.

상수 및 None 반환은 값을 미리 적재하지 않고 cleanup 이후 RETURN_CONST가 직접 읽게 했다. 비상수 표현식은 기존처럼 먼저 평가하고 cleanup 동안 값을 보존한다. CPython 3.12.15 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Python/compile.c: compiler_return`에서 Constant_kind에 preserve_tos를 사용하지 않는 것을 확인했다. CPython의 디버깅용 NOP까지 복제하지는 않았다.

`WrapInStopIterationHandler`는 함수 컴파일이 이미 반환 명령을 붙인 뒤에도 LOAD_CONST None / RETURN_VALUE를 덧붙였다. 본문이 terminator로 끝나면 이 추가 반환을 생략하고, 끝나지 않은 경우에만 RETURN_CONST None을 남긴다. StopIteration 변환 handler 자체는 유지한다.

새 AST 순회, 런타임 분기, 캐시 또는 의존성은 추가하지 않았다. 광범위한 CFG dead-code 제거는 이번 범위에 넣지 않았다.

## 검증

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 119개: 완전 일치 114개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 `constant_return_cleanup_regression.py`는 수정 전후 모두 CPython 3.12.15와 출력 일치한다. 기능 오류 수정이 아닌 최적화의 의미 보존 검사다.
- for/with/try/finally의 상수 반환, finally의 반환 덮어쓰기, __exit__ 오류 identity, 비상수 평가 순서, cleanup에서 yield 후 원래 반환, yield from 반환값, PEP 479를 확인했다.
- 전체 검사 후 cleanup에 throw/close를 주는 assertion을 추가했다. 확장 fixture도 CPython 비교와 Godot 재실행에서 통과했다.
- Godot에 SharpPy 소스를 직접 포함한 빌드 및 47개 회귀, native range helper 통과. SharpPy 외부 DLL을 참조하지 않았으며 모바일 실기 검사는 하지 않았다.
- `git diff --check` 통과.

로그는 `.artifacts/round63-full.json`, `round63-before-targeted.json`, `round63-expanded.json`, `round63-godot-*.log`다. 요약과 측정 원본은 [round63-validation.json](round63-validation.json)에 기록했다.

## 예외 테이블 추가 검사 해석

`compare_exception_tables.py --check-yield-depth`를 신규 fixture에 적용한 **전체 구조 비교 결과는 FAIL**이다. 실행 회귀 통과와 혼동하지 않는다.

1. `overridden`은 `try: return 42` / `finally: return 9`다. CPython은 table을 유지하지만 수정 후 SharpPy에는 보호 구간이 없다. 스택 적재를 제거해 정리 이전에 남는 실행 명령이 없으며 실제 결과는 9다. 수정 전에는 불필요한 적재를 감싸는 table이 있었다.
2. `suspending`의 finally/yield 코드 배치는 CPython과 다르다. 보호 yield 목록은 SharpPy 수정 전후 동일하며, 이번 변경으로 생긴 차이가 아니다. 실제 next/throw/close 결과를 별도 검사했다. 복제 cleanup 코드 전체의 CFG 동등성까지 검증했다고 주장하지 않는다.

수정 전후 모든 code unit의 table 범위 및 offset 왕복은 유효했다. 검사 도구를 통과시키기 위해 불필요한 명령이나 보호 구간을 되살리지 않았다. raw 비교는 `.artifacts/round63-exception-tables.json`과 `round63-before-exception-tables.json`에 남겼다.

## 명령 수와 성능

기존 bytecode 표본의 select는 7→5 word, delegated는 29→27 word로 줄었다. select는 상수 인덱스를 정규화하면 CPython과 같아지고 delegated는 opcode·인자가 같아졌지만, 이는 최적화의 부수 결과다. [round63-bytecode.json](round63-bytecode.json)에 실제 명령열을 보존했다. `protected` 표본에는 여전히 다른 정리 코드와 상수 처리가 남는다.

.NET 8.0.31, TieredCompilation=0, workload 1만 회. 프로세스당 warmup 10회와 측정 10회, 전후 교대 3쌍. 빌드·회귀 완료 후 측정했고 합계 결과도 검증했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 이전/이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| 조건부 상수 반환 호출 | 14.678 | 14.103 | 약 -3.9% | 8,321,336 |
| for iterator 정리 후 상수 반환 호출 | 19.304 | 18.065 | 약 -6.4% | 9,601,336 |

두 workload의 전후 프로세스 중앙값 범위는 겹치지 않았다. 제한된 workload의 결과이므로 전체 프로그램의 개선율로 일반화하지 않는다. 실행 중 할당량은 동일하다. generator의 도달하지 않는 종료 명령 제거는 code 크기 감소이며, 이 변경만으로 generator 실행이 빨라졌다고 주장하지 않는다.

## 다음 순서

CFG의 terminator 판정과 도달 불가 명령 처리를 조사한다. `PyFlowGraph.IsUnconditionalJump`에는 RETURN_VALUE는 있지만 RETURN_CONST가 없어 block 경계 처리 차이가 있다. 이 경로의 실제 영향을 확인하고, jump·exception handler·generator 재개 지점을 보존할 수 있는 최적화만 적용한다. CPython과 다른 배열 구성이나 opcode 번호 자체를 수정 이유로 삼지 않는다.

기존 super 인수 형식, classcell 전달, generator throw/close 및 native iterator의 남은 차이도 유지한다.
