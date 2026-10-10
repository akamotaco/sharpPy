# 50차: CACHE word의 불필요한 실행 처리 생략

49차 CFG 참조 정리는 `a4da1e9`로 커밋했다. 이번 변경은 최종 배열의 CACHE word를 유지하면서 실행 루프의 불필요한 처리를 줄인다.

## 구현

기존 VM은 CACHE를 가져온 뒤 소스 위치 table 조회와 frame 위치 갱신, 비활성 specializer 호출, 예외 처리 진입, dispatcher의 CACHE no-op 분기를 거쳤다. CPython v3.12.15 Python/bytecodes.c의 CACHE 실행 본문은 unreachable로 표시돼 있으며, 캐시 데이터는 독립 Python 명령이 아니다.

SharpPy에서는 EXTENDED_ARG 판별 뒤 CACHE를 조기에 건너뛴다. 배열을 압축하거나 jump/예외 offset을 바꾸지 않고 InstructionPointer를 1만 증가시킨다. 누적 EXTENDED_ARG가 있거나 PendingException이 있으면 기존 처리 경로를 유지한다. 특히 generator.throw()의 예외 전달을 건너뛰지 않는다. 디버그 instruction 카운터/기록은 기존 위치에 남는다. 새로운 캐시 자료구조·외부 의존성은 없다.

## 성능

.NET 8.0.31 Release, TieredCompilation=0, 기준 a4da1e9와 순차 전후 3쌍. 각 프로세스 warmup 10회·측정 10회, 루프 10,000회. 표는 30개 측정 중앙값과 Q1–Q3(ms)다.

| workload | 전 | 후 | 중앙값 감소 |
|---|---:|---:|---:|
| addition | 4.75435 (4.6161–5.2611) | 3.94040 (3.8773–4.5871) | 17.1% |
| function_call | 12.67375 (12.4197–13.1701) | 10.77140 (10.5168–10.9896) | 15.0% |
| attributes | 11.52925 (11.4021–11.7088) | 6.20855 (6.0505–6.4935) | 46.1% |

addition 할당은 801,280B, function_call은 7,682,488B로 전후 동일하다. attributes는 전후 모두 1,854,096–1,855,984B 범위였다. 이 측정 범위를 모든 프로그램이나 Godot의 개선율로 일반화하지 않는다.

## 회귀와 발견한 기존 오류

cache_execution_regression.py는 CACHE를 갖는 호출 뒤의 subscript 오류 traceback 행, 속성 연산과 반복 중 generator send, 마지막 반복에서 throw 처리, close/finally를 검사한다. 실제 CPython 3.12.15와 기준/변경본 모두 일치했다. Godot 소스 포함 빌드와 기존 17개 및 추가 4개 fixture 실행도 통과했다.

검사를 넓히다 **루프 중간에서 throw()를 처리한 generator가 다음 yield 대신 조기 종료하는 기존 오류**를 발견했다. 최소 재현 `tests/compatibility/generator_throw_loop_resume.py`는 실제 CPython에서 통과하고 기준/변경본 모두 실패한다. 정상 재개가 필요한 이 검사를 통과한 것으로 보고하지 않는다. 기본 통과 목록에는 넣지 않고 별도 실패 결과와 함께 보존한다. 다음 차수에서 우선 수정한다.

첫 기준 실행은 복사한 DLL 옆 runtimeconfig 누락으로 시작하지 못했다. 설정 파일을 복사한 뒤 재실행한 결과로 위 판단을 내렸다. 이 호스트 오류와 실제 generator 실패를 구분했다.

기본 평가 93개는 88개 완전 일치·기존 변동값 제외 일치 3개·C# 검사 2개 실패 보고 없음이다. 위 별도 generator 실패는 이 93개에 포함되지 않는다. 원시 전후 성능, 기본 평가 요약 및 별도 실패 재현 결과는 round50-validation.json, 전체 출력은 `.artifacts/round50-tests.json`에 보존한다. 이번 변경은 미커밋 상태다.
