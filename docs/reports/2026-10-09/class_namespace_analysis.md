# 8차 class namespace mapping과 호출 실패 상태

기준일: 2026-10-09. 7차 변경은 `d24e395`, 이 보고서의 8차 변경은 `6554d8d`로 커밋했다.

## 실행 의미와 수정

class body의 `locals()`와 `f_locals`는 실제 namespace mapping을 반환해야 한다. SharpPy는 custom `__prepare__` 결과를 저장하고도 별도 dictionary를 반환했고, 일반 class에는 mapping 없이 scope dictionary만 사용했다.

일반 class에는 실제 `PyDict` namespace를 만들고, `__prepare__`가 있으면 반환된 객체를 그대로 사용한다. 두 관찰 경로 모두 이 객체를 반환한다. class 이름 읽기·쓰기·삭제도 같은 mapping을 사용한다. 삭제된 prepare 초기값을 오래된 scope 복사본에서 다시 읽지 않도록, mapping miss 뒤에는 globals와 builtins를 조회한다. 정확한 PyDict는 직접 조회하고 custom mapping은 item protocol을 호출한다.

추가 회귀로 드러난 연관 오류도 수정했다.

- `type.__new__`의 descriptor 경로가 dict subclass를 거부했다. 실제 dict 저장소를 받아 읽으며, 사용자 `items()`/`__getitem__()` override를 호출하지 않는다. 일반 mapping을 dict로 인정하지 않는다.
- 실패한 keyword 호출 뒤 `KW_NAMES`와 caller scope 깊이가 남았다. 성공·실패 모두 `finally`에서 정리한다.
- class free variable을 일반 `LOAD_NAME`으로 우회하던 compiler variation이 있었다. CPython 3.12의 `LOAD_LOCALS` 및 `LOAD_FROM_DICT_OR_DEREF`를 생성하고 실행하도록 구현했다. mapping에 이름이 있으면 우선 사용하고, 없으면 closure cell을 읽는다. stack effect 및 기존 CFG cell offset 보정 경로도 연결했다.
- 서로 다른 함수 안의 같은 이름 class가 root 검색에서 다른 class의 symbol table을 가져왔다. 현재 lexical parent의 class child를 먼저 선택한다. AST identity로 전체 symbol table를 연결하는 재설계를 완료한 것은 아니며 legacy root fallback은 남아 있다.

기존 Release 진단 블록이 namespace의 사용자 `items()`를 호출하고 로그를 출력하던 경로도 제거했다.

## CPython 비교 근거

출처는 공식 v3.12.0/v3.12.15의 고정 commit과 [manifest](round5-cpython-manifest.json)를 사용한다. 실제 실행 기준은 전용 CPython 3.12.15다.

- [bltinmodule.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/bltinmodule.c): class body에 prepare 결과 `ns`를 전달하고 같은 객체를 metaclass에 넘긴다. prepare가 없으면 새 dict를 생성한다.
- [bytecodes.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/bytecodes.c): LOAD_NAME의 mapping→globals→builtins 경로, DELETE_NAME의 mapping 삭제 및 NameError 변환, class mapping/closure 조회, CALL의 keyword 상태 소비를 확인했다. DELETE_NAME은 KeyError뿐 아니라 mapping의 다른 삭제 오류도 NameError로 바꾼다. 이 동작은 실제 3.12.15 실행으로 재확인했다.
- [compile.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/compile.c): class free/cell 읽기에 LOAD_LOCALS를 먼저 생성하고 LOAD_FROM_DICT_OR_DEREF를 사용하는 경로를 확인했다.
- [typeobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/typeobject.c): type_new의 dict 입력 및 복사 경로를 참고했다.

[비교 도구](../../../tools/compare_class_closure.py)는 같은 회귀 소스를 실제 CPython으로 compile/disassemble하고 SharpPy compiler 결과와 비교한다. 세 class의 parent cellvars, class varnames/cellvars/freevars, 두 opcode 쌍의 숫자·operand가 모두 일치했다. opcode는 87과 176이다. 이는 선택한 metadata와 opcode 쌍의 일치이며 전체 bytecode·qualname·line table 동등성 판정은 아니다.

## 회귀 검증

- [class mapping](../../../tests/class_locals_mapping_regression.py): identity, 이후 변수 관찰, mapping 쓰기, DELETE_NAME, 전역 이름 삭제 방지, 반환 후 frame 참조.
- [prepared namespace](../../../tests/prepared_namespace_regression.py): prepare 객체 identity, dict subclass item 메서드, 초기값 삭제 뒤 global fallback, 읽기 오류 전파, 삭제 오류 변환.
- [type_new dict subclass](../../../tests/type_new_dict_subclass_regression.py): 실제 저장소 읽기, 사용자 override 우회, 임의 mapping 거부.
- [call failure state](../../../tests/call_failure_state_regression.py): 실패한 metaclass/class body 뒤 다음 호출과 module locals의 정상 동작.
- [class closure mapping](../../../tests/class_closure_mapping_regression.py): outer parameter, mapping 우선순위, class local 이름의 global fallback, 서로 다른 함수의 같은 class 이름.

