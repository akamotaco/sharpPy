# SharpPy 구현 평가 및 성능 개선 계획

## 현재 후속 1~6번 작업 (112차부터)

평가 순서, 콜백과 예외 발생 시점, 참조 수명을 포함한 CPython 3.12의
관찰 가능한 과정과 결과를 기준으로 한다. 내부 C 자료구조 복제가 목표는 아니다.

1. [112차](reports/2026-10-12/keyword_frame_allocation.md): 108→109차 직접 비교에서
   지연 재현 안 됨. keyword 전달의 임시 프레임 제거, 대상 시간·할당 감소.
2. [113차](reports/2026-10-12/set_storage_and_copy.md): 기존 set 해시·재진입 실패 해결,
   BUILD_SET 삽입 순서 및 iterable 조기 종료 수정. 공유 복사로 시간/할당 감소.
   작은 집합 생성·변경 할당 증가와 전체 API 감사는 남음.
3. [114차](reports/2026-10-12/memoryview_ownership_boundaries.md): **미해결**.
   자동 해제 9개 소유권 경계 실패를 보존, live owner 보호 7개 확인.
   컨테이너/프레임/호스트까지 연결하는 소유권 계층이 필요하다.
4. [115차](reports/2026-10-12/integer_format_and_literals.md): BigInteger 폭/정렬/그룹,
   컴파일 decimal literal 제한 수정. 2/8/16진 변환을 선형 비트 추출로 변경.
   locale n 및 float formatter 전체 호환성은 별도 범위.
5. [116차](reports/2026-10-12/comparison_dispatch_cost.md): native 슬롯/dispatch와 VM 비용 분리.
   반복 할당 0, subclass 조회 경로 추가 비용 관측. 강제 inline 효과 없어 제외.
   VM 지연은 대조군 범위와 겹치며 과거 단일 원인은 미확정.
6. [117차](reports/2026-10-12/library_process_and_async_execution.md): warnings의
   실제 C 가속 구현 과정, live f_globals, async generator 실행, iterable join,
   Unicode repr/chr, 스크립트 경로와 Godot resource stat 수정·검증.
   외부 C# Dictionary 직접 변경은 native 관찰 8개 중 3개 미일치로 남음.

최종 기본 회귀 220개, 하위 폴더 포함 compatibility 74개 중 72개 일치·
memoryview 실패/차이 2개. Godot 소스 포함 기본 17+관련 13개와 경로 5개 통과.
각 항목은 별도 커밋/푸시 대상으로 처리했다. **3번 자동 해제는 미해결**이며
5번 과거 단일 원인과 6번 호스트 직접 변경도 완료로 분류하지 않는다.
다음은 소유권 계층, 호스트 mapping 저장소 통합, 작은 set 할당 비용 및 API 감사다.

112차 이후 상태가 아래 과거 기록보다 우선한다.

## 현재 후속 1~6번 작업 (106차부터)

1. 부분 해결: [106차](reports/2026-10-12/frozenset_stored_hashes.md).
   frozenset 저장 해시·해시 캐시와 생성자 소비 순서를 수정했다.
   mutable set 저장소 교체는 성능 문제로 제외했으며 재진입은 미해결이다.
2. [107차](reports/2026-10-12/dict_mapping_followup.md): 기존 실패 5개 해결.
   저장소 cursor, view 집합 연산, 전역 binding과 보존 view 동기화.
3. [108차](reports/2026-10-12/integer_conversion_api.md): bytes/bytearray,
   index·keyword base, 공통 __new__ 변환과 오류 재현 해결.
4. [109차](reports/2026-10-12/class_function_metadata.md): 기존 실패 3개 해결.
   일반 함수 호출의 105차 대비 약 3.4% 지연 관측은 별도 원인 조사로 남긴다.
5. 부분 해결: [110차](reports/2026-10-12/coroutine_warning_and_view_lifetime.md).
   구식 coroutine throw 경고 해결. memoryview 즉시 자동 해제는 VM 참조 수명
   설계가 필요하여 미해결이며 alias/child 보호 회귀를 추가했다.
6. [111차](reports/2026-10-12/math_isclose_and_followup_status.md): math.isclose 구현.
   500개 수치 조합과 변환 callback/오류 검증.

최종 기본 회귀 206개, Godot 기본 17+관련 21개 및 경로 5개 통과.
기존 compatibility 68개 전체 재실행은 일치 66·실패 2다. 이번 요청의 기존
실패 13개 중 11개를 해결했으며, 1번 set 재진입과 5번 memoryview 즉시 해제는
여전히 미해결이다. 각 항목의 완료/부분 해결 구분과 성능 관측은 111차 보고서를 따른다.

아래 100~105차 기록은 이전 작업의 결과이며 위 목록을 최신 상태로 사용한다.

## 후속 1~6번 작업 (100차부터)

이 목록이 아래 과거 기록보다 우선한다. 이전 6번도 `8408021`로 푸시 완료했다.

