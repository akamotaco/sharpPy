# 2차 구조 비교와 할당 비용 개선

기준일: 2026-10-09. 1차 변경을 `71f34ad`로 커밋한 뒤 수행한 분석이다. 2차 변경은 `0e18b36`으로 커밋했다. 후속 결과는 [3차 scope·descriptor 개선](scope_and_descriptor_analysis.md)에 있다.

## 결론

SharpPy는 CPython 3.12의 컴파일 파이프라인과 Python 객체 의미론을 따르지만, 실행 데이터 배치와 호출 경로까지 같지는 않다. 이번에는 값 표현을 바꾸기 전에 불필요한 타입 조회·예외와 매 명령마다 생기는 C# 클로저를 제거했다.

같은 바이너리 기준으로 번갈아 실행한 측정에서 in-place 덧셈은 44.29ms → 15.73ms, 호출당 할당량은 14.16MB → 1.84MB로 감소했다. CLR boxing을 제거한 결과가 아니라, Python 객체 모델을 유지하면서 실행 보조 비용을 줄인 결과다.

## CPython과의 구조 비교

공식 CPython v3.12.15 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 소스를 사용했다. 기존 파일에 frame·long·type·method·ceval 소스를 추가했으며, [출처 manifest](round2-cpython-manifest.json)에 14개 파일의 URL과 SHA-256을 기록했다.

| 영역 | CPython 3.12 | SharpPy 현재 구현 | 평가 |
|---|---|---|---|
| 컴파일 | AST → instruction sequence → CFG → assembler | 같은 단계의 클래스·모듈 구성 | 큰 구조가 유사함. 내부 변환의 세부 정확성은 별도 검증 필요 |
| 숫자 | PyLongObject, 작은 정수 캐시, 타입 숫자 슬롯 | PyInt 클래스 + BigInteger 값, SmallIntCache, C# 가상 메서드와 dunder 조회 | 객체 의미론은 유사하지만 물리 표현과 슬롯 실행 경로는 다름 |
| frame | 가벼운 `_PyInterpreterFrame`, 필요시 PyFrameObject 노출 | 모든 실행 frame이 PyFrame 참조 객체 | 매 함수 호출의 객체·컨테이너 비용을 별도로 확인해야 함 |
| locals/cells/stack | localsplus 저장소에 지역 변수·셀·값 스택의 참조 배치 | LocalsPlus 배열, Cells 배열, List 기반 PyStack, ScopeChain과 dictionary | fast locals는 도입했지만 데이터와 관리 객체가 분산됨 |
| 함수 호출 | CALL·vectorcall·frame 초기화에서 스택의 인자를 전달 | PyObject[] 인자, scope chain 및 frame 생성, 일부 bound method의 추가 인자 배열 | CLR boxing보다 인자·frame·scope 할당이 우선 후보 |
| 특수 연산 | 타입 slot, type-only lookup, descriptor binding | 기존에는 일반 GetAttribute와 누락 속성 예외 | 이번에 in-place 연산을 type-only 조회로 정렬 |
| 타입 캐시 | 타입 변경과 상속 관계를 고려한 무효화 | version tag 캐시가 있으나 자손 무효화 TODO, 직접 dictionary 수정도 존재 | 이번 경로는 캐시 없이 MRO를 검색함 |
| 실행 명령 | compact code unit과 분리된 메타데이터 | opcode·인자·위치·파일·CFG 참조를 포함한 ByteCodeInstruction struct | 박싱은 없지만 원소 크기·값 복사·캐시 효율 검토 대상 |
| 함수 생성 | code·globals·defaults·closure 등 함수 상태 구성 | 이 상태와 C# 구현 delegate, ParentScope 등을 함께 유지 | 캡처된 호출 frame의 생존 기간과 중복 구현도 후속 조사 대상 |

근거 파일은 `Include/internal/pycore_frame.h`, `Python/ceval.c`, `Python/bytecodes.c`, `Objects/abstract.c`, `Objects/typeobject.c`, `Objects/longobject.c`다. frame의 물리 배치나 참조 카운팅을 C#에 그대로 복사해야 한다는 뜻은 아니다. GC 환경에서 Python의 관찰 가능한 실행 의미를 지키면서 비용을 줄이는 것이 기준이다.

## boxing과 Python 객체 할당의 구분

