# 9차 class Exception Table 전달과 byte offset 정합성

기준일: 2026-10-09. 8차 변경은 `6554d8d`로 커밋했다. 이번 9차 변경은 아직 커밋하지 않았다.

## 수정한 문제

`CompileClassBody`는 CFG/assembler에서 만든 Exception Table을 code object 생성자에 전달하지 않았다. class의 table이 비어 있어 내부 try/except가 동작하지 않았다. assembler 결과를 class code object에 전달하도록 수정했다.

class 컴파일을 시작할 때 별도의 table 상태를 만들고 종료 시 부모 참조를 복원한다. 부모 table을 복제하는 대신 실제 부모 참조를 저장한다. 중첩 class 이후 `_isInClassBody`도 원래 값으로 복원한다. 이 flag를 무조건 false로 바꾸면 outer class의 free variable이 mapping 우선 조회를 건너뛰었다.

독립적인 metadata 검증에서 다른 기존 오류도 드러났다. 최종 instruction 배열에는 CACHE와 EXTENDED_ARG가 이미 개별 2바이트 word로 들어 있다. 그런데 assembler의 Exception Table range 계산과 VM의 index→byte offset 계산이 cache 크기를 다시 더했다. 두 계산이 같은 방식으로 잘못 늘어나 일부 행동 테스트는 통과했지만, table 보호 구간이 실제 bytecode 길이 밖에 놓였다.

최종 배열을 순회하는 table builder는 word마다 2바이트만 더하도록 수정했다. VM의 index→offset은 `index * 2`로 바꿨으며 기존 index 유효 범위 검사는 유지했다. 이 변환은 O(n)에서 O(1)이 됐다. CFG 단계의 아직 펼치지 않은 instruction 크기 계산은 유지한다.

## CPython 3.12 비교

공식 고정 소스는 [manifest](round5-cpython-manifest.json)를 재사용한다.

- [compile.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/compile.c)의 code-unit 조립 경로와 [assemble.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/assemble.c)의 exception table 생성·code object 전달 경로를 확인했다.
- 실제 CPython 3.12.15의 `dis.Bytecode(...).exception_entries`와 `dis.get_instructions(..., show_caches=True)`를 사용했다. C# 검사는 VM 실행 없이 같은 소스를 compile하여 각 code unit을 수집한다.

[비교 도구](../../../tools/compare_exception_tables.py)는 8개 code unit의 table 유무, 보호된 BINARY_OP/RAISE_VARARGS 유무, byte range·handler target의 유효 범위와 depth의 비음수 여부, instruction index↔offset 변환 및 nonempty table 소유권 독립성을 확인한다. 기준 바이너리는 실패하고 최종 Release·Debug는 통과했다. raw entry 목록은 함께 보존하며, CPP와 SharpPy의 table 전체·depth·lasti가 byte 단위로 동일하다는 판정은 하지 않는다.

## 회귀 결과

- [class exception table](../../../tests/class_exception_table_regression.py): handler/else/finally, bound exception 이름 정리, 중첩 class·함수, 미처리 예외 전파와 finally.
- [nested class state](../../../tests/class_nested_state_regression.py): inner class 뒤 outer class 문맥 복원 및 mapping의 free-variable override.
- [exception offset](../../../tests/exception_offset_regression.py): cache를 가진 호출이 여러 번 나온 뒤 함수/class에서 발생한 예외 처리.

기준 8차 바이너리에서는 세 파일이 실패하고 최종 구현은 실제 CPython과 일치했다. table 전달만 수정한 중간 구현에서는 offset 행동 회귀가 통과했지만 metadata 검사는 실패했다. 행동 회귀와 독립적인 physical bytecode 검증을 함께 유지한다.

최종 Release 평가 44개: 출력 일치 38개, 기존 출력 차이 4개, native 실패 보고 없음 2개. 새 실패는 없다. Release·Debug 빌드, Debug의 세 새 회귀, CPP metadata 비교, C# frame/descriptor checks도 통과했다. harness의 nullable code-object 경고도 명시적인 null 검사로 정리했다.

별도 compatibility 9개는 일치 2개, 미해결 7개다. class body try/except와 class mapping 최소 사례는 이제 통과한다. 새로 확인한 [LookupError builtin](../../../tests/compatibility/lookup_error_builtin.py)은 등록/상속 구조가 미완성이어서 별도 실패 fixture로 기록했다. 이 문제를 class table 수정의 실패로 분류하거나 기본 통과 수에 섞지 않는다.

## 성능 측정

공통 workload에 exception_handling을 추가해 총 12개 작업을 수집한다. 작업 크기 10,000, 각 process에서 10회 warmup 및 10개 sample, 기준/최종 각각 3개 독립 process를 번갈아 실행했다. 빌드와 측정은 분리했고 모든 반환값을 검증했다. 아래는 총 30개 sample의 중앙값이다.

| 작업 | 이전 ms | 이후 ms | 할당 B, 전후 동일 |
|---|---:|---:|---:|
| addition | 14.046 | 14.294 | 1,841,352 |
| inplace_addition | 16.212 | 15.974 | 1,841,352 |
| arithmetic | 14.065 | 14.280 | 2,401,352 |
| function_call | 13.145 | 12.478 | 8,082,560 |
| attributes | 13.664 | 13.371 | 2,254,224 |
| dictionary | 13.220 | 12.600 | 3,926,616 |
| comprehension | 3.921 | 3.717 | 1,464,496 |
| bound_builtin | 6.902 | 6.638 | 1,784,464 |
| locals_observation | 12.749 | 12.663 | 1,761,720 |
| frame_observation | 12.772 | 12.099 | 1,522,000 |
| class_locals_observation | 16.060 | 10.818 | 3,054,120 |
| exception_handling | 147.601 | 101.315 | 10,561,352 |

exception_handling의 중앙값은 약 31.4% 감소했지만 사분위 범위는 이전 100.061–154.969ms, 이후 98.517–145.603ms로 많이 겹친다. 일부 process/sample에 변동이 커서 안정적인 속도 향상이나 동일한 장기 개선율을 확정하지 않는다. 다른 작업의 사분위 범위도 겹쳤다. 정상 상태 thread 할당량은 모든 작업에서 동일했고 compiler/최초 실행 비용은 이 표로 판단하지 않는다.

원자료: [성능](round9-performance.json), [전체 평가](round9-tests.json), [기준 새 회귀](round9-regressions-before.json), [기준 metadata](round9-tables-before.json), [table 전달 후 offset 수정 전 metadata](round9-tables-intermediate.json), [최종 Release metadata](round9-tables-final.json), [Debug metadata](round9-tables-debug.json), [compatibility 재평가](round9-known-gaps.json).

## 다음 후보와 재현

module namespace wrapper의 live 조회·삭제·views 동기화가 다음 구조 후보다. LookupError 등록/상속 및 기존 local class free-variable 사례도 별도 호환성 후보로 남긴다. Exception Table의 전체 encoded format 및 모든 handler depth/lasti 정합성은 선별 CPP 비교를 확대해 검증한다.

```powershell
& $referencePython tools/compare_exception_tables.py --dotnet "$taskDotnet\dotnet.exe" --output .artifacts/exception-tables.json
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --files tests/class_exception_table_regression.py tests/class_nested_state_regression.py tests/exception_offset_regression.py
```