1. metaclass 속성: [100차](reports/2026-10-12/metaclass_attribute_precedence.md), 기본 회귀 173개·Godot 20개 통과. 원래 재현 해결. 중첩 클래스 qualname 생성은 별도 재현으로 보존.
2. set/frozenset 해시: [101차](reports/2026-10-12/set_hash_audit.md). 수치 hash/identity 및 복사 재해시 수정. 전체 저장소 교체는 성능 저하로 기각; 전체 폭 hash·재진입·frozen hash는 미해결 재현으로 유지.
3. 정수 변환 범위·sys 제한 API: [102차](reports/2026-10-12/integer_digit_limits.md). 동적 제한·str/repr·기본/d format 연결, 175개 회귀 통과. bytes/bytearray/base 및 나머지 format·컴파일 literal 제한은 별도 미해결.
4. 비교·예외 성능 잔여 조사: [103차](reports/2026-10-12/performance_residual_followup.md). 과거 DLL/현재 교차 재측정과 native 검사 완료. int/float 잔여 +2.73% 관측, 과거 지연 단일 원인 미확정. 런타임 추가 변경 없음.
5. 과거 미해결 기록 재분류: [104차](reports/2026-10-12/compatibility_reconciliation.md). 재현 67개 전체 재실행: 일치 55·미일치/실패 12. 미등록 일치 11개를 기본 회귀로 승격. 전체 목록과 모집단 밖 과제 분리 완료.
6. CPython 테스트 및 라이브러리 조합 검증 확대: [105차](reports/2026-10-12/cpython_selected_and_godot_libraries.md). 고정 원본 메서드 10개·조합 fixture 추가. Godot resource import 경로 수정. 기본 188개·Godot 33개 및 경로 5개 검증. math.isclose 미구현은 신규 실패 재현.

이 여섯 작업은 각각 수정·선별·조사 결과를 남긴 범위다. 모든 잔여 문제가
해결되었다는 뜻은 아니다. 현재 보존된 실패는 104차 12개와 105차 신규 1개이며,
성능 원인 미확정 및 전체 라이브러리 감사 범위 밖 항목도 계속 추적한다.

## 요청한 1~6번 작업 상태 (99차)

1~5번은 아래 개별 커밋으로 `origin/work/oserror-compatibility`에 푸시했다. 6번의 검증된 최적화와 조사 기록은 이번 커밋에 포함한다. 과거 85→86차 지연의 단일 원인은 확정하지 못했으며, 이를 해결 완료로 표시하지 않는다. 아래 상태가 과거 회차별 기록의 '다음 작업'보다 우선한다.

| 순서 | 작업 | 근거·상태 |
| --- | --- | --- |
| 1 | 문자열 반대편 비교·tuple 하위 타입·int 비교 descriptor | 수정·회귀·푸시 완료: `56b99ca`, [94차 보고서](reports/2026-10-12/native_comparison_slots.md). |
| 2 | 명시적 type.__setattr__ 바인딩 | 수정·회귀·푸시 완료: `1b83a61`, [95차 보고서](reports/2026-10-12/type_descriptor_binding.md). |
| 3 | sorted/list.sort callback·안정성·예외·mutation | 수정·회귀·푸시 완료: `8d0f495`, [96차 보고서](reports/2026-10-12/sort_protocol.md). |
| 4 | dict 전체 폭 해시·수치 키·사용자 callback·재진입 | 수정·회귀·푸시 완료: `b948ae9`, [97차 보고서](reports/2026-10-12/dictionary_hash_storage.md). |
| 5 | 큰 정수 문자열 파싱 | 수정·회귀·푸시 완료: `24c4f9b`, [98차 보고서](reports/2026-10-12/integer_string_parsing.md). |
| 6 | 비교 잔여 비용·예외 생성 비용 조사 | 중복 비교 판정·descriptor 저장 제거, 한 인자 예외 경로 분리 검증 완료. 과거 지연 원인과 int/float 비교 잔여 비용은 미확정: [99차 보고서](reports/2026-10-12/performance_audit.md). |
| 지속 | CPython 3.12 호환성 범위 확대와 구조적 최적화 후보 조사 | 전체 호환성 감사는 완료되지 않았다. 과거 미해결 기록은 후속 수정 여부를 확인한 뒤 재분류한다. |

최종 기본 회귀 171개(출력 일치 166, 지정 변동 필드 3, native CLI 검사 2), Godot 소스 포함 검증 44개, 별도 native 잘못된 연산자 8개·C# 하위 타입 재정의 12개를 통과했다. 전체 CPython 테스트 통과를 뜻하지 않는다. 소스 포함 Godot 빌드와 Windows x64에서 검증했으며 모바일 실기/32비트 실행은 하지 않았다.

후속 범위는 다음과 같다.

- `tests/compatibility/metaclass_attribute_remaining.py`: 사용자 클래스의 명시적 type.__repr__, metaclass property 우선권, type.__dict__의 native 항목 노출.
- 정수 문자열 제한은 기본 4,300자리 기준이다. sys의 제한 변경 API·다른 int 변환 API 전체는 별도 범위다.
- set/frozenset의 별도 해시 저장소는 이번 dict 변경 범위에 포함하지 않았다.
- 85→86차 지연은 새 측정에서 크기·방향이 달라 원인 미확정으로 유지한다. int 하위 타입/float 비교도 과거 비용을 완전히 회복했다고 주장하지 않는다.
- 과거 미해결 기록의 후속 수정 여부 확인과 CPython 호환성 범위 확대를 계속한다.

## 호스트·라이브러리 조건 (2026-10-10 추가)

