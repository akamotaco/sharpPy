# 48차: dispatcher에 실행에 필요한 두 값만 전달

47차는 `cbc1b20`로 커밋했다. 이번에는 instruction 표현과 호출 비용을 조사하고, 전체 표현 변경보다 좁은 dispatcher 인수 변경을 적용했다.

## 분석과 변경

이 환경의 Unsafe.SizeOf<ByteCodeInstruction>()는 40B이고 참조 필드를 포함한다. opcode·argument 외에 소스 위치, 파일명, CFG target/exception block 참조가 있다. ExecuteFrame은 소스 위치를 사용하지만 ExecuteInstruction은 opcode와 argument만 읽는다. MAKE_FUNCTION의 분리된 helper는 argument만 사용한다.

읽기 전용 참조(in)로 전달하는 후보는 덧셈·호출·예외 workload에서 유의미한 개선이 없어 제외했다. 채택한 변경은 ExecuteInstruction에 ByteCodeOp와 int를, ExecuteMakeFunction에 int만 넘긴다. 각 opcode 처리 본문의 instruction.OpCode/Argument 참조를 대응하는 매개 변수로 바꿨다. 기준 파일에 이 기계적 치환만 적용한 결과가 변경 파일과 같음을 검사했다.

컴파일 IR, 공개 ByteCodeInstruction, 최종 instruction 배열, EXTENDED_ARG 결합, 소스 위치·예외 테이블은 유지한다. 기존 private 메서드 인수만 바꿨으며 별도 실행 배열이나 캐시, DLL 의존성을 추가하지 않았다. instruction 저장 크기는 여전히 40B다. 이 수정으로 IR/실행 표현 분리를 완료했다고 주장하지 않는다.

## 성능

.NET 8.0.31 Release, TieredCompilation=0, 기준 cbc1b20. workload마다 순차 전후 3쌍, 프로세스별 warmup 10회·측정 10회, 루프 10,000회. 표는 30개 측정 중앙값과 Q1–Q3(ms)다.

| workload | 전 중앙값 (Q1–Q3) | 후 중앙값 (Q1–Q3) | 시간 감소 |
|---|---:|---:|---:|
| addition | 5.18710 (5.0679–5.2886) | 4.69485 (4.5351–4.7782) | 9.5% |
| function_call | 13.66365 (13.6118–13.7164) | 12.37065 (12.2338–12.4104) | 9.5% |
| exception_handling | 97.03235 (96.2575–97.2423) | 95.42165 (95.1628–95.8965) | 1.7% |
| comprehension | 3.63655 (3.6162–3.8315) | 3.20545 (3.1211–3.3513) | 11.9% |

모든 조건에서 할당은 전후 동일하다. JIT이 생성한 호출 규약·레지스터·코드 배치의 영향을 포함하는 측정이며, 시간 차이 전체가 정확히 40B 복사만의 비용이라고 단정하지 않는다. 측정하지 않은 모든 프로그램이나 Godot에서 같은 개선율을 보장하지 않는다.

## 검증과 자료

일반 Release 및 Godot 소스 포함 빌드 통과(최종 빌드 경고/오류 0개). 실제 CPython 3.12.15와 전체 92개 평가에서 87개 완전 일치, 기존 변동값 제외 일치 3개, C# 전용 검사 2개 실패 보고 없음. Godot에서 기존 17개 fixture 및 C# helper, 추가 range slice·instruction offset·큰 generator fixture 3개를 실행해 통과했다. 전체 CPython 대조와 측정 원자료는 round48-validation.json 및 `.artifacts/round48-tests.json`에 기록했다.

정적 IL 도구에 instruction의 관리 메모리 크기와 참조 포함 여부를 추가했다. 이 도구는 개발용 harness이며 Godot 런타임 의존성을 추가하지 않는다. boxing/생성자 IL 자료는 런타임 전체의 할당 증명이 아니다.

다음은 최종 instruction 배열에서 실행 메타데이터와 CFG 참조가 실제로 유지되는 양, 프레임의 instruction fetch 비용을 조사하는 것이다. 이번 변경은 미커밋 상태다.
