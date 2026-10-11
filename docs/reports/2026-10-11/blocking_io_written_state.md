# 83차: BlockingIOError와 characters_written

82차를 `e24adae`으로 커밋했다. BlockingIOError를 등록하고 errno 선택 및 생성자의 세 번째 인자 처리를 구현했다. OSError 계열의 characters_written 읽기·설정·삭제도 연결했다. 이번 변경은 미커밋 상태다.

## CPython 기준과 플랫폼 차이

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 로컬 소스에서 `Objects/exceptions.c`의 oserror_init, OSError_written_get/set, errnomap과 `Objects/abstract.c`의 PyNumber_Check/PyNumber_AsSsize_t를 대조했다. Windows CPython 3.12.15 실행 결과로 검증했다.

Windows에서는 EAGAIN=11과 Winsock 10035/10036/10037이 BlockingIOError를 고른다. Unix 계열에 Windows 값을 그대로 적용하지 않는다. Linux/Android 계열은 11/114/115, Apple/FreeBSD 계열은 35/36/37을 사용한다. 숫자 근거는 [Linux errno](https://github.com/torvalds/linux/blob/master/include/uapi/asm-generic/errno.h), [Linux errno-base](https://github.com/torvalds/linux/blob/master/include/uapi/asm-generic/errno-base.h), [Apple errno](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/errno.h), [FreeBSD errno](https://github.com/freebsd/freebsd-src/blob/main/sys/sys/errno.h)다. 비Windows 실행 검증은 하지 않았으며 Linux의 특수 아키텍처별 errno 차이까지 검증한 것은 아니다.

## 구현

- 정확히 BlockingIOError이고 세 번째 인자가 숫자 프로토콜을 갖는 경우에만 문자 수로 해석한다. 숫자 여부 판정은 __index__/__int__/__float__의 타입 수준 존재 여부와 기본 숫자 타입을 본다. 실제 변환은 __index__만 호출한다.
- 문자 수 경로는 filename/filename2를 설정하지 않고 원래 args를 보존한다. 직접 BlockingIOError 생성은 다른 errno를 받아도 타입을 유지한다.
- characters_written은 OSError 전체에서 접근할 수 있다. 미설정 또는 -1이면 AttributeError('characters_written')다. 유효 값 삭제 후 다시 삭제해도 같은 오류다.
- 변환 실패 시 이전 값을 보존한다. host ssize_t 범위를 벗어나면 ValueError를 낸다. callback 내부에서 filename2나 문자 수를 갱신하는 재진입도 검증했다.
- 읽기는 기존 SmallIntCache를 재사용해 작은 정수의 객체 identity와 할당을 맞춘다. 새 전역 캐시는 만들지 않았다.

일반 OSError마다 문자 수 필드를 추가하면 모든 생성에서 크기가 늘어난다. 이를 피하기 위해 기존 filename2 저장 슬롯을 일반 파일명 또는 WrittenState로 사용한다. 문자 수가 실제 설정된 경우에만 작은 보조 객체를 만든다. WrittenState는 filename2도 보존하므로 두 속성의 변경·삭제가 서로의 값을 잃지 않는다. 문자 수가 없으면 기존 파일명 저장으로 돌아간다. 일반 OSError의 필드 수와 측정 할당량은 유지한다. 문자 수를 사용하는 새 경로에는 보조 객체 할당 비용이 있다.

## 검증

기존 실패 파일 `tests/compatibility/blocking_io_arguments.py`를 `tests/blocking_io_arguments_regression.py`로 옮겨 확장하고 기본 평가에 포함했다.

- builtin identity와 상속, errno 선택, 직접 생성, 인자 개수, winerror 변환 후 선택, kwargs 거부, filename/filename2/args 출력을 비교했다.
- 숫자/비숫자 구분, __index__ 호출, __int__/__float__ 미호출, 잘못된 반환값/사용자 예외, 크기 초과, 실패 후 상태, 두 속성 동시 보존, 재진입을 비교했다.
- -1 sentinel, 작은 정수 캐시 경계, host ssize_t 최솟값/최댓값을 비교했다.
- 수정 전 DLL에서는 새 회귀 파일이 실패함을 확인했다.
- 최종 Release 빌드 성공. 전체 평가 149개: 출력 일치 144개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot 소스 포함 자체 빌드 성공. 기본 17개와 관련 5개, 총 22개 fixture 통과. 관련 5개 출력은 CPython과 일치한다.
- `git diff --check` 통과. 별도 SharpPy DLL이나 외부 native 호출을 추가하지 않았다. 모바일 실기 및 비Windows 실행은 수행하지 않았다.

## 성능: 할당 유지, 시간 증가는 남음

.NET 8.0.31 Release, TieredCompilation=0, 반복 10만 회, 워밍업 10회, 측정 15회다. 전후 3쌍의 프로세스를 교대로 실행했고 빌드/회귀와 겹치지 않았다. 아래는 프로세스별 중앙값 3개의 중앙값이다.

| 기존 workload | 이전 ms | 최종 ms | 변화 | 양쪽 할당량 |
|---|---:|---:|---:|---:|
| os_error_fallback | 74.9435 | 76.7904 | +2.46% | 25,904,120 B |
| os_error_construction | 100.1710 | 103.3177 | +3.14% | 41,104,040 B |
| os_error_winerror | 89.9978 | 92.6627 | +2.96% | 41,104,120 B |

초기 구현에서도 증가가 관찰되어 정확한 타입 비교와 Windows 연속 코드 범위 비교로 좁힌 뒤 위 최종 측정을 수행했다. 초기 표본도 원시 기록에 보존한다.

일반 생성의 프로세스 중앙값 범위는 이전 99.4564–100.6288, 이후 101.8199–104.6169 ms로 겹치지 않으며 세 쌍 모두 증가했다. 다른 두 workload도 최종 세 쌍에서 모두 증가했다. 범위가 일부 겹친다고 회귀가 없다고 해석하지 않는다. 이번 변경은 호환성 확장이며 속도 개선을 주장하지 않는다. 단일 인자 일반 생성은 문자 수 처리에 들어가지 않으므로 모든 증가를 새 숫자 판정 비용으로 설명할 수 없다. 생성 분기와 코드 배치의 영향은 추가 조사가 필요하다.

[출력·최종 성능 표본·초기 비교·별도 미해결 사례](round83-validation.json)를 보존한다.

## 남은 차이와 다음 작업

조사 중 기존 errno/winerror 파서가 사용자 정의 int 하위 타입을 일반 정수처럼 처리하지 못하는 것을 확인했다. `tests/compatibility/os_error_int_subclass.py`에 FileNotFoundError/BlockingIOError/NotADirectoryError 선택과 Windows 변환 차이를 보존하고 실제 출력 불일치를 확인했다. 이 파일은 기본 통과 평가에 포함하지 않는다.

다음 호환성 수정은 이 int 하위 타입 처리다. 기존 `os_error_type_selection.py`에서는 해결한 BlockingIOError 번호를 제거하고 아직 없는 다른 하위 타입의 차이를 남겼다. 일반 생성 경로의 시간 증가도 함께 추적해야 하며, 현재 결과를 OSError 전체 호환성 완성이나 성능 문제 해결로 표현하지 않는다.