[93차 결과](reports/2026-10-12/comparison_error_cold_path.md): 92차를 `1b8ff7c`로 로컬 커밋했다. 인라인된 비교 경로에서 오류 메시지 생성만 분리해 해당 함수 기계어 445→257 B, 스택 예약 80→32 B로 줄였고 payload 위임이 tail call로 바뀌었다. 잘못된 native enum 8개·전체 회귀 161개·Godot 소스 포함 34개 통과. 일반 빌드의 애플리케이션 제어 4551 차단은 사용자 설정 변경 후 같은 명령 재시도로 해소됐다. 할당은 동일하며 시간 범위가 겹쳐 속도 개선은 주장하지 않는다. 사용자 비교 +2.28%는 역순 재측정에서 +0.26%로 줄어 원인을 확정하지 않았다. 다음은 문자열 반대편 비교 누락이다. 93차는 `3560d62`로 커밋하고 원격 브랜치에 푸시했다.

[92차 결과](reports/2026-10-12/comparison_dispatch_inlining.md): 91차를 `c28d75f`로 로컬 커밋했다. 비교 캐시 확인과 실제 호출을 분리해 인라인하고, int 하위 타입과 기본 숫자 조합의 불필요한 계층 탐색을 생략했다. callback과 NotImplemented는 기존 경로로 처리한다. 새 보존 회귀 포함 전체 161개·Godot 소스 포함 34개 통과. 두 대상 workload 시간은 91차 대비 -5.18%/-3.28%, 같은 측정의 90차 대비 +0.62%/+0.85%다. 할당은 동일하며 int/float 조합의 작은 잔여 비용은 남는다. 일반 혼합 숫자 대조군의 +1.57%는 역순 재측정에서 -1.01%로 바뀌어 원인 확정을 보류했다. 92차는 다음 단계 시작 시 로컬 커밋했다.

[91차 결과](reports/2026-10-12/comparison_dispatch.md): 90차를 `1e4cd65`로 로컬 커밋했다. 공통 비교 판정으로 하위 타입 우선 호출·NotImplemented·임의 결과의 진리 판정과 컨테이너 비교를 맞췄다. 전체 160개·Godot 소스 포함 33개 통과. 사용자 비교는 10만 회당 7,200,000 B 감소했으나 기본 int 하위 타입 비교 시간은 +6.70%/+4.65%로 퇴행이 남는다. 최적화 완료로 보지 않으며 이 반복 판정 비용 제거가 다음 우선 대상이다. 조회 실패만 캐시하며 부모 변경과 실행 중 메서드 교체를 검증했다. 다른 native 비교 슬롯·type.__setattr__·정렬·수치 키 해시는 별도 미해결 재현으로 관리한다. 91차는 다음 단계 시작 시 커밋했다.

