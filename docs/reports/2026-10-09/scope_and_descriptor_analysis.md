# 3차 scope 판정과 descriptor 호출 개선

기준일: 2026-10-09. 2차 변경은 `0e18b36`, 이 보고서의 3차 변경은 `8089546`으로 커밋했다.

## 결과

함수 이름으로 지역/전역 scope를 추측하던 코드를 명시적인 ScopeType 판정으로 바꿨다. 함수 이름이 `module`, `abc`, `typing` 등일 때 매개변수가 전역 변수를 덮어쓰던 문제를 수정했다. 함수 반환 시 내부 class body 접두사 검사는 ordinal 비교를 사용한다.

인덱스 읽기·쓰기·삭제에서 builtin descriptor의 receiver를 따로 전달해 인자 배열의 조립·분리 복사를 줄였다. 일반 함수 인자 배열과 frame 표현은 유지했다.

10,000회 작업의 함수 호출 시간은 기준 바이너리 27.96ms → 최종 14.90ms로 감소했다. dict 작업의 할당량은 4.81MB → 3.93MB로 감소했다. 개선 대상 밖의 작은 시간 차이는 변동 범위와 함께 보고한다.

## 병목을 좁힌 근거

2차 CPU trace를 Speedscope로 변환하고, native StartsWith/EndsWith의 상위 C# 호출 스택을 확인했다. 함수 인자 바인딩 → AssignVariable → IsModuleScope, 그리고 함수 반환 경로가 확인됐다. topN 목록의 문자열 함수 이름만 보고 전체 문자열 처리 방식을 바꾸지 않았다.

`IsModuleScope`는 `<module>`과 일부 표준 모듈 이름을 비교하고, `.py` suffix와 `module` substring을 검사했다. 이를 함수 이름에도 적용해 `def module(value)`의 인자 바인딩이 전역 `value`를 수정했다. 지역 변수 배열에 정상 값이 있으므로 반환값만 검사하는 테스트에서는 오류가 숨겨질 수 있었다.

CPython v3.12.15 `Python/ceval.c::initialize_locals`는 인자를 localsplus에 저장한다. 함수 이름의 문자열 패턴으로 인자 소유 scope를 판정하지 않는다. SharpPy에서는 기존 scope dictionary도 필요한 경로가 있어 유지하되, 명시적인 scope 종류에 따라 대상 dictionary를 선택했다. 기존 `global` 선언 처리는 우선 적용한다.

새 scope 테스트는 `module`, `module_counter`, `abc`, `contextlib`, `functools`, `typing`, defaults·keyword 호출, locals(), closure, bound method의 self, 명시적 global 할당, 한국어 함수·class 이름을 확인한다. 기준 바이너리는 첫 `module` 사례에서 실패했고 최종 바이너리는 CPython과 출력이 일치한다.

## 변경 범위

### scope 판정과 반환 경로

`PyScopeChain.AssignVariable`은 Global/Module scope를 enum으로 판정하고 나머지는 현재 scope에 저장한다. class body의 별도 이름 검사도 같은 저장 동작을 반복하고 있어 제거했다. scope 이름 추측 함수는 삭제했다.

`RETURN_VALUE`의 `<class_body_` 검사는 생성된 내부 식별자이므로 StringComparison.Ordinal을 사용한다. 일반 Python 문자열 메서드의 의미나 locale 처리를 일괄 변경하지 않았다. 최종 CPU sampling에서 native StartsWith/EndsWith가 상위 15개에서 빠졌다. 이것은 전체 프로그램에 문화권 기반 문자열 처리가 없다는 뜻은 아니다.

### descriptor의 receiver 전달

호출 경로를 확인하면 bound builtin의 `PyBoundMethodDescriptor.Call`은 이미 `_implementation(Instance, args, kwargs)`를 직접 호출하고 있었다. 이 경로는 수정하지 않았다.

추가 복사는 VM의 BINARY_SUBSCR/STORE_SUBSCR/DELETE_SUBSCR에서 unbound PyMethodDescriptor를 호출할 때 발생했다. VM이 `[receiver, key, ...]` 배열을 만들고 descriptor.Call이 receiver를 뺀 배열을 다시 만들었다.

새 내부 CallWithReceiver 경로는 정확한 PyMethodDescriptor 타입에서 receiver와 읽기용 인자 배열을 직접 callback에 전달한다. C# descriptor subclass에는 기존 전체 인자 배열을 만들어 virtual Call로 넘겨 override를 유지한다. 사용자 Python 함수, wrapper descriptor, 일반 unbound 호출 및 기존 bound 호출 경로는 유지했다.

CPython `Objects/abstract.c::PyObject_GetItem`, `PyObject_SetItem`, `PyObject_DelItem`의 receiver와 key/value를 직접 slot에 전달하는 구조를 참고했다. C# callback 시그니처는 유지했으므로 작은 인자 배열 하나는 여전히 생성한다. 완전한 vectorcall이나 값 스택 재설계가 아니다.

dict에서 10,000회 쓰기와 10,000회 읽기에 제거된 배열 크기는 각각 48바이트와 40바이트이며, 측정된 감소량은 정확히 880,000바이트다.

## 성능 측정