`PyInt`는 참조형 클래스다. `PyInt → PyObject`는 기반 클래스 참조로의 변환이고 `(PyInt)value`는 참조형 cast다. CLR의 값 형식 boxing/unboxing과 다르다. `PyInt.Value`의 BigInteger는 구조체이며 작은 값에는 큰 정수용 배열이 필요하지 않을 수 있다. 값이 커지면 BigInteger의 내부 숫자 저장소 비용도 고려해야 한다.

선별한 산술·스택·VM dispatch·함수 호출·frame 생성 메서드의 정적 IL에서는 `box`, `unbox`, `unbox.any`가 없었다. 이것은 전체 라이브러리·CLR 내부·JIT 이후 모든 경로에 boxing이 없다는 증명이 아니다.

같은 .NET 8.0.31 x64 프로세스에서 10,000회 수행한 별도 할당 실험은 다음과 같다. 저장용 배열은 측정 전에 만들고, 새 객체는 배열에 보관해 살아 있게 했다. 참조 변환 실험은 JIT 최적화가 적용될 수 있는 정상 C# 코드다.

| 실험 | 할당 바이트 |
|---|---:|
| 기존 PyInt의 기반 클래스 참조 변환과 cast | 0 |
| 작은 값으로 새 PyInt 10,000개 생성 | 400,000 |
| 기존 SmallIntCache 범위의 값 10,000회 사용 | 0 |
| CLR int를 object 배열에 boxing 10,000회 | 240,000 |
| 이미 boxed된 int를 10,000회 unboxing | 0 |

따라서 “unboxing을 줄이면 매번 객체 할당이 사라진다”는 전제는 맞지 않는다. 여기서 할당을 만드는 것은 새 객체의 생성이다. 숫자 wrapper를 줄이는 목표는 유효하지만, 실제 hot path의 비용을 CLR boxing과 구분해야 한다. 실험과 IL은 [IL 근거](round2-il-final.json)에 보관했다.

`newobj`가 있다고 모두 힙 할당은 아니다. IL 검사 도구는 구조체 생성자를 구분한다. 예를 들어 DefaultInterpolatedStringHandler의 생성과 참조형 display class의 생성은 같은 비용으로 취급하지 않는다. 정적 생성 지점 수와 실제 호출 횟수도 다르다.

## 적용한 개선

### in-place 연산의 type-only 조회

`__iadd__` 등의 특수 연산을 일반 `GetAttribute`로 조회하면 인스턴스 속성·`__getattribute__`·`__getattr__`가 개입하고, 누락 때마다 AttributeError를 만든다. 기존 코드는 메서드나 descriptor가 던진 예외도 삼키고 일반 연산으로 넘어갔다.

새 경로는 타입 MRO의 ClassDict/TypeDict에서 특수 메서드를 찾는다. 누락은 null로 표현한다. 함수는 self와 인자를 직접 전달해 임시 bound method와 추가 인자 배열을 피하고, 다른 descriptor는 바인딩한 뒤 호출한다. descriptor 및 사용자 메서드의 예외는 전달하고 NotImplemented 결과에서 fallback한다.

CPython의 `Objects/typeobject.c::lookup_maybe_method`, `slot_nb_inplace_*`와 `Objects/abstract.c::binary_iop1`을 대조했다. CPython도 일반 속성 hook을 우회하며, 가능한 경우 임시 bound method를 만들지 않는다.

기존 타입 캐시를 이 경로에 연결하지 않았다. 자손 타입의 캐시 무효화가 아직 완전하지 않아 현재 MRO를 직접 검색한다. CPython식 슬롯·캐시로 더 줄일 수 있지만, 변경·삭제·상속에 대한 무효화가 먼저 필요하다.

새 테스트 14개는 이전 바이너리에서 10개 실패했고 최종 바이너리에서는 모두 통과했다. 인스턴스 속성 우회, attribute hook 우회, ValueError/AttributeError 전달, staticmethod/classmethod/property/사용자 descriptor, 비호출 객체, 상속된 메서드 변경·삭제, int 서브클래스를 검증했다.

### 명령 처리 메서드의 캡처 분리

`ExecuteInstruction` 안의 MAKE_FUNCTION 구현이 frame·VM·closure·code를 캡처했다. C# 컴파일러는 참조형 display class 생성 코드를 메서드 진입점 IL offset 0에 배치했다. 함수 생성과 관계없는 LOAD_FAST·STORE_FAST·BINARY_OP·FOR_ITER에도 할당이 발생했다.