[90차 결과](reports/2026-10-12/integer_subclass_comparison.md): 89차 `956dd3e`를 커밋하고 `origin/work/oserror-compatibility`에 푸시했다. int 하위 타입의 Int64 축소와 중복 비교 분기를 제거하고 기본 int/float/bool의 반대 방향 비교를 연결했다. 676개 조합·전체 157개·Godot 30개 통과. 최종 네 workload의 할당은 동일하고 시간 범위는 겹친다. 초기 하위 타입/float +1.44%는 재측정에서 +0.22%가 됐지만 원인은 확정하지 않는다. 사용자 비교 재정의·하위 타입 우선 호출의 실패 fixture를 보존했으며 VM과 컨테이너의 공통 비교 판정이 다음 대상이다.
[89차 결과](reports/2026-10-11/numeric_mixed_comparison.md): 88차를 `3533981`로 커밋했다. 수치 키 조사에서 발견한 기본 int/float 비교의 정밀도 손실을 먼저 수정했다. 577개 조합의 양방향 6종 비교, 전체 155개·Godot 28개 통과. 정확한 큰 정수의 임시 BigInteger 생성을 제거해 기존 세 workload의 할당을 유지했고 시간 범위는 겹친다. int 하위 타입의 Int64 축소·반대편 비교와 동등 수치 키의 dict 해시 불일치를 실패 fixture로 보존했으며 순서대로 후속 검증한다.
[88차 결과](reports/2026-10-11/dict_key_identity.md): 87차를 `9629fe0`으로 커밋했다. dict에서 같은 키 객체의 동등 비교를 생략해 NaN 및 사용자 비교 예외의 조회·갱신·삭제를 CPython과 맞췄다. 기본 154개·Godot 27개 통과. 기존 dict 두 workload 시간 변화는 -0.78%/-0.09%이며 범위가 겹쳐 속도 개선은 주장하지 않는다. 다음은 수치 키 해시 계약과 저장된 전체 해시·키 참조 구조, 다른 객체의 비교 callback 및 재진입 검증이다.
[87차 결과](reports/2026-10-11/integer_hash_protocol.md): 86차를 `2b71fa8`로 커밋했다. 정수 hash()를 native-width Python 해시와 사용자 callback에 연결하고 errno와 경로를 공유했다. builtin wrapper 임시 바인딩 및 작은 반환 정수 생성을 제거해 두 workload에서 각각 10만 회당 400만 B 감소했다. 기본 153개·Godot 관련 26개 통과. 85→86 비용 분리에서 native 생성의 일관된 증가는 없었지만 VM 일반 생성 +2.72%가 재현돼 원인은 미확정이다. dict의 32-bit 해시·동등 비교 callback과 큰 문자열 파싱은 후속 과제로 유지한다.
[86차 결과](reports/2026-10-11/os_error_errno_subclass.md): 85차를 `121e21c`로 커밋했다. errno int 하위 타입의 hash/eq·descriptor 오류·NotImplemented·진리값·비교 키 identity를 CPython과 맞추고 int.__hash__, 암시적 hash=None, MRO 해시 조회를 보완했다. 기본 152개·Godot 관련 25개 통과. 기존 네 workload 할당은 동일하며 일반 생성·정수 하위 타입 생성에서 각각 약 +1.42% 관측의 원인은 미확정이다. 다음은 생성 비용 분리 조사와 일반 hash/dict의 기존 하위 타입 callback 누락이며 큰 문자열 파싱 실패도 유지한다.
[85차 결과](reports/2026-10-11/os_error_remaining_types.md): 84차를 `b47ab97`로 커밋했다. OSError의 남은 native 하위 타입 9개와 errno 매핑, ConnectionError 부모 catch를 연결했다. 기본 151개·Godot 관련 24개 검증. 기존 세 workload의 할당량은 동일하며 시간 변화는 +0.42%/+1.06%/-0.38%, 프로세스 범위가 겹친다. errno int 하위 타입의 hash/eq 조회 의미는 다음 단계로 유지한다.
[84차 결과](reports/2026-10-11/os_error_winerror_int_subclass.md): 83차를 `51b3bf1`으로 커밋했다. winerror int 하위 타입을 callback 없이 변환하고, 하위 타입 생성·repr의 큰 정수 축소를 제거했다. 기본 150개·Godot 관련 23개 통과. 기존 네 workload의 할당은 동일하며 시간 범위는 겹친다(winerror 중앙값 +2.4% 관측). errno 선택은 CPython의 사용자 정의 hash/eq 호출을 보존해야 하므로 별도 실패 fixture로 남겼다. 다음은 이 조회 의미이며 큰 문자열 파싱도 미해결로 추적한다.
[83차 결과](reports/2026-10-11/blocking_io_written_state.md): 82차를 `e24adae`으로 커밋했다. BlockingIOError 선택과 세 번째 인자, OSError 전체의 characters_written 설정·삭제·오류·재진입을 맞췄다. 기존 filename2 슬롯을 공유해 일반 생성 할당은 유지했다. 기본 149개·Godot 관련 22개 통과. 최종 시간은 기존 세 workload에서 약 2.5~3.1% 증가해 추가 조사가 남는다. int 하위 타입의 errno/winerror 불일치를 별도 실패 fixture로 보존했으며 다음 호환성 수정 대상이다.
[82차 결과](reports/2026-10-11/os_error_normalization_dispatch.md): 81차를 `52e3f7c`으로 커밋했다. 생성·호출·builtin 조회 비용을 분리하고, 인자 4~5개에서만 Windows 정규화를 호출하도록 바꿨다. 직접 생성은 OSError 한 인자 약 10.1%·FileNotFoundError 약 14.4% 감소, 할당 동일. 기본 148개·Godot 관련 21개 통과. VM 차이는 -0.95%/+0.94%/+0.83%이며 범위가 겹쳐 전체 속도 개선과 이전 2.4% 증가 원인 해결을 주장하지 않는다. 다음은 BlockingIOError의 characters_written 호환성이다.
[81차 결과](reports/2026-10-11/os_error_directory_subtypes.md): 80차를 `2d39f9e`으로 커밋했다. NotADirectoryError/IsADirectoryError와 errno 20/21 선택을 추가해 Windows 오류 267도 올바른 타입을 고른다. 기본 148개·Godot 기본 및 관련 21개 통과. 할당 동일, 6쌍 측정에서 fallback +1.0%·일반 생성 +2.4% 관측으로 원인 조사가 남는다. 다음 확장 전에 생성/호출/조회 비용을 분리한다. BlockingIOError의 characters_written은 별도 실패 fixture로 보존했다.
[80차 결과](reports/2026-10-11/os_error_winerror_translation.md): 79차를 `acaa18a`으로 커밋했다. Windows 정수 winerror의 errno 변환, HRESULT/Winsock 매핑, 32-bit 경계와 인자 보존을 CPython 3.12.15에 맞췄다. 기본 147개·Godot 기본 및 관련 20개 통과. 기존 생성 경로 할당 동일, 시간 범위 겹침. 아직 없는 errno 하위 타입은 별도 실패 fixture로 보존하며 다음에 보완한다.
[79차 결과](reports/2026-10-11/os_error_dispatch_performance.md): 78차를 `2bc60a0`으로 커밋했다. OSError의 일반 정수 검사를 bool보다 앞에 배치해 직접 생성 비용을 줄였다(errno 999 약 7.4%). VM 전체의 유의한 개선은 확인하지 못했으며, 이전 약 5% 증가는 동일 1만 회 조건에서도 재현되지 않아 원인을 확정하지 않는다. 기본 146개·Godot 소스 통합 75개 통과, 할당 동일. 다음은 Windows winerror 변환 호환성이다.
[78차 결과](reports/2026-10-11/os_error_filesystem_subtypes.md): 77차 `0688409` 이후 PermissionError/FileExistsError 등록과 errno 1/13/17 선택을 구현했다. 기본 146개·Godot 소스 통합 75개 통과. 할당은 동일하지만 6쌍 측정에서 대조군 시간이 약 4.8%/5.2% 증가했다. 원인은 미확정이며 다음 확장보다 이 회귀 관측의 원인 분리를 우선한다. Windows 오류 코드 변환 등은 계속 미해결이다.
[77차 결과](reports/2026-10-11/os_error_arguments.md): 76차를 `d0a1064`로 커밋했다. OSError/FileNotFoundError의 위치 인자·전용 속성·출력을 구현하고 errno 2를 기존 FileNotFoundError 타입에 연결했다. 기본 145개·Godot 소스 통합 74개 통과. 두 예외 각각 1만 회 생성 시 총 480,000 B 감소, 시간 범위는 겹친다. 다음은 다른 errno 하위 타입 선택과 정수 winerror 변환이며 별도 실패 fixture로 보존했다.
[76차 결과](reports/2026-10-11/key_error_identity_and_format.md): 75차를 `997d2bc`로 커밋했다. KeyError가 원래 key와 위치 인자를 보존하고 단일 인자는 repr로 출력하도록 수정했다. mapping/set의 사전 문자열화를 제거하고 PythonException 포맷을 지연해 raise/catch만으로 사용자 repr/str이 실행되지 않게 했다. 기본 143개·Godot 소스 통합 72개 통과. 대조군 할당은 동일하며 시간 범위는 겹친다. 다음은 OSError의 인자·전용 속성과 errno별 생성 규칙이다.
[75차 결과](reports/2026-10-11/lookup_exception_constructors.md): 74차를 `35c2e0f`로 커밋했다. NameError/UnboundLocalError/AttributeError의 위치 인자와 name/obj 키워드·속성·오류 문구를 CPython과 맞췄다. 기본 142개·Godot 소스 통합 71개 통과. NameError와 AttributeError 각각 1만 회 생성에서 할당 총 1,120,000 B 감소. 시간 표본 범위는 겹친다. 다음은 KeyError의 key 보존과 단일 인자 repr 규칙이며 OSError 차이도 유지한다.
[74차 결과](reports/2026-10-11/exception_constructor_arguments.md): 73차를 `1834719`로 커밋했다. 기본 예외 15종에서 원래 Python 인자 배열을 보존해 빈 args·숫자·여러 인자와 키워드 거부를 맞췄다. 기본 141개·Godot 소스 통합 70개 통과. 문자열 예외 생성 1만 회당 할당 640,000 B 감소. 다음은 NameError/AttributeError/KeyError/OSError의 개별 생성·표현 규칙이며 별도 실패 fixture로 보존했다.
[73차 결과](reports/2026-10-11/generator_throw_delegation.md): 72차를 `689e698`로 커밋했다. generator.Throw가 yield from receiver에 먼저 예외를 위임하도록 수정하고 GeneratorExit 처리와 close의 receiver 종료 코드를 공유했다. native 재귀 위임에는 추가 인자 배열·bound method를 만들지 않는다. 기본 139개·Godot 소스 통합 68개 통과. 다음은 기존 예외 생성자의 빈 args 불일치이며 구식 throw 경고 등 후속 항목은 유지한다.
[72차 결과](reports/2026-10-11/coroutine_throw_delegation.md): 71차를 `5cf7f00`으로 커밋했다. coroutine/wrapper throw의 await 위임·재개·GeneratorExit 종료를 구현하고 generator의 예외 이름별 switch를 실제 생성자 기반 공통 정규화로 교체했다. 기본 137개·Godot 소스 통합 66개 통과. send/close 대조군 할당은 동일하며 작은 시간 차이를 속도 개선으로 주장하지 않는다. 다음은 __await__ 내부 yield from의 재귀 throw 위임이며, 예외 생성자 빈 args와 구식 throw 경고 차이도 실패 fixture로 보존했다.
[71차 결과](reports/2026-10-11/unraisable_close_reporting.md): 70차를 `897397e`로 커밋했다. generator/coroutine close의 조회 오류를 sys.unraisablehook으로 보고하고 종료를 계속하도록 수정했다. hook 실패·기본 출력·stderr 경로를 비교했으며 Godot res:// 경로 훼손으로 traceback 소스가 빠지는 문제도 수정했다. 기본 135개·Godot 소스 통합 64개 검증 통과. 다음은 coroutine/wrapper throw 위임과 예외 정규화다.
[70차 결과](reports/2026-10-11/coroutine_close_cleanup.md): 69차를 `7559718`로 커밋했다. coroutine/wrapper close에 위임 종료와 GeneratorExit 주입을 연결하고, 정상 완료 상태 정리를 분리했다. 종료 중 await·재진입 순서를 실제 CPython과 비교했으며 단일 인자 raise의 BaseException 처리도 수정했다. 기본 132개·Godot 소스 통합 61개 통과. 다음은 close 속성 조회 오류의 unraisable 보고이며, 이어서 throw 위임을 검증한다.
[69차 결과](reports/2026-10-11/coroutine_wrapper_send.md): 68차를 `e1eb5e7`로 커밋했다. coroutine_wrapper의 non-None send, 타입·반복자·인자 검사를 구현하고 VM 직접 전달로 정상 descriptor 경로 대비 1만 회 할당 720,000 B를 줄였다. 속도 개선은 주장하지 않는다. 기본 130개·Godot 소스 통합 59개 통과. 이전 generator 시간 증가는 VM 부분 복원 비교까지 기록했으며 원인 확정은 보류한다. 다음은 coroutine close의 위임·GeneratorExit 주입과 finally 정리다.
[68차 결과](reports/2026-10-11/coroutine_entry_and_await.md): 67차를 `97e0282`로 커밋했다. coroutine 최초/재개 스택과 IP, 완료·재진입 예외, globals 보존을 수정하고 await에 기존 SEND 루프를 연결했다. native await의 즉시 이벤트 루프 실행을 제거했다. 기본 128개·Godot 소스 통합 57개 통과. 실패하던 경로의 속도 개선율은 주장하지 않는다. 다음은 중첩 await wrapper의 non-None send 전달과 종료·예외 위임이다.
[67차 결과](reports/2026-10-11/generator_kind_flags.md): 66차를 `bafc2cd`로 커밋했다. async/generator lambda의 종류를 scope에서 정하고 IsGenerator의 명령열 검색을 제거했다. async code object 재생성도 제거해 예외 table과 keyword-only metadata를 보존한다. 기본 126개·Godot 소스 통합 55개 통과. 두 호출 workload에서 1만 회 할당 720,072 B 감소, closure_access 시간 약 11.7% 감소. 실제 CPython 3.12의 lambda StopIteration 처리를 확인해 불필요한 wrapper를 추가하지 않았다. 다음은 별도 재현으로 남긴 coroutine 최초 send/재개 처리다.
[66차 결과](reports/2026-10-11/unbound_variable_and_closure_access.md): 65차를 `206930d`로 커밋했다. 미초기화 local/cell/free variable의 예외 타입·메시지·name, 빈 cell 초기화 및 del-only scope를 맞췄다. nonlocal 저장·삭제의 잘못된 offset 방출을 수정하고 공통 resolver로 closure 접근의 임시 목록 할당을 제거했다. 기본 125개·Godot 소스 통합 54개 통과. 읽기/갱신 workload 측정 시간 약 19.7%/18.7%, 1만 회 할당 1,200,176/960,176 B 감소. 다음은 async/lambda 플래그 및 남은 generator 판정 검색 조사다.
[65차 결과](reports/2026-10-11/finally_scope_analysis.md): 64차를 `e8d2be5`로 커밋했다. FINALLY_TRY 수명과 누락된 TRY_EXCEPT/FINALLY_END 범위를 수정해 continue/finally 스택 오류 및 중첩 finally 중복 실행을 해결했다. 기본 123개·Godot 소스 통합 52개와 CFG native probe 통과. 실행 중 할당은 동일하며 속도 개선은 주장하지 않는다. 다음은 미초기화 지역 변수의 정확한 예외 타입·메시지 처리다.
[64차 결과](reports/2026-10-11/cfg_reachability_analysis.md): 63차를 `faa5daa`로 커밋했다. CFG의 실제 edge를 따라 도달 불가 명령을 제거하고 generator 플래그를 우선 확인한다. 기본 120개·Godot 소스 통합 49개와 CFG native probe 통과. generator 반복은 측정 시간 약 4.5%, 할당 720,144 B 감소했고 cleanup workload 할당은 동일하다. async/lambda 검색 fallback은 남는다. 기존 continue/finally 스택 오류와 nested finally 중복 실행을 별도 재현으로 보존했으며, 다음은 unwind frame 수명 조사다.
[63차 결과](reports/2026-10-11/constant_return_optimization.md): 62차를 `9520ba0`로 커밋했다. bytecode 완전 일치가 아닌 결과 보존과 비용 감소를 기준으로, 상수 반환의 여분 적재·cleanup 보존 및 generator의 중복 종료 명령을 제거했다. 기본 119개·Godot 소스 통합 47개 통과. 두 반환 workload의 시간 약 3.9%/6.4% 감소, 할당 동일. 예외 테이블 구조 비교 불일치는 실행 회귀와 구분해 기록했다. 다음은 CFG terminator 판정과 도달 불가 명령 조사다.

