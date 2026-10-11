# Generator의 재귀 throw 위임 — 73차

72차를 `689e698` (`fix: delegate coroutine throw and normalize suspension exceptions`)로 커밋했다. 이번 변경은 다음 검토를 위해 작업 트리에 남긴다.

## 문제와 수정

coroutine의 throw가 __await__ generator에는 전달되지만, 그 generator가 yield from에서 중단되어 있으면 내부 iterator까지 전달되지 않았다. 기존 generator.Throw는 예외를 즉시 현재 frame에 주입했다. CPython에서는 내부 iterator가 먼저 예외를 처리하고 다시 yield하거나 반환할 수 있다.

중단된 frame의 다음 RESUME 인자를 사용해 기존 yield-from receiver를 찾는다. native generator에는 원래 예외 인자를 그대로 재귀 전달한다. 사용자 iterator는 throw 속성을 조회하고 원래 인자 개수로 호출한다. native 경로에서는 별도 인자 배열이나 bound method를 생성하지 않는다.

조회 중 AttributeError가 발생하면 바깥 frame에 예외를 주입한다. 다른 조회 오류는 해당 generator를 재개하지 않고 전달한다. 위임 메서드가 반환한 값은 yield 결과로 반환하며, 메서드 호출 중 발생한 예외는 원래 yield 위치에서 처리한다. StopIteration의 값 회수는 기존 CLEANUP_THROW를 사용한다. 내부 native generator에서 조회 오류가 발생하면 외부 generator에는 위임 호출 오류이므로 외부 frame의 예외 처리와 finally가 실행된다. 이 차이도 회귀 테스트로 확인했다.

GeneratorExit는 receiver.close를 먼저 실행한다. close와 throw가 receiver 조회·종료 처리를 공유하도록 분리했고, 위임 호출 중 frame 상태를 Executing으로 설정한 뒤 이전 상태를 복원한다. 일반 Next/Send의 실행 루프와 bytecode 배치는 변경하지 않았다.

참조는 CPython 3.12.15, pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/genobject.c:_gen_throw 및 실제 실행 결과다. SharpPy의 bytecode를 CPython과 동일하게 만드는 작업은 아니다.

## 검증

- .NET Release 빌드 성공.
- 기본 139개: 완전 출력 일치 134개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- generator_throw_delegation_regression.py: 다단계 위임, 예외 클래스·인스턴스, 다시 yield한 뒤 send/return, 사용자 iterator의 throw 부재·조회 실패·호출 실패·StopIteration, 원래 인자 전달, GeneratorExit와 close 실패, 재진입 상태 복원을 비교했다.
- 기존 coroutine_throw_yield_from.py를 기본 통과 목록으로 승격했다. 두 fixture는 이전 DLL에서 모두 출력 차이가 발생하는 것도 확인했다.
- Godot에 저장소 소스를 직접 포함해 빌드하고 기본 17개 및 native helper, 추가 51개 fixture를 실행했다. 합계 68개 fixture 통과. 별도 SharpPy DLL 의존성을 추가하지 않았다.
- 추가 Godot 51개는 CPython 출력과 비교했다. 기존 traceback fixture 1개만 컴파일 입력 파일명을 절대 경로에서 res://로 바꾸어 비교했으며 소스와 줄 번호는 그대로 비교했다.
- CFG reachability probe 및 git diff --check 통과. 모바일 실기 검증은 실행하지 않았다.

원본 요약은 [round73-validation.json](round73-validation.json), 상세 로그는 `.artifacts/round73-full.json`, `round73-before.json`, `round73-godot-comparison.json`에 보존했다.

## 성능

이전의 위임 경로는 결과가 다르므로 이번 구현과 처리량을 비교하지 않는다. 기존 정상 generator_iteration과 generator_close를 대조군으로 측정했다. .NET 8.0.31, TieredCompilation=0, 반복 1만 회, warmup 10회·측정 10회, 전후 교대 3쌍이다. 빌드·기능 검사와 겹치지 않게 실행했다. workload 결과 합계 검증을 통과했다.

| workload | before ms | after ms | before allocated B | after allocated B |
| --- | ---: | ---: | ---: | ---: |
| generator_close | 283.305 | 275.134 | 27,361,184 | 27,361,184 |
| generator_iteration | 55.553 | 56.855 | 6,242,352 | 6,242,352 |


시간은 프로세스별 중앙값 3개의 중앙값이다. close는 약 2.88% 감소했고 일반 반복은 약 2.34% 증가했다. 프로세스 중앙값 범위는 close 이전 278.021–283.816 ms/이후 273.101–275.275 ms, 반복 이전 55.089–57.025 ms/이후 55.420–62.990 ms다. 반복 범위는 겹치며 반복 루프 자체는 바꾸지 않았다. 두 workload의 할당량은 동일하다. 이번 수정의 일반적인 속도 개선을 주장하지 않으며, 시간 차이는 후속 관측 대상으로 남긴다.
## 다음 순서와 한계

1. 72차에서 재현한 coroutine_throw_empty_args.py의 차이: 실제 예외 생성자가 인자 없는 ValueError의 args를 () 대신 ('',)로 만든다. 다음은 throw에만 보정 코드를 넣지 않고 공통 예외 생성자를 분석하는 작업이다.
2. 구식 throw(type, value, traceback)의 DeprecationWarning 미구현은 coroutine_throw_legacy_warning.py로 남아 있다. 이번 단일 인자 회귀 결과를 전체 throw API 호환성으로 확대하지 않는다.
3. 기존 문서의 generator close 재진입 순서, 예외 traceback 세부 형식, awaitable 검증, coroutine kwargs, async generator 및 module delattr 후속 항목을 유지한다.
