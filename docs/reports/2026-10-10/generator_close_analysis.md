# 55차: generator.close의 예외와 상태 전이

54차를 `05557b7`로 커밋했다. 이번 변경은 작업 트리에 남긴다.

## 구현

close()가 정리 코드에서 발생한 모든 오류를 RuntimeError로 바꾸던 catch를 제거했다. CPython 3.12처럼 GeneratorExit와 정상 완료의 StopIteration만 성공으로 처리하며 다른 오류는 원래 객체 그대로 전파한다. close() 중 다시 yield하면 `generator ignored GeneratorExit`를 발생시키되 generator를 suspended 상태로 남겨 다음 resume/close를 허용한다.

시작 전 generator는 본문을 실행하지 않고 종료한다. 기존 PyFrame.State를 Next 진입과 finally에서 갱신해 실행 중 재진입을 거절한다. 새 필드나 상태 객체는 추가하지 않는다. throw의 인자 검증은 재진입 검사 전에 유지하며, 재진입 실패 전에 주입 예외를 저장하지 않는다.

close()는 public throw의 인자 변환 경로를 거치지 않고 인자 없는 GeneratorExit를 주입한다. PyGeneratorExit의 무인자 생성자를 빈 args로 맞추고 누락된 builtin 이름을 등록했다. 빈 args의 예외를 문자열로 변환할 때 기존 불변 PyString.Empty를 재사용해 임시 문자열 객체를 제거했다.

CPython v3.12.15 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`, Objects/genobject.c의 gen_close 및 gen_send_ex2를 로컬 원문과 대조했다. 현재 변경의 검증 범위는 직접 실행하는 generator의 종료다. yield from 위임 종료는 아래의 별도 미해결 항목이다.

## 검증

실제 CPython 3.12.15 기준 기본 102개: 출력 완전 일치 97개, 지정된 주소·시간 필드 제외 일치 3개, C# native 검사 2개. 새 두 fixture는 변경 전 DLL에서 실패했다.

회귀 범위는 시작 전 close, 반복 close, cleanup 예외의 identity와 GeneratorExit context/빈 args, 종료 요청을 무시한 yield 이후 재개, 처리기 return, 실행 중 close 재진입 및 잘못된 throw 인자의 우선 검증이다.

Godot 4.7.2에서 소스를 직접 포함해 빌드하고 기존 17개와 추가 13개 fixture, native range helper가 통과했다. 최종 실행 host는 .NET 8.0.31이다. 모바일 실기 검증은 수행하지 않았다. 원시 출력은 `.artifacts/round55-tests.json`, `.artifacts/round55-godot-*.log`에 보존한다.

## 성능

일반 generator 반복과 생성·시작·close를 분리한 generator_workloads.py를 추가했다. harness의 기존 기본 workload 목록은 유지하며 새 workload는 이름을 지정해 실행한다. 각 결과의 합계를 확인하므로 오류를 삼킨 실행을 성능 결과로 인정하지 않는다.

.NET 8.0.31 Release, TieredCompilation=0, 10,000회, warmup 10회, 측정 10회, 이전/이후 3쌍을 순차 실행했다. 동일 harness에 변경 전·후 DLL을 사용했다. 아래 시간은 각 프로세스의 중앙값 세 개의 중앙값이다.

| workload | 이전 | 이후 | 10,000회 전체 할당(이전 → 이후) |
| --- | ---: | ---: | ---: |
| generator_iteration | 60.425 ms | 60.833 ms | 6,962,632 → 6,962,568 B |
| generator_close | 316.716 ms | 278.634 ms | 30,161,288 → 29,521,256 B |

close는 약 12.0% 단축됐고 할당은 640,032 B 줄었다. 일반 generator 반복은 약 0.7% 증가했다. 상태 검사를 추가한 비용과 측정 변동을 분리하지 않았으므로 일반 반복 속도 개선은 주장하지 않는다. 예외 처리 중 yield 방식 자체의 비용은 여전히 별도 최적화 대상이다. 원시 측정과 fixture 해시는 [round55-validation.json](round55-validation.json)에 보존한다.

## 다음 단계

`tests/compatibility/generator_close_delegation.py`는 yield from의 내부 generator를 바깥 generator보다 먼저 닫아야 함을 검사한다. 실제 CPython은 통과하고 이전·현재 DLL은 모두 실패한다. 이 fixture는 기본 통과 목록에서 제외했다. 다음에는 gen_close의 하위 iterator close 호출, 내부 close 실패와 바깥 finally의 순서, 종료 거부를 검증한다. public throw의 예외 인자 정규화와 내장 연산 오류의 context 연결도 후속 항목이다.