[62차 결과](reports/2026-10-11/super_frame_and_bytecode_analysis.md): 61차를 `a5c5fb2`로 커밋했다. super의 frame cell 인덱스·캡처된 첫 인자·오류 순서를 맞춰 기본 118개·Godot 소스 통합 46개를 통과했다. bytecode 직접 비교에서 공개 opcode 129개 번호는 일치하지만 BINARY_OP 인자 12개와 일부 명령 배치·stack 동작이 다름을 확인했다. 다음은 상수 반환·generator 종료의 중복 명령 제거 검토다. 성능 workload의 할당은 동일하며 유의한 시간 개선은 주장하지 않는다.

[61차 결과](reports/2026-10-11/super_name_analysis.md): 60차를 `f39cc36`로 커밋했다. 함수 scope의 super 이름 읽기를 implicit class 사용으로 분석하고, AST 재검색 및 compiler의 강제 free variable 추가를 제거했다. 기본 116개·Godot 소스 통합 44개 통과. 기존 workload의 할당은 동일하며 실행 시간 차이는 표본 범위가 겹친다. 다음은 self가 캡처된 메서드에서 실패하는 인자 없는 super의 frame/cell 조회와 오류 처리다.

[60차 결과](reports/2026-10-11/class_lexical_closure_analysis.md): 59차를 `6b21479`로 커밋했다. 직접 `__class__` 참조의 implicit cell을 심볼 분석에서 생성하고, 메서드 바인딩의 함수·closure 복제와 덮어쓰기를 제거했다. 클래스 namespace 대입도 해당 scope를 따르도록 수정했다. 기본 114개·Godot 소스 통합 42개 통과. class cell이 있는 metaclass 호출 1만 회 할당 10,640,000 B 감소, 측정 시간 약 16.6% 감소. 다음은 별도 실패 fixture로 보존한 `super` alias의 implicit cell 분석이다.

