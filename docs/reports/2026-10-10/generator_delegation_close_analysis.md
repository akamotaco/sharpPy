# 56차: yield from의 위임 종료와 CLEANUP_THROW

55차를 `2960df4`로 커밋했다. 이번 변경은 작업 트리에 남긴다.

## 구현과 원문 대조

suspend된 generator의 다음 명령이 RESUME이고 인자가 2 이상인 경우, 스택 맨 위의 위임 iterator를 먼저 닫는다. PyGenerator는 Close를 직접 호출하고 다른 iterator는 close 속성을 조회해 호출한다. close 속성이 없는 경우는 건너뛰되 close 호출 자체의 AttributeError는 전파한다. 내부 close의 오류는 바깥 프레임에 주입해 바깥 handler/finally가 실행되게 한다. 위임 종료 중 부모의 실행 상태를 표시해 next 재진입을 막는다.

CLEANUP_THROW는 기존에 no-op이었다. 이제 StopIteration이면 마지막 전송값과 하위 iterator를 제거하고 None/value를 남겨 END_SEND로 넘긴다. 다른 예외는 그대로 다시 전파한다.

generator 재개 시 예외를 주입할 때도 None을 스택에 넣는다. CPython gen_send_ex2와 같은 규칙이다. 52차에서 정확해진 exception depth가 일반 yield의 불필요한 값을 제거하고, yield from cleanup에서는 last_sent_val을 보존한다. IP는 계속 suspend된 YIELD_VALUE에 유지한다. 일반 재개의 PendingException 검사를 한 번으로 합쳤다. 임시 컬렉션이나 새 iterator 래퍼는 추가하지 않았다.

CPython v3.12.15 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/genobject.c `_PyGen_yf`, `gen_close_iter`, `gen_close`, `gen_send_ex2` 및 Python/bytecodes.c `CLEANUP_THROW`를 로컬 원문과 대조했다.

## 검증

실제 CPython 3.12.15 비교 기본 104개: 완전 일치 99개, 지정한 주소·시간 필드 제외 일치 3개, C# native 검사 2개. 새 두 fixture는 이전 DLL에서 실패했다. 마지막 분기 통합 후에는 관련 회귀 6개와 Godot 관련 fixture 3개를 다시 검증했다.

회귀 범위는 다단계 generator 종료 순서, 내부 finally의 오류 identity 보존, 바깥 handler의 오류 처리, 내부 종료 거부 뒤 내부/외부의 재개 상태, close 없는 list iterator, 사용자 iterator의 close 반환값 무시·AttributeError 전파·StopIteration.value 처리, close callback 중 next 재진입 거절이다. 사용자 iterator 회귀에는 send 메서드를 가진 iterator를 사용한다. send 없는 iterator의 기존 문제는 다음 과제로 분리했다.

Godot 4.7.2 소스 포함 빌드 후 기존 17개 및 추가 15개 fixture와 native range helper가 통과했다. 모바일 실기 검증은 하지 않았다. 전체 로그는 `.artifacts/round56-tests.json`, `.artifacts/round56-godot-*.log`에 보존한다.

## 성능

.NET 8.0.31 Release, TieredCompilation=0, 각 workload 10,000회, warmup 10회, 측정 10회, 이전/이후 3쌍을 순차 실행했다. 시간은 프로세스별 중앙값 세 개의 중앙값이다.

| workload | 이전 | 이후 | 10,000회 할당(전후 동일) |
| --- | ---: | ---: | ---: |
| generator_iteration | 62.363 ms | 61.802 ms | 6,962,568 B |
| generator_close | 283.075 ms | 286.951 ms | 29,521,256 B |

일반 반복은 약 0.9% 감소, 직접 close는 약 1.4% 증가했다. 작은 시간 차이는 측정 변동과 새 위임 확인 비용을 분리하지 못하므로 속도 향상으로 주장하지 않는다. 할당량은 동일하다. 위임 종료는 이전에 필요한 작업을 생략했으므로 이전의 잘못된 결과와 처리 시간을 직접 비교하지 않는다. 원시 측정과 fixture 해시는 [round56-validation.json](round56-validation.json)에 보존한다.

## 한계와 다음 순서

먼저 send 없는 사용자 정의 iterator의 __next__ 경로를 수정한다. 현재 SEND가 PyIterator 파생 CLR 객체에 대해서만 next를 호출하고, 일반 Python iterator에는 send를 찾는다. 별도 generator_delegate_iterator.py에서 CPython 통과, 이전·현재 DLL 모두 TypeError 실패를 확인했다. 기본 통과 목록에는 포함하지 않았다.

추가로 close 속성 조회 자체가 AttributeError 이외의 예외를 던지는 경우 CPython은 unraisable 처리 후 종료를 계속하지만, 현재는 바깥 프레임으로 주입한다. sys.unraisablehook 경로와 함께 후속 검증·수정이 필요하다. 내부 close callback이 바깥 close를 다시 호출하는 특수 재진입은 실제 CPython 3.12에서 재귀 호출이 관찰됐으며, 현재 SharpPy의 즉시 거절과 다르다. 일반 next 재진입과 구분해야 한다. 이번 결과를 모든 iterator 종료의 호환성 완료로 해석하지 않는다.
