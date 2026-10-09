# SharpPy 구현 평가 및 성능 개선 계획

## 호스트·라이브러리 조건 (2026-10-10 추가)

[38차 결과](reports/2026-10-10/exception_length_hint_analysis.md): args 변환의 len/length_hint 순서와 예외를 맞추고 __iter__의 Python 예외를 보존했다. 기본 81개와 Godot 소스 통합 회귀 11개 통과. 힌트가 없을 때 CLR 기본 확장을 사용한다. 공개 _operator.length_hint의 기존 별도 구현은 다음 대상으로 재현했다.

[37차 결과](reports/2026-10-10/exception_args_update_analysis.md): 예외 args 재대입·identity·삭제 거부와 변환 실패 시 상태를 맞췄다. 기본 79개와 Godot 소스 통합 회귀 10개 통과. 별도 캐시 필드 없이 관찰 tuple을 보존한다. 일반 iterable의 길이 힌트 callback은 다음 과제로 재현 테스트를 남겼다.

[36차 결과](reports/2026-10-10/stop_iteration_analysis.md): StopIteration 생성 인자·value·str/repr 및 generator 반환값을 맞췄다. 기본 77개와 Godot 소스 통합 회귀 9개 통과. 빈 종료 예외의 불필요한 인자 객체를 제거했다. 다음은 예외 args 재대입이며 별도 실패 fixture를 보존했다.

[35차 결과](reports/2026-10-10/bytearray_bytevalue_analysis.md): bytearray 항목의 __index__·bool 및 큰 정수 범위 오류를 맞추고 값 callback 후 길이를 검사한다. 기본 75개와 Godot 소스 통합 회귀 8개 통과. StopIteration 메시지의 기존 차이와 CPython 저장소 재할당 callback의 불안정한 관찰 결과는 별도 기록했다.

[34차 결과](reports/2026-10-10/bytearray_slice_reentry_analysis.md): bytearray의 RHS별 slice 재평가와 callback 후 경계 조정을 맞추고 불변 bytes 복사를 제거했다. 기본 74개 및 Godot 소스 통합 회귀 7개 통과. native 큰 bytes RHS 대입은 약 32% 빨라지고 측정 구간 할당은 0B였다. 삭제 혼합 측정의 변동은 별도 결과와 함께 기록했다.

[33차 결과](reports/2026-10-10/bytearray_iterable_analysis.md): bytearray iterable 변환에서 정상 StopIteration을 처리했다. 기본 72개 검사와 Godot 소스 통합 회귀 6개 통과. 다음은 CPython bytearray의 RHS 변환 후 slice callback 재평가와 대상 변경 순서이며 별도 실패 fixture를 보존했다.

[32차 결과](reports/2026-10-10/sequence_large_step_analysis.md): 저장된 시퀀스 다섯 타입의 큰 step을 포화 변환해 CPython과 결과를 맞췄다. 기본 70개 검사와 Godot 소스 통합 회귀 5개 통과. 일반 step의 native 할당은 동일하며 속도 향상은 주장하지 않는다. range/view의 큰 step 표현과 bytearray 일반 iterable 대입의 StopIteration 오류는 별도 회귀로 남겼다.

[31차 결과](reports/2026-10-10/range_slice_analysis.md): range slice의 stop을 CPython과 동일하게 매핑하고 경계 callback 중복을 제거했다. 기본 69개 검사와 Godot 소스 통합의 선별 회귀 4개가 통과했다. 큰 step의 int 변환과 range/view의 정수 표현은 별도 미해결 항목으로 유지한다.

일반 .NET 실행과 Godot .NET 실행을 모두 지원한다. Godot에서는 **SharpPy 소스를 프로젝트에 직접 포함해 Godot의 빌드 구성으로 컴파일·빌드하고 실행**하는 것을 호환성 검증 기준으로 삼는다. 사용자의 Android·iPhone 배포 경험상 소스 통합은 동작하지만 SharpPy를 별도 DLL로 분리하면 오류가 발생하므로 DLL 분리 방식은 제외한다. 모바일 실기기 검증을 추가 완료 조건으로 요구하지 않는다. 새 API·패키지·native 의존성·동적 코드 생성도 이 소스 통합 경로를 유지해야 한다.