[59차 결과](reports/2026-10-10/metaclass_iterator_analysis.md): 58차를 `cc36b07`로 커밋했다. metaclass iteration의 descriptor·반환 검증·None 차단·sequence fallback과 클래스 자체의 next를 공통 프로토콜에 연결했다. 기본 112개·Godot 소스 통합 40개 통과. 임시 bound method 제거로 iter+next 1만 회 할당 800,000 B 감소. 다음은 수정 전후 모두 실패하는 직접 `__class__` 참조의 implicit cell 생성과 명시적 metaclass 메서드 바인딩 조사다.

[58차 결과](reports/2026-10-10/iterator_protocol_analysis.md): 57차를 `49a37ea`로 커밋했다. 사용자 객체의 iter 특수 메서드 조회·반환값 검증과 sequence fallback의 오류 전파·영구 종료를 맞췄다. 기본 110개·Godot 소스 통합 38개 통과. iter 호출 1만 회 할당 240,000 B 감소, 측정 시간은 약 1.2% 증가하며 표본 범위가 겹친다. 다음은 별도 실패 fixture로 보존한 metaclass iterator 반환값 검증이다.

[57차 결과](reports/2026-10-10/generator_send_protocol_analysis.md): 56차를 `9ae7747`로 커밋했다. SEND의 None/값 전송 경로를 구분하고 사용자 __next__의 타입 lookup·descriptor 바인딩을 맞췄다. 기본 106개·Godot 34개 통과. Python iterator next 벤치마크 약 4.9% 단축, 1만 회 할당 24만 B 감소. 다음은 __iter__의 special lookup과 반환 iterator 검증이며 별도 실패 fixture로 보존한다.