다섯 파일은 기준 SharpPy에서 실패하고 CPython에서 통과했다. 최종 SharpPy에서는 CPython과 일치한다. 처음 일반 class mapping을 적용했을 때 기존에 동작하던 outer parameter 사례가 실패하는 것을 발견했고, opcode 경로를 수정한 뒤 기준과 최종 모두 통과하는 것을 확인했다.

Release 전체 41개 평가: 출력 일치 35개, 기존 출력 차이 4개, native 실패 보고 없음 2개. 새 실패는 없다. Release·Debug 빌드, Debug의 다섯 새 회귀, C# frame/descriptor checks도 통과했다. Debug 로그의 전체 출력 일치를 통과 조건으로 사용하지 않는다.

별도 compatibility fixture 8개를 재평가했다. 이전 class locals mapping 최소 사례는 이제 일치하고, 나머지 7개는 미해결이다. class body 내부 try/except를 추가 최소 사례로 남겼다. module locals의 stale 읽기, local class를 다른 class body에서 사용하는 기존 사례 등도 아직 실패한다. 이 결과를 기본 41개 평가의 통과 수에 섞지 않았다.

## 성능 확인과 dispatcher 분리

class locals를 10,000회 읽는 작업을 추가해 공통 workload는 11개가 됐다. 모든 결과를 검증한다. 기준/최종 순서를 번갈아 각각 3개 독립 process에서 실행하고, 작업당 process별 10회 warmup과 10개 sample을 수집했다. 표는 총 30개 sample의 중앙값이다.

첫 구현의 측정에서는 일부 기본 작업에 약 4–7%의 시간 증가와 사분위 범위 분리가 있었다. CALL 및 mapping protocol의 예외 처리를 큰 ExecuteInstruction 함수 밖으로 분리했다. CALL 본문은 method boundary와 argument 전달을 제외하고 그대로 옮겼음을 확인하고 회귀와 CPython 비교를 다시 실행했다.

최종 반복 측정에서는 앞선 기본 작업의 일괄 증가가 재현되지 않았다. 두 측정 사이 환경 변동과 JIT 코드 배치 효과를 분리하지 못했으므로, helper 분리가 퇴행의 원인을 확정적으로 제거했다고 주장하지 않는다. 중간 원자료도 보존한다.

| 작업 | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
|---|---:|---:|---:|---:|
| addition | 15.300 | 15.497 | 1,841,352 | 1,841,352 |
| inplace_addition | 17.759 | 16.904 | 1,841,352 | 1,841,352 |
| arithmetic | 15.508 | 15.726 | 2,401,352 | 2,401,352 |
| function_call | 13.847 | 13.620 | 8,082,560 | 8,082,560 |
| attributes | 14.539 | 14.292 | 2,254,216 | 2,254,224 |
| dictionary | 14.248 | 14.116 | 3,926,616 | 3,926,616 |
| comprehension | 4.189 | 4.185 | 1,464,496 | 1,464,496 |
| bound_builtin | 7.544 | 7.336 | 1,784,464 | 1,784,464 |
| locals_observation | 14.181 | 13.558 | 1,761,720 | 1,761,720 |
| frame_observation | 18.073 | 13.873 | 1,522,000 | 1,522,000 |
| class_locals_observation | 23.157 | 17.980 | 9,214,160 | 3,054,120 |

class_locals_observation의 할당량은 6,160,040B, 약 66.9% 감소했다. 매번 namespace dictionary를 복사하던 비용을 줄였지만 class 이름용 PyString 및 namespace/반환용 자료구조 비용은 남는다. attributes는 class 생성이 포함되어 8B 증가했고 다른 기존 작업의 할당량은 동일하다.

최종 class 작업 시간의 사분위 범위는 이전 22.677–24.142ms, 이후 17.135–18.700ms다. 이 측정에서 중앙값은 약 22.4% 감소했다. 첫 측정에서도 class 중앙값은 감소했지만 범위는 겹쳤다. 절대 시간과 일부 작업의 변동이 커서 모든 작업의 속도 향상이나 장기적인 고정 개선율을 주장하지 않는다.

원자료: [최종 성능](round8-performance.json), [중간 성능](round8-intermediate-performance.json), [최종 전체 비교](round8-tests.json), [변경 전 회귀](round8-regressions-before.json), [최종 새 회귀](round8-regressions-final.json), [실제 CPython metadata/opcode 비교](round8-bytecode.json), [compatibility 재평가](round8-known-gaps.json).

## 다음 후보와 재현

class body의 Exception Table 전달 경로를 우선 조사한다. `CompileClassBody`의 code object 구성에는 table 전달이 빠져 있고, 단순 class 내부 try/except가 실패한다. module wrapper의 namespace 생성·삭제·views 동기화는 여전히 별도 구조 작업이다. metaclass identity, 모든 임의 mapping, hidden locals 및 전체 class symbol table binding의 호환성까지 이번 결과로 완료 판정하지 않는다.

```powershell
& $referencePython tools/compare_class_closure.py --dotnet "$taskDotnet\dotnet.exe" --output .artifacts/class-closure.json
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --files tests/class_locals_mapping_regression.py tests/prepared_namespace_regression.py tests/type_new_dict_subclass_regression.py tests/call_failure_state_regression.py tests/class_closure_mapping_regression.py
```