수정된 Python 라이브러리, 전용 C# 모듈, pass/stub 등 미지원 범위를 구분한다. CPython 파일로 일괄 교체하지 않고 기존 수정 이유와 호출 경로를 확인한다. 지원하는 기능의 결과는 CPython 3.12와 비교하고 stub의 존재를 호환성 통과로 취급하지 않는다.

[30차 결과](reports/2026-10-10/slice_compaction_analysis.md): 삭제 구간 압축을 적용하고 기본 68개 검사에서 새 실패가 없었다. 로컬 Godot 4.7.2/.NET 8.0.31의 소스 직접 포함 빌드와 3개 회귀가 통과했다. 전체 라이브러리 import는 미검증이다. 후속 검증도 Godot 소스 통합 빌드·실행을 기준으로 한다.

작성일: 2026-10-09. 상태: 1·2차 개선 커밋 후 3차 scope·descriptor 개선 수행. [1차 결과](reports/2026-10-09/analysis.md), [2차 구조 비교·할당 개선](reports/2026-10-09/structural_analysis.md), [3차 scope·descriptor 개선](reports/2026-10-09/scope_and_descriptor_analysis.md)에 실행 근거와 다음 후보를 기록했다. 아래 계획은 작업 기준이며 최초 미확정 항목은 마지막 절에서 현재 상태를 구분한다.

## 목적과 원칙

기본 라이브러리가 동작하는 기존 구현을 바탕으로 CPython 3.12 호환성을 평가하고, 실제 병목에 근거해 성능을 개선한다. 정확성, 구현의 깊이, 성능을 별도로 보고한다. README의 완전 구현 주장이나 메서드 수만으로 완성도를 판단하지 않는다.

.NET 자체가 느리다는 가정을 출발점으로 삼지 않는다. VM 명령 처리, 객체 할당, GC, 속성 조회, 함수 호출 등의 비용을 측정한다. C의 구현 패턴을 옮길 때도 Python 실행 의미를 유지하면서 C# 런타임에서 효과가 있는지 확인한다.

기존 문서는 [보관 안내](archive/2026-10-09/INDEX.md)를 통해 참고한다. 과거 계획의 우선순위와 성능 수치는 재검증 전까지 현재 근거로 사용하지 않는다.

## 1. CPython 기준과 variation 관리

비교 실행 기준은 전용 conda 환경 `sharppy-test-py312`의 CPython 3.12.15다. 초기 SharpPy 구현이 참고한 패치 버전은 미확정이다. 비교 실행 버전과 구현 참조 버전을 구분한다.

공식 CPython 저장소의 고정 태그를 사용한다. 초기 참조 조사에는 `v3.12.0`, 현재 실행 비교에는 `v3.12.15`를 후보로 두고, 차이가 있는 핵심 파일의 이력을 통해 필요한 중간 태그를 선별한다. 움직이는 `3.12` 브랜치는 고정된 기준으로 사용하지 않는다.

다운로드한 소스마다 공식 출처 URL, 태그, 실제 커밋, 다운로드 일자와 파일 해시를 기록한다. 참조 소스는 프로젝트 코드와 분리해 보관하고, conda 빌드의 패치와 빌드 설정도 필요시 조사한다. 최초 계획 수립 후 두 태그의 핵심 소스를 다운로드했으며 출처는 1차 보고서에 기록했다.

우선 대조할 영역은 다음과 같다.

- `Grammar/python.gram`, `Parser/Python.asdl`, PEG 관련 소스.
- opcode 정의, `Python/bytecodes.c` 및 생성된 명령 처리 코드.
- `Python/compile.c`, `Python/flowgraph.c`, `Python/assemble.c`.
- `Python/specialize.c`, 객체·descriptor·함수 호출 구현.
- `Lib/test`에서 선별한 기능 테스트.

줄 번호·파일 크기는 출처 추정의 단서다. 판정은 함수, 규칙, 상수 및 실제 동작에 근거한다. 차이는 패치 버전 차이, 플랫폼/배포 빌드 차이, C# 내부 표현 차이, 구현 오류, 출처 불명 혼입으로 분류한다. 서로 다른 버전의 정의를 근거 없이 섞지 않는다.