[56차 결과](reports/2026-10-10/generator_delegation_close_analysis.md): 55차를 `2960df4`로 커밋했다. yield from의 내부 iterator를 먼저 닫고 그 오류를 바깥 handler/finally에 전달한다. CLEANUP_THROW와 예외 주입 스택을 CPython 규칙에 맞췄다. 기본 104개·Godot 32개 통과. 측정 할당량은 동일하며 직접 close 시간은 약 1.4% 증가했다. 다음은 send 없는 사용자 iterator의 SEND 경로다. close 속성 조회 오류의 unraisable 처리와 특수 재진입 차이도 보고서에 남겼다.

[55차 결과](reports/2026-10-10/generator_close_analysis.md): 54차를 `05557b7`로 커밋했다. 직접 실행 generator의 close 예외 전파·종료 거부·재진입·시작 전 종료를 맞추고 GeneratorExit builtin과 빈 args를 수정했다. 기본 102개·Godot 30개 통과. close 벤치마크 시간 약 12% 감소, 1만 회 할당 약 64만 B 감소. 일반 generator 반복 시간은 약 0.7% 증가했다. 다음은 yield from의 하위 iterator 종료 순서이며 별도 실패 fixture로 보존한다.

[54차 결과](reports/2026-10-10/caller_exception_state_analysis.md): 53차를 `165ef9e`로 커밋했다. 실행 스택에서 처리 중인 예외만 조회해 callee의 bare raise와 context, sys 예외 관찰 및 generator 재개 상태를 맞췄다. 기본 100개·Godot 28개 통과. 측정 workload의 할당량은 동일하고 예외 시간 차이는 약 0.1%. 다음은 generator.close의 cleanup 예외 전파 오류이며 별도 실패 fixture로 보존한다.

[53차 결과](reports/2026-10-10/handled_exception_state_analysis.md): 52차를 `d38f35f`로 커밋했다. handler 진입 전 CurrentException 덮어쓰기를 제거하고 bare raise를 RAISE_VARARGS 0으로 맞췄다. 이전 예외 복원·GeneratorExit 상태·오류 메시지를 검증해 기본 98개, Godot 26개 통과. 다음은 호출자와 피호출자 사이의 handled exception 공유이며 별도 실패 fixture로 보존한다.

[52차 결과](reports/2026-10-10/exception_stack_depth_analysis.md): 51차를 `1393896`으로 커밋했다. SETUP 시점의 스택 깊이를 계산하고 정확히 unwind해 표현식 yield의 throw를 수정했다. 합류 검사가 드러낸 try/finally·async 컴파일 오류와 with의 GeneratorExit 처리도 수정했다. 기본 96개·Godot 24개 통과, protected yield의 depth/lasti CPython 일치. 예외 1만 회 할당 120만 B 감소. 다음은 처리 완료된 예외가 __context__에 남는 기존 문제이며 별도 실패 fixture로 보존한다.

[51차 결과](reports/2026-10-10/generator_throw_resume_analysis.md): 50차를 ddec605로 커밋했다. generator.throw에서 yield 위치를 유지하고 정상 send 결과를 스택에 넣지 않아 루프 중간 조기 종료를 수정했다. 기본 94개·Godot 22개 통과. 남은 `100 + (yield i)` 실패는 CFG의 고정 exception depth 및 VM의 비표준 unwind와 연결돼 있어 다음 최우선 과제로 삼는다. 별도 실패 재현은 통과 목록에 포함하지 않는다.

[50차 결과](reports/2026-10-10/cache_dispatch_analysis.md): 49차를 a4da1e9로 커밋했다. CACHE word의 offset은 유지하면서 불필요한 소스 위치 조회·dispatcher 호출을 생략한다. 측정한 덧셈/함수호출/속성 workload 중앙값 17.1%/15.0%/46.1% 단축. 별도 재현에서 기존 generator.throw의 루프 중간 조기 종료를 발견했으며 다음 최우선 수정 대상이다. 이 실패는 CACHE 수정 전후 모두 존재한다.

