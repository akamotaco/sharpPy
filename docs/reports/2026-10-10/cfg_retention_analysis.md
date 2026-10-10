# 49차: 최종 instruction의 컴파일 CFG 참조 해제

48차 dispatcher 개선은 `ae7c2c8`로 커밋했다. 최종 instruction 배열의 메모리 유지 경로를 조사해 실행에 불필요한 CFG 참조를 제거했다.

## 원인과 수정

assembler는 CFG instruction을 최종 배열에 복사하면서 TargetBlock/ExceptBlock 참조도 보존했다. 이 참조로 BasicBlock의 Next, Successors, ExceptionHandler 및 block별 instruction 배열이 코드 객체와 함께 유지됐다. 최종 예외 테이블 생성까지는 필요한 정보지만 이후 실행에는 필요하지 않았다. 저장소의 소비자를 확인했으며 VM은 EXTENDED_ARG 결합 시 참조를 복사만 하고 사용하지 않는다.

BuildExceptionTableFromInstructions의 기존 순회에서 각 instruction의 예외 정보를 읽은 뒤 최종 배열 복사본의 두 참조를 null로 만든다. CFG 원본은 struct 복사본과 별개이므로 수정하지 않는다. CACHE는 emitter가 처음부터 두 참조를 null로 생성한다. 추가 배열·추가 전체 순회·외부 의존성을 만들지 않는다.

instruction 크기는 여전히 40B다. 컴파일 중 할당 자체를 줄인 것이 아니라 완성된 코드 객체가 컴파일 자료를 불필요하게 유지하는 경로를 끊은 것이다. 외부 C# 소비자가 최종 instruction의 공개 TargetBlock/ExceptBlock을 들여다봤다면 이제 null을 관찰한다. 이 필드를 런타임 메타데이터로 사용하는 저장소 내 소비자는 없었다. 실행 opcode/argument와 소스 위치, 예외 테이블은 유지한다.

## 검사 결과

개발용 `--cfg-retention-inspect <source> --require-detached` 검사를 추가했다. 코드 객체와 중첩 코드 객체의 최종 instruction에서 CFG 연결을 따라가 도달 가능한 블록과 instruction 배열 capacity를 센다. 실행에 사용되는 각 word의 opcode/argument/행/열/파일명 및 예외 테이블 전체를 직렬화한 SHA-256도 비교한다.

| 검사 소스 | 코드 객체 수 | 전 CFG instruction capacity(B) | 후 |
|---|---:|---:|---:|
| 300문장 분기·예외 함수 | 2 | 86,880 | 0 |
| 공통 성능 workload | 21 | 17,120 | 0 |
| class 예외 테이블 회귀 | 8 | 16,160 | 0 |
| 14,000문장 generator | 2 | 2,624,640 | 0 |

총 33개 코드 객체의 실행 메타데이터 해시가 전후 동일하며 변경 후 도달 가능한 CFG 블록이 모두 0개다. capacity는 배열 원소 크기×용량이고 객체 헤더나 다른 자료구조 크기는 포함하지 않는다. 별도 소유자가 CFG를 보관할 가능성이나 GC 시점까지 포함한 프로세스 메모리 감소량 측정은 아니다.

- 실제 CPython 3.12.15 대조 전체 92개: 완전 일치 87개, 기존 변동값 제외 일치 3개, C# 검사 2개 실패 보고 없음.
- class exception table의 CPython 대조 검사 통과. 이는 table 유무/범위/보호 opcode 검사이며 CPython과 전체 테이블 배치 동일성을 주장하지 않는다.
- 일반 Release 빌드 통과(기존 tokenizer nullable 경고 1개, 오류 0개).
- Godot 소스 직접 포함 빌드 경고/오류 0개. 기존 17개 및 추가 slice/offset/큰 generator 3개, C# helper 검사 통과.

원시 참조·메타데이터 검사와 회귀 요약은 round49-validation.json, 전체 출력은 `.artifacts/round49-tests.json`에 보존했다. 실행 시간 향상률은 이번 변경에서 측정하지 않았다.

## 다음

instruction fetch에서 CACHE를 일반 명령처럼 처리하는 비용과 소스 위치 갱신 비용을 조사한다. CACHE를 건너뛸 경우 점프 목적지, 예외 위치, generator 재개 및 tracing 의미를 먼저 검증해야 한다. 실행 배열 축소나 adaptive specialization을 한꺼번에 적용하지 않는다. 이번 변경은 미커밋 상태다.