각 차이의 기록에는 SharpPy 위치, CPython 태그/커밋/함수, 최소 사례, 두 엔진 결과, 분류, 적용 판단과 회귀 테스트를 포함한다.

공식 참고:

- [CPython 저장소](https://github.com/python/cpython)
- [CPython v3.12.0](https://github.com/python/cpython/tree/v3.12.0)
- [CPython v3.12.15](https://github.com/python/cpython/tree/v3.12.15)
- [3.12 dis 문서](https://docs.python.org/3.12/library/dis.html)

## 2. 구현 수준 평가

| 영역 | 확인할 내용 |
|---|---|
| 문법 | 정상 문법, 잘못된 문법 거부, 오류 위치 |
| 실행 의미 | 반환값, 타입, 부작용, 평가 순서 |
| 객체 모델 | MRO, descriptor, 특수 메서드, 동적 변경, 서브클래스 |
| 제어 흐름 | 예외, finally, context manager, generator, async |
| 표준 라이브러리 | import, 대표 동작, 경계 조건, 모듈 간 조합 |
| 컴파일과 바이트코드 | opcode, 인자, 점프, 스택 효과, 예외 테이블, 중첩 코드 객체 |
| C# 내장 실행 | 호출, generator 조작, 상태 재사용, 실행 간 간섭 |

기능별 상태는 미검증, 대표 사례 통과, 경계·오류 사례 통과, 조합·회귀 검증 완료로 표시한다. 전체 호환성 비율을 제시한다면 모집단과 분모를 명시하고 미실행·지원 제외 항목도 함께 공개한다.

기존 `tests/level*.py`, 루트 종합 테스트, C# 호출 테스트를 목록화한다. 출력 확인형과 assertion 검증형을 구분한다. 실패를 잡아 출력하는 테스트는 종료 코드만으로 통과 판정하지 않는다. 실패 집계 누락, 예외 삼키기, 예상값 없는 사례도 확인한다.

CPython `Lib/test`는 관련 기능부터 선별한다. 테스트 지원 모듈·C 확장·플랫폼 의존성으로 실행할 수 없는 사례는 구현 실패와 구분한다. 기대 실패에는 이유와 추적 항목을 붙이고 예상 밖 통과도 보고한다.

## 3. 차등 테스트 체계

동일한 소스와 입력을 두 엔진에서 실행하고 출력, 반환값과 타입, 예외 타입, 의미 있는 메시지, 상태 변화를 비교한다. 무작위 시드, 작업 디렉터리, 인코딩, 환경 변수와 외부 입력을 고정한다. 각 테스트는 타임아웃을 두고 가능한 한 프로세스로 격리한다. 공유 VM/전역 상태를 검증할 때는 별도의 재사용 테스트를 수행한다.

정규화는 절대 경로, 객체 주소, 시간 등 변동 요소에 한정하고 항목별로 기록한다. 순서나 예외 차이를 광범위하게 지우지 않는다. 실패는 최소 재현 코드로 축소해 회귀 테스트로 보관한다.

바이트코드는 워밍업 전 일반 코드와 실행 중 특수화 코드를 구분한다. 우선 `adaptive=False`, `show_caches=True` 기준으로 비교한다. 텍스트 dis 출력뿐 아니라 opcode·인자·상수·이름·중첩 코드·점프 대상·예외 테이블을 구조적으로 비교한다. 내보낸 바이트코드의 정확한 일치와 내부 IR의 의미 일치를 별도 보고한다. CACHE, EXTENDED_ARG, 인덱스/바이트 오프셋 변환 때문에 차이를 숨기지 않도록 원본도 보존한다.

최적화 on/off 비교는 실제 제어 경로가 있는지 먼저 확인한다. 비교가 불가능하면 가능하다고 보고하지 않고 관찰 가능한 실행 결과와 컴파일 구조를 검증한다.

결과 상태는 통과, 동작 불일치, SharpPy 실패, 타임아웃, 테스트 의존성 부족, 명시적 지원 제외, 테스트 자체 오류로 구분한다.

## 4. 성능 측정

| 구간 | 측정 범위 |
|---|---|
| 새 프로세스 파일 실행 | 프로세스 시작, 초기화, 컴파일, 실행을 포함한 전체 비용 |
| 소스 처리 단계 | 토큰화, 파싱, AST 처리, 컴파일, CFG 최적화, 어셈블 각각 |
| 컴파일된 코드 반복 실행 | 컴파일 비용을 제외한 VM과 객체 모델 |

Release로 한 번 빌드한 산출물을 사용한다. 빌드·복원·코드 생성 시간은 별도로 기록한다. Release에서도 출력 로그가 남는지 확인하고 벤치마크 본문에서는 출력하지 않는다. 측정 전 정답을 검증한다.

동일한 입력과 작업량으로 비교한다. 너무 짧은 작업은 공통 반복량을 늘리거나 묶어서 측정한다. 서로 다른 반복량을 사용한 처리량은 보조 자료로만 취급한다. 실행 제한과 VM singleton/캐시의 영향을 사전 확인한다.

.NET JIT/계층화와 CPython 특수화의 워밍업을 고려한다. 최초 실행과 충분히 워밍업된 실행을 별도로 기록한다. 외부 측정은 고해상도 단조 시계, 단계별 C# 측정은 Stopwatch를 사용한다. Python 내부 타이머는 구현 정확성과 측정 오버헤드를 확인한 후 보조적으로 사용한다.

초기 설정은 워밍업 후 10회 이상 측정, 독립 프로세스 3회 이상 반복으로 시작한다. 중앙값, 사분위 범위, 표본 수와 원시 결과를 저장한다. 안정성이 부족하면 측정 시간을 늘린다. 두 엔진의 실행 순서를 교차하고 전원 설정·백그라운드 작업·열 상태를 기록한다.

시간 외에 할당 바이트, GC 세대별 횟수, 최대 메모리, CPU 프로파일을 수집한다. 프로파일링 실행과 최종 시간 측정은 분리한다. 스레드별 할당량은 전체 프로세스 할당량과 구분한다.

작은 벤치마크는 산술, 참/거짓 판정, 지역/전역/클로저 조회, 속성 읽기/쓰기, 함수/메서드/키워드 호출, list/dict, comprehension, generator, 예외 경로로 나눈다. 안정된 타입과 타입/클래스가 바뀌는 사례를 모두 포함한다.

실제 작업은 표준 라이브러리 조합, 데이터 변환, 짧은 게임 로직 업데이트로 구성한다. 대화형 Progress Quest 예제는 입력·sleep·무작위 요소를 통제한 작업으로 추출해야 한다.

결과에는 SharpPy 커밋, dirty 여부, 빌드 설정, .NET SDK/런타임, CPython 버전/배포, OS/CPU, 입력 크기, 워밍업, 측정 범위, 정답, 원시 표본을 기록한다. 시작 비용·컴파일·정상 상태 실행을 하나의 배수로 합치지 않는다.

## 5. 개선 후보 선별과 승인 기준

후보마다 재현 작업, CPU/할당 비중, 호출 빈도, 예상 개선 원리, 회귀 위험, 구현 비용, 검증 방법을 기록한다. 큰 병목이며 여러 작업에 영향을 주고 의미 변화 위험이 작은 후보를 먼저 선택한다.

관찰 대상은 VM 디스패치, 임시 객체/컨테이너, 변수 조회, MRO/descriptor 조회, 인자 바인딩, 반복자, 캐시, 컴파일 단계다. 아직 병목으로 확정하지 않았다.

`runtime/AdaptiveSpecializer.cs`는 skeleton이며 기본 비활성화다. 클래스나 특수화 opcode의 존재만으로 최적화 구현 완료를 판정하지 않는다. 실행 경로와 실제 효과를 확인한다.

캐시와 타입별 빠른 경로는 descriptor 우선순위, 클래스/인스턴스 변경, 전역 재할당, 서브클래스, 타입 변경 시 가정 무효화와 일반 경로 복귀를 검증한다. 객체 풀링은 객체 생존 기간과 참조 누출을 검증한다.

변경 채택 조건:

1. 대상 작업의 정답이 동일하고 관련 회귀 테스트가 통과한다.
2. 반복 측정에서 기존 변동 폭을 넘어서는 효과가 재현된다.
3. 다른 대표 작업의 시간·할당·메모리 퇴행을 확인하고 허용 여부를 설명한다.
4. 구현 복잡도와 유지 비용을 설명할 수 있다.

측정 전 임의의 목표 배수나 모든 작업에 동일한 개선율을 약속하지 않는다. 기준선 확보 후 작업별 목표와 허용 퇴행을 정한다.

## 6. 실행 단계와 완료 조건

| 단계 | 산출물 | 완료 조건 |
|---|---|---|
| 0. 환경과 출처 | 도구/런타임 정보, CPython 소스 출처, 재현 명령 | Release 빌드와 두 엔진 간단 실행 확인, 참조 불명확성 기록 |
| 1. 기존 구현 평가 | 테스트 목록, 기능별 결과, 실패 최소 사례 | 실패·미검증·지원 제외가 구분된 기준선 |
| 2. 비교 체계 | 차등 실행 도구, 선별 CPython 테스트, 바이트코드 비교 | 자동 집계와 재현 가능한 결과 |
| 3. 성능 기준선 | 구간별 벤치마크, 원시 표본, 환경 기록 | 정답 확인, 변동 폭과 측정 범위 공개 |
| 4. 병목 선별 | CPU/할당 프로파일, 상위 개선 후보 | 각 후보에 근거·위험·검증 방법 존재 |
| 5. 첫 개선 | 작은 변경 한 건, 회귀와 전후 측정 | 채택 조건 충족 또는 효과 부족으로 보류 |

각 단계의 결과는 계획과 별도의 보고서에 기록한다. 이후 단계는 앞선 결과에 따라 수정하며, 현재 문서에 아직 실행하지 않은 일을 완료로 표시하지 않는다.

## 현재 확인과 미확정 사항

- 추가 확인: [29차 slice 대입·삭제](reports/2026-10-09/slice_mutation_analysis.md)는 int 범위 step의 overflow, list 삭제의 콜백 중복, bytearray 빈 값 대입의 삭제 동작을 수정했다. 기본 67개에서 새 실패 없음. 고정 JIT의 큰 bytearray 측정에서 대입 약 49~56%, 삭제 약 25~28% 단축, 할당 각각 약 88%/79% 감소. 큰 step 변환과 선형 삭제 알고리즘은 다음 검토 대상으로 남는다.

- 추가 확인: [28차 slice 읽기](reports/2026-10-09/sequence_slice_read_analysis.md)는 5개 타입의 int 범위 step 증가식 overflow를 수정했다. bytes/str/list 및 연속 tuple의 중간 버퍼 비용도 줄였다. 기본 66개에서 새 실패 없음. JIT 전환 의심 변동 때문에 고정 JIT 조건의 측정을 별도 기록했으며 전체 VM 개선율로 해석하지 않는다. 큰 step 변환과 쓰기·삭제 루프는 다음 대상으로 남는다.

- 추가 확인: [27차 list 확장 slice 대입](reports/2026-10-09/list_slice_assignment_analysis.md)은 길이 계산 overflow, __index__ 중복 호출, 자기 대입 원본 손상을 수정했다. 반복 Items 복사와 인덱스 배열을 제거해 별도 list 대입 native 측정에서 약 67.7%/93.4% 단축, 추가 할당 0바이트. 기본 64개에서 새 실패 없음. 큰 step 변환과 다른 읽기 루프의 overflow는 별도 실패로 남아 다음 대상이다.

- 추가 확인: [26차 bytes 비교](reports/2026-10-09/bytes_comparison_analysis.md)는 내용 비교·Python hash 연결, 사용자 NotImplemented 전달과 VM 동등성 fallback을 수정했다. bytes slice 동등성 실패를 해소해 기본 63개에서 새 실패 없음. 기존 CLR Equals 교체는 빠른 불일치 경로의 퇴행 때문에 제외했고, 새 Python 비교는 span을 사용한다. 비교 루프 추가 할당 0바이트이며 잘못된 기존 Python 결과 대비 속도 개선율은 제시하지 않는다. 다음은 큰 step/stride 산술이다.

- 추가 확인: [25차 큰 slice 경계](reports/2026-10-09/slice_bounds_analysis.md)는 start/stop 범위 초과를 정규화해 6개 시퀀스 타입의 결과를 CPython과 맞췄다. 기본 61개에서 새 실패 없음. 일반 slice 경로의 시간 분포·할당 유지, 큰 경계값의 CLR 예외 처리 비용은 남는다. 큰 step과 bytes slice 동등성은 별도 실패 fixture로 보존했고 bytes 비교 경로를 다음 우선 확인 대상으로 둔다.

- 추가 확인: [24차 memoryview 슬라이스 대입](reports/2026-10-09/memoryview_slice_analysis.md)은 겹침·역방향 복사와 콜백 중 해제 순서를 CPython에 맞췄다. 기본 60개에서 새 실패 없음. CPU 경합 중 측정은 제외했고, 최종 기존 경로의 시간 분포는 겹치며 할당은 동일했다. 연속 memoryview 대입은 native 루프 내 추가 할당 0바이트. 다음 후보는 큰 slice 정수의 포화 변환과 stride 범위이며 자동 해제는 미해결이다.

- 추가 확인: [23차 memoryview index](reports/2026-10-09/memoryview_index_analysis.md)는 bool/int subclass 및 type-only __index__, 예외 전파와 재진입 시 해제 순서를 보완했다. 기본 59개에서 새 실패 없음. native 바이트 쓰기 약 34.8% 단축, 할당 동일. 자동 해제는 VM 전반의 참조 소유권 기반이 없어 국소 패치로 해결하지 않았으며 구조 과제로 유지한다.

- 추가 확인: [22차 memoryview 명시적 해제](reports/2026-10-09/memoryview_release_analysis.md)는 release/with 종료와 자식 view의 resize 보호를 CPython과 맞췄다. 기본 58개에서 새 실패 없음. native slice/읽기 중앙값 약 39.4%/20.2% 감소, 할당 동일. `del view` 자동 해제는 별도 재현에서 아직 실패하며 다음 우선 조사 대상으로 지정한다.

- 추가 확인: [21차 기존 출력 차이 분석](reports/2026-10-09/output_difference_analysis.md)에서 bound method 속성, memoryview 원본 공유, 불필요한 type 생성 로그를 수정했다. set은 동등성 assertion과 정렬 출력으로 검사한다. 전체 57개: raw 일치 52, 시간·주소 필드만 다른 3, native 2. 시간·주소 차이는 별도 상태로 표시하며 나머지 output_difference는 evaluator 실패로 처리한다. memoryview 수명/format 전체 호환성과 별도 compatibility fixture들은 미해결이다.

- 추가 확인: [20차 dict subclass copy](reports/2026-10-09/dict_copy_subclass_analysis.md)는 기본 iteration의 저장소 복사와 iteration 재정의 시 mapping 복사를 CPython 소스·실행 결과에 맞췄다. 전체 55개에서 새 실패 없음. 일반 copy workload 할당 동일, 시간 분포는 겹치므로 추가 속도 개선은 주장하지 않는다.

- 판단 기준: CPython 3.12의 관찰 가능한 결과를 유지하고 C#에 적합한 구현으로 측정한다. 자체 동작 정책이나 C 구조의 직역을 최적화로 인정하지 않는다. 결과 차이가 남거나 비용이 증가한 기존 변경도 재검토한다. module snapshot의 메서드별 보정은 늘리지 않는다.

- 추가 확인: [19차 일반 dict copy](reports/2026-10-09/dict_copy_analysis.md)는 저장된 해시·참조와 삽입 순서를 복사해 재해시 및 반복 확장을 제거했다. Python copy workload 시간 약 46.0%, 할당 약 66.0% 감소. 전체 54개에서 새 실패 없음. dict subclass copy의 기존 실패를 별도 보존했다. module 이중 저장소와 임의 cursor 압축 정책은 여전히 교체 검토 대상이다.

- 추가 확인: [18차 module observation 분석](reports/2026-10-09/module_mapping_observation_analysis.md)에서 길이·진리값·copy의 최신 binding 관찰을 보완했다. wrapper 생성당 8B와 copy의 key 객체 할당이 추가돼 호환성 개선으로 평가한다. view iteration, copy의 삭제/재삽입 및 mixed-key 순서는 아직 불일치이며 실패 fixture를 유지한다.

- 추가 확인: [17차 module live 조회 분석](reports/2026-10-09/module_mapping_live_read_analysis.md)에서 string key GetItem/Get/Contains 및 builtin storage read를 원본 namespace에 연결했다. 기존 module_locals_live가 통과한다. native 조회 중앙값은 약 59.2% 감소했지만 VM 개선은 미확정이며 Length/Copy/view iteration과 raw storage 관찰은 아직 통합되지 않았다.

- 추가 확인: [16차 module mutation 분석](reports/2026-10-09/module_mapping_mutation_analysis.md)에서 pop/del/clear/setdefault/update/|=의 원본 반영과 stale 전체 replay를 수정했다. 실제 Python namespace_update workload의 중앙값은 약 16.2% 감소했다. 일반 live 조회와 popitem은 아직 실패하며 저장소 통합 후보로 유지한다.

- 추가 확인: [15차 cursor 일반 경로 분석](reports/2026-10-09/dict_cursor_fast_path_analysis.md)에서 이력 없는 순회를 분리했다. native keys 순회 중앙값 약 10.4% 감소를 확인했으나 Python 전체 개선은 미확정이다. fresh dict별 첫 cursor 할당(120B/192B)을 별도 측정했고 그대로 남는다. class workload의 실행 순서 의존성과 일부 할당 변동도 보존했다.

- 추가 확인: [14차 dict cursor 분석](reports/2026-10-09/dict_cursor_analysis.md)에서 작은 same-size mutation과 복수 iterator를 CPython과 비교하고 이력 상한을 추가했다. dict 생성당 8B 및 native 순회 시간 증가가 있어 호환성 개선으로 평가한다. CPython resize/compaction 결과는 아직 다르며 namespace 저장소 채택 전 cursor 비용과 first-use/삭제 이력 비용을 검토한다.

- 추가 확인: [13차 dict iterator 분석](reports/2026-10-09/dict_iterator_analysis.md)에서 snapshot을 제거하고 live 값 조회·size mutation 감지·sticky exhaustion을 actual CPython과 비교했다. native 전체 순회에서도 할당 감소를 확인했다. same-size 삭제/재삽입은 List enumerator와 CPython entry cursor의 차이로 미해결이며 별도 실패 fixture를 유지한다.

- 추가 확인: [12차 dict view 분석](reports/2026-10-09/dict_views_analysis.md)에서 Python keys/values/items를 retained view로 바꾸고 내부 snapshot API는 유지했다. native keys 생성 probe의 할당이 약 97.7% 감소했다. iterator mutation, set 연산, namespace stale 저장소는 아직 미해결이며 실제 CPython 비교 실패 fixture로 구분했다.

- 추가 확인: [11차 dict 저장소 분석](reports/2026-10-09/dict_storage_analysis.md)에서 동등 키 pop의 순서 목록 정합성을 수정하고 comparer를 공유했다. native probe에서 생성당 24B 할당 감소를 확인했으며 속도 개선은 미확정이다. namespace live 조회·삭제와 일반 dict retained view를 별도 실패 fixture로 확보했다.

- 추가 확인: [10차 LookupError 분석](reports/2026-10-09/lookup_error_analysis.md)에서 누락된 builtins 등록을 보완했다. 실제 CPython 3.12와 생성·상속·예외 포착을 비교했고 Release 전체 평가의 기존 결과를 유지했다. 다음 구조적 후보는 module namespace 저장소 통합이며 wrapper의 일부 조회만 수정하는 방식은 views/mutation 정합성을 해결하지 못한다.

- 확인: 전용 CPython 3.12.15 환경 생성, 표준 모듈 import 및 간단 계산 실행.
- 확인: .NET SDK 10.0.401 설치, 프로젝트 대상은 net8.0.
- 확인: 기존 벤치마크의 반복량 차이, 측정 방식 보강 필요, 일부 테스트의 출력 기반 실패 집계.
- 추가 확인: 사용자 전용 SDK 8.0.425 / 런타임 8.0.31 설치, Release 빌드 성공, 기존 테스트 파일 단위 평가와 7개 작은 작업의 성능 기준선. 전체 기능별 통과 범위와 실제 애플리케이션 성능은 미확정.
- 미확정: 초기 참고 CPython 패치 버전, variation의 출처와 적용 타당성.
- 추가 확인: Release 디버그 준비 비용과 이항 연산 fallback 할당을 개선하고 회귀·전후 측정을 수행했다. 남은 속성 조회 비용이 프로파일 상위에 있다. 실제 작업별 목표와 최적화 제어 경로의 전체 검증은 미확정.
- 추가 확인: 2차에서 type-only in-place 조회와 명령 처리 진입점의 캡처 분리를 적용했다. 선별한 IL에서는 직접적인 CLR boxing이 없었으며, PyInt 객체화와 실행 보조 객체의 할당을 구분했다. 일반 값 표현 재설계는 아직 prototype 단계에도 착수하지 않았다.
- 추가 확인: 3차에서 scope 이름 추측으로 인한 전역 변수 오염을 수정하고, 반환 시 내부 접두사 비교와 인덱스 descriptor의 배열 복사를 개선했다. 다음 조사 대상은 호출별 frame/scope/인자 할당이다.
- 추가 확인: [4차 프레임 할당 분석](reports/2026-10-09/frame_allocation_analysis.md)에서 빈 배열과 legacy 예외 스택, Release 로그 준비 비용을 줄였다. 함수 호출 작업의 할당량은 약 16% 감소했고 시간 개선은 확정하지 않았다. 다음 조사 대상은 fast locals와 scope dictionary의 중복 저장이며, 변경 전 locals 관찰·변경과 closure/class body/generator 동작을 검증한다.
- 추가 확인: [5차 locals 관찰 분석](reports/2026-10-09/locals_observation_analysis.md)에서 CPython 3.12.15와 회귀 비교로 stale locals와 captured local 삭제 오류를 수정했다. 관찰용 dictionary는 lazy 생성하지만 frame당 참조 필드 8B가 추가됐다. 매개변수 중복 저장은 유지하며 다음 조사 대상은 scope 읽기 경로와 class/module/hidden locals 동작이다.
- 추가 확인: [6차 lazy scope 분석](reports/2026-10-09/lazy_scope_analysis.md)에서 최적화 함수의 매개변수 scope 복사를 제거하고 C# dictionary 조회 시 fast locals/cells를 갱신한다. 기본 eval/exec도 같은 관찰 경로를 사용한다. 함수 호출 작업 할당량은 5차 대비 약 20.5% 감소했으며 시간 개선은 미확정이다. 다음 후보는 관찰 경로 workload, class/module mapping의 identity·mutation과 hidden locals 회귀다.
- 추가 확인: [7차 이름 키 재사용](reports/2026-10-09/locals_key_cache_analysis.md)에서 locals/frame 관찰 workload를 추가하고 code object의 이름 키를 재사용했다. 관찰 할당량은 각각 약 35.3%/45.7% 감소했으며 시간 개선은 미확정이다. 보관한 module locals의 stale 읽기와 class locals mapping 차이를 별도 회귀 fixture로 확인했다. 다음 후보는 class namespace의 실제 mapping 노출과 module wrapper의 생성·삭제·views 동기화다.
- 추가 확인: [8차 class namespace 분석](reports/2026-10-09/class_namespace_analysis.md)에서 실제 mapping 노출, 삭제·fallback, dict subclass 입력, 실패한 호출 상태 정리를 수정했다. class closure opcode 쌍과 일부 metadata를 실제 CPython 3.12.15와 비교했다. class locals 관찰 할당량은 약 66.9% 감소했다. 다음 후보는 class body Exception Table 전달과 module wrapper의 live namespace 동기화다.
- 추가 확인: [9차 Exception Table 분석](reports/2026-10-09/class_exception_table_analysis.md)에서 class table 전달, 중첩 컴파일 상태 복원 및 최종 word 배열의 cache 중복 계산을 수정했다. 실제 CPP metadata의 table 유무·보호 opcode·offset 범위를 비교했다. 예외 workload의 할당량은 동일하고 시간 개선은 변동 때문에 미확정이다. 다음 후보는 module live namespace 및 LookupError/기존 class free-variable 호환성이다.