[49차 결과](reports/2026-10-10/cfg_retention_analysis.md): 48차를 ae7c2c8로 커밋했다. 예외 테이블 생성 후 최종 instruction의 CFG 참조를 끊어 컴파일 자료 유지 경로를 제거했다. 33개 코드 객체의 실행 메타데이터 해시는 동일하고 최종 배열에서 도달하는 CFG 블록은 0개다. 전체 92개 및 Godot 회귀 20개 통과. 다음은 CACHE dispatch와 소스 위치 갱신 비용 조사다.

[48차 결과](reports/2026-10-10/instruction_dispatch_analysis.md): 47차를 cbc1b20로 커밋했다. 40B instruction 전체 대신 opcode·인자만 private dispatcher에 전달한다. 측정한 덧셈·함수 호출 약 9.5%, comprehension 약 11.9% 단축, 할당 동일. 공개 IR과 예외/소스 메타데이터는 유지한다. 다음은 최종 배열의 메타데이터 유지량 및 instruction fetch 비용 조사다.

[47차 결과](reports/2026-10-10/jump_backward_allocation_analysis.md): 45·46차를 826f64e로 커밋했다. 일반 JUMP_BACKWARD 계산은 이미 상수 시간이었지만 Release에 디버그용 배열 생성이 남아 있었다. 이를 제거해 루프 back edge당 40B 할당을 없앴다. 전체 92개 평가와 Godot 기존 17개·신규 큰 generator 회귀 통과. 다음은 실행 instruction 표현의 크기와 복사 비용 측정이다.

[46차 결과](reports/2026-10-10/instruction_offset_analysis.md): VM 구조 검증으로 우선순위를 옮겼다. 큰 함수의 jump/CACHE/EXTENDED_ARG/예외 동작은 CPython과 일치했고, 핸들러 byte offset→instruction index의 불필요한 선형 순회를 상수 시간 계산으로 줄였다. 전체 91개 평가 및 Godot 소스 빌드·신규 회귀 통과. 다음은 별도 점프 변환 호출 경로와 더 큰 jump/generator 경계 검증이다.

[45차 조사](reports/2026-10-10/range_constructor_analysis.md): 44차는 688d46e로 커밋했다. range 생성 최적화 후보들은 성능 효과가 작거나 다른 경로가 느려져 제외했다. 런타임 소스 변경 없이 인수 개수·bool·큰 정수 성능 측정과 변환 실패 순서 회귀를 보강했다. 수정 fixture의 실제 CPython 비교 및 Godot 소스 빌드·실행 통과. 생성 비용은 미해결이며 후속 JIT 분석이 필요하다.

[44차 결과](reports/2026-10-10/range_slice_step_analysis.md): 43차는 fef86bd로 커밋했다. range slice의 step 중복 변환을 제거해 직전 대비 약 12.7% 단축했고 Godot에서도 큰 정수 지원 전 slice 비용 수준을 회복했다. 실제 CPython 3.12.15와 기본 90개 평가 및 Godot 소스 통합 18개 회귀 통과. builtin 생성 비용과 C# 공개 API 호환 검토는 남아 있다.

[43차 진행 중](reports/2026-10-10/range_bigint_work_in_progress.md): 큰 정수용 range 구현을 연결했고 Godot 소스 빌드·17개 회귀는 통과했다. 사용자가 Smart App Control을 끈 후 일반 Release 빌드와 실제 CPython 3.12.15 대조를 재개했다. 기본 89개는 완전 일치 84개·변동값 제외 일치 3개·C# 검사 2개 통과다. Godot 전후 측정에서 할당은 유지했지만 일반 slice 약 13% 지연과 builtin 생성 비용이 남아 있다. 변경은 미커밋이며 일반 경로 최적화와 C# 공개 API 검토를 마쳐야 한다.

[42차 결과](reports/2026-10-10/slice_indices_bigint_analysis.md): 임의 정밀도 slice 정규화를 추가하고 slice.indices의 500개 조합·callback/예외 순서를 CPython과 대조했다. 기본 87개와 Godot 소스 통합 회귀 15개 통과. 기존 range/sequence slice probe 54개 조건에서 할당은 동일하다. 다음은 일반 long 경로를 유지하는 range의 큰 정수 표현과 직접 순회 호출부 정리다. range 큰 step 최소 재현은 아직 미해결이다.

[41차 결과](reports/2026-10-10/range_native_boundary_analysis.md): range 확장 조사 중 발견한 signed-long 경계 길이·반복 종료·인덱스 문제를 먼저 수정했다. 기본 86개와 Godot 소스 통합 회귀 14개 통과. 반복자 할당은 8B 감소했고 GetValues 할당은 유지했다. 일부 길이 조회 비용 증가는 보고서에 기록했다. 임의 정밀도 step과 len/slice 한계는 후속 과제다.

[40차 결과](reports/2026-10-10/memoryview_large_step_analysis.md): memoryview 큰 step·native stride·중첩 대입을 맞추고 sys.maxsize를 바로잡았다. 기본 85개와 Godot 소스 통합 회귀 13개 통과. native view 생성·slice 할당은 기존과 같으며 읽기·쓰기 시간 범위도 겹친다. 다음은 range의 임의 정밀도 step이다.

[39차 결과](reports/2026-10-10/operator_length_hint_analysis.md): _operator.length_hint를 공통 프로토콜에 연결하고 기본값 검증 순서·음수/초대형 값·keyword 처리를 맞췄다. 기본 83개와 Godot 소스 통합 회귀 12개 통과. 다음은 기존 range/memoryview 큰 step 표현 문제로 돌아간다.

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