MAKE_FUNCTION 본문을 `ExecuteMakeFunction`으로 옮겼다. 본문은 들여쓰기와 마지막 switch break를 제외하고 원문과 같음을 자동 비교했다. 추가로 진단용 LINQ의 frame 캡처도 제거했다. 이제 함수 생성에 필요한 캡처는 함수 생성 경로에서만 일어난다.

중간 IL과 최종 IL에서 entry의 display class 생성이 사라진 것을 확인했다. [분리 전 IL](round2-il-before-dispatch.json)은 특수 조회 개선 후, dispatch 분리 전 상태다. 측정에서도 덧셈 10,000개 처리 시 분리 전후 할당량이 4,320,864바이트 감소했다.

이는 거대한 opcode switch를 유지할 때 C#의 변수 캡처 범위가 구조적인 성능 문제를 만들 수 있음을 보여 준다. CPython 알고리즘을 따른다는 이유로 C# 생성 코드의 할당 패턴까지 효율적이라고 가정하면 안 된다.

## 1차 커밋 대비 성능

SDK 8.0.425, 런타임 8.0.31, CPython 3.12.15, 동일 PC에서 측정했다. 워크로드·입력 10,000개·예상값은 1차와 동일하다. 각 프로세스에서 워밍업 10회와 표본 10개를 수집했고, 기준/최종 바이너리 쌍을 3회 실행했다. 가운데 쌍은 순서를 반대로 했다. 프로파일은 별도로 실행했다.

아래는 프로세스별 중앙값 3개의 중앙값이다. 시간 차이가 작은 항목은 전원·열·스케줄링의 영향을 받을 수 있다. 이 PC의 모든 작업 또는 .NET 자체에 대한 성능 배수가 아니다. MB는 1,000,000바이트 기준이고 현재 스레드의 누적 할당량 차이다.

| 작업 | 1차 커밋 ms | 최종 ms | 시간 감소 | 1차 MB | 최종 MB |
|---|---:|---:|---:|---:|---:|
| 덧셈 | 14.69 | 13.60 | 7.4% | 6.16 | 1.84 |
| in-place 덧셈 | 44.29 | 15.73 | 64.5% | 14.16 | 1.84 |
| 혼합 산술 | 16.75 | 13.56 | 19.1% | 13.92 | 2.40 |
| 함수 호출 | 27.53 | 25.07 | 8.9% | 21.12 | 12.00 |
| 속성 읽기·쓰기 | 14.69 | 13.31 | 9.4% | 13.78 | 2.25 |
| dict 저장·조회 | 15.83 | 12.95 | 18.2% | 14.89 | 4.81 |
| list comprehension | 4.22 | 3.68 | 12.9% | 4.35 | 1.46 |

[원시 표본·바이너리 해시·사분위 범위](round2-performance.json)를 보관했다. slot-only 중간 결과도 포함해 두 변경의 효과를 구분했다. 중간 버전에서는 `+=` 시간과 할당이 크게 줄고 일반 덧셈의 할당량은 그대로였다. dispatch 분리 후에는 모든 작은 작업의 할당량이 감소했다.

최종 CPU sampling에서는 ExecuteFrame, PyMethodDescriptor.Call, 문자열 StartsWith/EndsWith 비용이 상위에 남았다. 이전의 GenericGetAttribute는 상위 15개에서 빠졌다. 프로파일은 setup·워밍업과 여러 작업을 함께 포함하므로, 각각의 실제 애플리케이션 비중은 추가로 좁혀야 한다.

## 회귀 검증과 알려진 문제

- 기본 평가 목록 28개: 출력 일치 22개, 기존 출력 차이 4개, C# 호출 테스트에서 실패 보고 없음 2개. 파일 수를 기능별 호환성 비율로 해석하지 않는다.
- 이항 연산 및 새 특수 조회 테스트 통과.
- 함수 생성 테스트에서 defaults·kwdefaults·annotations·독립 closure·공유 cell·generator·coroutine 생성/close·async generator code flag 확인. 이전/최종 바이너리 모두 통과했다.
- Release 빌드, 기본 Debug의 특수 조회 테스트, DEBUG_VM_LOG Debug의 함수 생성 테스트 통과.
- 이항 연산 회귀 사례의 `--compile --dis` 출력이 기준/최종 바이너리에서 그대로 일치.
- 알려진 문제 재현 목록은 기존 3개와 새 coroutine.send·중첩 class의 free variable 사례, 총 5개 모두 SharpPy에서 실패한다. 정상 회귀 목록과 분리해 보고했다.

