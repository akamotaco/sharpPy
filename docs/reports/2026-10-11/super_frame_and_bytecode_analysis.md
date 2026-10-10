# 62차: super frame 조회와 CPython bytecode 비교

61차를 `a5c5fb2` (`fix: derive implicit class references from super name usage`)로 커밋했다. 이번 변경은 미커밋으로 남긴다.

## super 수정

CPython 3.12.15 및 고정 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/typeobject.c: super_init_without_args`와 비교했다.

SharpPy의 Code.ClassCellIndex는 cellvars 다음 freevars 순서의 인덱스인데, 실행 frame의 Cells는 freevars 다음 cellvars 순서다. 기존 CallSuper가 이 둘을 혼용해 self도 cell인 경우 class cell 대신 다른 cell을 읽었다. 첫 인자 역시 capture된 경우에는 LocalsPlus의 오래된 값이 아닌 cell 값을 읽어야 한다.

기존 name-index map으로 free class cell과 첫 매개변수의 cell을 각각 찾게 했다. 배열을 새로 만들거나 매 호출마다 이름 목록을 선형 검색하지 않는다. CPython처럼 인자 유무, 첫 인자 삭제, class free cell 누락·비정상·비어 있음·타입이 아닌 값의 순서로 검사한다. 로컬 class cell은 defining class로 사용하지 않는다. SharpPy ArgCount는 keyword-only를 포함하므로 위치 인자 수 확인 시 이를 제외했다.

## 검증 및 성능

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개.
- 기본 118개: 완전 일치 113개, 지정된 변동값 제외 일치 3개, native 검사 2개 통과.
- 신규 super frame 회귀와 기존 captured self fixture는 이전 DLL에서 실패하고 수정 후 통과한다.
- 첫 인자 캡처·재대입·삭제, 인자 없는 함수와 keyword-only 함수, class cell 누락·삭제·정수가 된 경우를 실제 CPython과 비교했다.
- Godot 소스 직접 포함 빌드 및 46개 회귀, native range helper 통과. 외부 SharpPy DLL 참조와 모바일 실기 검사는 없다.
- `git diff --check` 통과.

.NET 8.0.31, TieredCompilation=0, workload 1만 회, warmup 10회와 측정 10회, 전후 교대 3쌍. 모든 빌드와 회귀 종료 후 측정했고 결과 합계도 확인했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전/이후 할당 B |
| --- | ---: | ---: | ---: |
| super alias 생성·속성 조회·호출 | 28.174 | 28.320 | 18,482,584 |
| class cell을 가진 metaclass 바인딩·호출 | 17.786 | 17.935 | 9,041,336 |

할당은 동일하다. 시간은 각각 약 0.52%, 0.84% 증가했지만 전후 프로세스 범위가 겹친다. 유의한 성능 변화로 주장하지 않는다. 오류 경로까지 포함한 이번 목표는 결과 호환성이다. 로그는 `.artifacts/round62-full.json`, `round62-before-targeted.json`, `round62-godot-*.log`이며 측정 원본은 [round62-validation.json](round62-validation.json)에 있다.

## bytecode는 CPython과 얼마나 같은가

전체를 하나의 유사도 백분율로 나타낼 근거는 없다. 아래는 **실제로 확인한 층별 결과**다.

| 비교 대상 | 확인 결과 |
| --- | --- |
| opcode 선언 | CPython 3.12.15 `dis.opmap`의 256 미만 129개 이름·번호 모두 SharpPy enum에 일치. 실행 구현의 완성도 검사는 아니다. |
| 추가 opcode | SharpPy는 UNARY_POSITIVE=13을 별도로 사용한다. 300번대 자체 특수화 명령도 있으며 CPython의 quickened opcode 전체 매핑과 같다고 볼 수 없다. |
| BINARY_OP 인자 | 26개 중 12개 불일치. ADD와 13개 inplace 값은 같지만 나머지 일반 연산의 순서가 다르다. 예: FLOOR_DIVIDE는 CPython 2, SharpPy 4. |
| 위치 표현 | 논리적으로 2바이트 word 기준 offset, CACHE·EXTENDED_ARG 개별 word를 사용한다. 실제 메모리 저장은 메타데이터를 포함한 C# ByteCodeInstruction 배열이다. |
| 컴파일 구조 | 심볼 분석, CFG, assembly, 스택 VM 구조를 공유하지만 상수 배치, 상수 접기, 불필요 명령 제거, 예외·generator 명령 배치가 다르다. |
| 실행 규칙 | 예를 들어 SharpPy는 FOR_ITER 종료 시 iterator를 제거하고 END_FOR는 stack을 변경하지 않는다. 같은 opcode 번호가 모든 stack 동작의 동일함을 뜻하지 않는다. |
| adaptive specialization | 코드 골격은 있지만 AdaptiveSpecializer.Enabled 기본값은 false이고 구현에 TODO가 남아 있다. CPython의 활성 adaptive VM과 같다고 볼 수 없다. |

BINARY_OP의 기존 'CPython과 100% 같은 순서' 주석은 사실과 달라 수정했다. 이번에는 번호 자체를 바꾸지 않았다. 컴파일러와 VM은 현재 같은 enum을 사용하므로 이 번호 차이가 곧 Python 연산 결과 오류라는 뜻은 아니다. 다만 CPython bytecode 직접 실행 및 그대로의 disassembly 비교는 불가능하다.

## 실제 명령열 표본

`tools/fixtures/bytecode_comparison.py`를 양쪽에서 컴파일했다. CACHE를 포함하고 adaptive 변환 전 명령을 비교한다. 단순히 소스 결과만 실행해 비교한 것이 아니다.

| 함수 | CPython word 수 | SharpPy word 수 | 차이 |
| --- | ---: | ---: | --- |
| add | 6 | 6 | opcode·인자 일치 |
| select | 5 | 7 | SharpPy에 LOAD_CONST 뒤 RETURN_CONST가 남음. 상수 인덱스도 다름. |
| loop | 26 | 26 | 상수 인덱스만 다름. 상수를 값으로 정규화하면 명령열 일치. |
| protected | 22 | 29 | // 인자 번호, 음수 상수 생성, exception cleanup과 jump 배치 차이 |
| delegated | 27 | 29 | SharpPy에 종료 뒤 LOAD_CONST/RETURN_VALUE가 추가되고 cleanup jump 거리도 다름 |

모듈 본문도 17 word 대 18 word로 달랐다. 작은 5개 함수 표본을 전체 프로그램의 유사도 비율로 일반화하지 않는다. 이 검사는 code object 전체의 constants·flags·line/exception table 또는 pyc 바이너리 호환성 판정이 아니다.

재실행 도구는 `tools/compare_bytecode.py --dotnet <dotnet.exe> --output <report.json>`와 harness의 `--bytecode-inspect`다. opcode 선언 대조, BINARY_OP 매핑, 원본 명령열, 상수 테이블 및 상수 번호 정규화 결과를 [round62-bytecode.json](round62-bytecode.json)에 보존했다. 소스만 분석하며 샘플 프로그램을 실행하지 않는다.

## 다음 순서

상수 반환의 중복 LOAD_CONST와 generator 종료 후 남은 명령을 먼저 조사한다. 출력뿐 아니라 예외 스택과 jump offset을 유지하면서 제거할 수 있는지 검증하고 성능을 측정한다. BINARY_OP 번호 정렬은 내부·C# API 사용처를 조사한 뒤 결정하며, 번호를 맞추는 것만으로 성능이 좋아진다고 가정하지 않는다.

super의 모든 인수 형식, classcell metaclass 전달, generator throw/close 등 기존 미완성 경계도 별도 과제로 유지한다. CPython 결과를 최우선으로 하되 C의 저장 구조 자체를 복제하는 것을 목표로 삼지 않는다.