CPU·OS·.NET·CPython 환경은 [2차 보고서](structural_analysis.md)와 같다. 입력 10,000개, 워밍업 10회, 표본 10개, variant별 프로세스 3개를 사용했다. 보관한 기준 바이너리와 최종 바이너리를 번갈아 실행하고 두 번째 쌍은 순서를 바꿨다. 표는 프로세스별 중앙값 3개의 중앙값이다.

receiver가 이미 분리된 bound builtin의 negative control 작업을 추가해 총 8개 작업을 측정했다. 새 작업은 list.append를 bound method로 저장한 뒤 반복 호출하고 결과 합계를 검사한다. 다른 7개 작업의 소스와 입력은 같다.

| 작업 | 기준 ms | 최종 ms | 시간 감소 | 기준 MB | 최종 MB |
|---|---:|---:|---:|---:|---:|
| 덧셈 | 14.98 | 15.43 | -3.0% | 1.84 | 1.84 |
| in-place 덧셈 | 17.05 | 17.50 | -2.6% | 1.84 | 1.84 |
| 혼합 산술 | 15.14 | 15.07 | 0.5% | 2.40 | 2.40 |
| 함수 호출 | 27.96 | 14.90 | 46.7% | 12.00 | 12.00 |
| 속성 읽기·쓰기 | 14.30 | 14.22 | 0.6% | 2.25 | 2.25 |
| dict 저장·조회 | 14.11 | 13.78 | 2.3% | 4.81 | 3.93 |
| list comprehension | 3.90 | 4.02 | -3.1% | 1.46 | 1.46 |
| 저장한 bound builtin 호출 | 7.32 | 7.35 | -0.4% | 1.78 | 1.78 |

마이너스는 중앙값 증가다. 함수 호출의 사분위 범위는 기준 약 27.61–28.23ms, 최종 약 14.54–15.68ms로 분리된다. 다른 항목의 작은 시간 차이는 사분위 범위가 겹치며 확정적인 향상/퇴행으로 해석하지 않는다. dict 작업에서는 시간보다 결정적인 할당량 감소를 근거로 삼는다. 온도와 전원 모드를 자동 기록하지 못한 한계는 유지한다.

최종 [원시 표본·해시·사분위 범위](round3-performance.json)와 [중간 단계 결과](round3-intermediate-performance.json)를 보관했다. scope-only 단계에서 함수 호출 시간이 줄고 할당은 그대로였다. descriptor 변경은 dict 할당을 줄였다. ordinal 적용 이전/이후의 작은 시간 차이는 단독 개선율로 주장하지 않는다.

## 검증

- Release 빌드 성공, C# descriptor 호출 검사 통과.
- 기본 평가 30개: 출력 일치 24개, 기존 출력 차이 4개, C# 호출 테스트에서 실패 보고 없음 2개. 기능별 호환성 통과율이 아니다.
- 기본 Debug 빌드 및 새 scope/descriptor Python 테스트 통과.
- bound/unbound builtin 호출, list/dict 인덱스 수정·삭제, tuple/string 읽기, 누락 인자와 IndexError 검사.
- C# callback의 receiver·인자·kwargs·반환값·예외 검사. VM subscription의 descriptor subclass Call override와 기존 bound 동작 유지 확인.
- 기존 호환성 문제 5개는 별도 재현 목록에서 계속 실패한다. 이번 변경의 새 회귀로 표시하지 않았다.

[테스트 집계](round3-tests.json)에 판정과 종료 코드를 저장했다. 원시 로그·빌드 로그·trace와 보관한 기준 실행 파일은 `.artifacts/round3`에 있다.

## 다음 조사 대상

함수 호출 10,000회 작업의 할당량은 아직 약 12MB로 그대로다. scope chain·global/local scope 객체·dictionary·frame·값 스택·인자 배열의 비용을 호출 단위로 분해하는 것이 다음 우선 과제다. CPython의 작은 interpreter frame과 lazy한 frame/locals 노출 구조를 참고하되, frame·locals 관찰, closure, generator, C# 외부 호출의 수명을 검증해야 한다.

최종 CPU sampling의 상위는 ExecuteFrame, ExecuteInstruction, PyMethodDescriptor.Call 등이다. 단일 native 함수 비용을 지운 뒤 VM 자체의 비용이 더 잘 보이지만, 여러 작은 작업을 묶은 sampling 비율을 실제 애플리케이션 비율로 확대하지 않는다.

이미 receiver를 직접 전달하는 경로를 다시 최적화하는 중복 작업을 피하고, CALL/vectorcall과 scope dictionary의 실제 사용 지점을 먼저 확인한다. 이번에도 CLR boxing을 주요 원인으로 가정하지 않았다.

## 재현

빌드·평가·benchmark 명령은 [1차 보고서](analysis.md)의 명령을 사용한다. benchmark는 이제 8개 작업을 수집한다.

```powershell
& "$taskDotnet\dotnet.exe" tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll --descriptor-check
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --files tests/scope_binding_regression.py tests/builtin_descriptor_regression.py
```

CPU trace는 기존 dotnet-trace 명령으로 수집하고 Speedscope로 변환해 호출자를 확인할 수 있다. 출력 폴더는 미리 만든다.

```powershell
& .artifacts/dotnet-tools/dotnet-trace.exe convert --format Speedscope --output .artifacts/round3/profile .artifacts/round3/final.nettrace
```