새 coroutine.send 최소 사례는 1차 바이너리에서도 Stack is empty로 실패한다. 함수 생성 코드를 옮겨 생긴 회귀가 아니다. 함수 생성 테스트는 생성·메타데이터·close만 검증하며 coroutine의 실제 실행 호환성까지 통과했다고 표시하지 않는다.

함수 안의 class 본문에서 같은 함수의 지역 class를 참조하는 최소 사례도 기준/최종 바이너리에서 모두 실패한다. 특수 descriptor 조회 테스트에서는 descriptor를 모듈에 정의해 이 별도 scope 문제를 분리했다. 이 사례는 `tests/compatibility/class_body_freevar.py`로 보관했으며, 지역 변수 배열을 갖춘 것만으로 class의 free variable 처리가 CPython과 같다고 판단할 수 없음을 보여 준다.

[테스트 집계](round2-tests.json)와 별도로 원시 로그, 빌드 로그, nettrace는 `.artifacts/round2`에 남겼다.

## 값 표현을 바꾸는 개선의 난도와 순서

CPython의 기본 VM 스택도 PyObject 참조를 저장한다. 내부에서 int·double을 직접 들고 다니는 tagged value 스택은 CPython의 물리 구조를 그대로 따라가는 변경이 아니라, 외부 호환성을 유지하면서 내부 표현을 바꾸는 별도 설계다.

단계적으로 접근한다.

1. 현재처럼 불필요한 클로저·예외·배열·임시 method 객체를 줄인다.
2. 작은 정수 캐시가 실제 생성 지점에서 사용되는지 평가한다. 기존 cache 실험의 0바이트를 VM 전체의 개선율로 확대하지 않는다.
3. 정확한 builtin 타입에 한정한 빠른 연산 경로를 검증한다. 서브클래스와 descriptor는 일반 경로로 복귀하고, 정수 범위 초과 시 BigInteger 의미를 유지한다.
4. CALL의 인자 배열과 frame/scope 생성 비용을 측정한다. vectorcall에 가까운 전달 방식이나 최소 frame 데이터 배치를 작은 변경으로 시도한다.
5. 충분한 근거가 남으면 내부 `PyValue` 구조체 스택의 제한된 prototype을 따로 만든다. Python/C# API 경계에서 객체로 변환하고 전체 회귀와 메모리 효율을 비교한 뒤 채택한다.

tagged value prototype에서 반드시 다룰 사항은 큰 정수·타입 변경, bool과 int, 서브클래스, `is`/`id`, 함수 인자와 반환, 컨테이너와 closure cell, `locals()`/frame 관찰, generator·coroutine 정지/재개, 예외 상태, C# 외부 호출이다. 작은 정수만 맞는 예제로 완료를 판단하지 않는다.

구조체는 관리 객체 참조를 정상 필드로 유지해야 GC가 추적할 수 있다. 값·tag·참조를 함께 보관하면 원소 크기가 참조 1개보다 커져 복사와 캐시 비용이 늘 수 있다. 값 형식을 object나 인터페이스로 넘기면 실제 CLR boxing을 새로 도입할 수도 있다. 프레임 pooling도 밖으로 노출되거나 정지된 frame의 수명을 먼저 정리해야 한다.

다음 우선 후보는 호출별 frame/인자 할당, attribute 처리의 문자열 비교와 descriptor 호출, 타입 캐시 무효화다. 전체 값 표현 재설계보다 근거가 분명하고 영향 범위를 작게 검증할 수 있다.

## 재현

기본 빌드·평가·측정 명령은 [1차 보고서](analysis.md)의 명령을 사용한다. 추가된 IL/할당 검사는 다음과 같다.

```powershell
& "$taskDotnet\dotnet.exe" tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll --inspect
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --files tests/inplace_special_lookup.py tests/function_creation_regression.py
& $referencePython tools/fetch_cpython_reference.py --tags v3.12.15 --output .artifacts/cpython-structural
```

기준 바이너리를 따로 보관하면 측정 도구의 `--harness`와 `--dll`로 선택할 수 있다. 호스트 옆의 sharppy.dll도 해당 기준 바이너리여야 한다. exe/runtimeconfig/deps 등 실행 파일 구성을 함께 보관한다.

```powershell
& $referencePython tools/benchmark.py --dotnet "$taskDotnet\dotnet.exe" --harness .artifacts/round2/before-host/PerformanceHarness.dll --dll .artifacts/round2/before-host/sharppy.dll --output .artifacts/round2/baseline-rerun.json
```
