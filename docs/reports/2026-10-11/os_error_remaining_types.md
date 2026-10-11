# 85차: OSError의 남은 native 하위 타입과 errno 매핑

84차를 `b47ab97`로 커밋했다. 이번에는 ConnectionError와 연결 오류 4종, ChildProcessError, InterruptedError, ProcessLookupError, TimeoutError를 등록하고 OSError 생성 시 선택하도록 연결했다. 새 변경은 검토를 위해 미커밋 상태로 남긴다.

## 범위와 근거

CPython 3.12.15 고정 소스 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/exceptions.c`에서 OSError_new, errnomap 초기화, Windows socket errno 재정의를 확인했다. 기존 `tests/compatibility/os_error_type_selection.py`의 실패 사례는 새 회귀 파일로 통합했다.

- 공통 errno 3/4/10/32는 각각 ProcessLookupError/InterruptedError/ChildProcessError/BrokenPipeError다.
- Windows socket 오류는 CRT의 동명 errno가 아니라 Winsock 값으로 선택한다. winerror 정규화 후에도 같은 선택 경로를 사용한다.
- ConnectionError 자체를 선택하는 errno는 없다. BrokenPipeError와 ConnectionAbortedError/ConnectionRefusedError/ConnectionResetError의 공통 부모다.
- 직접 하위 타입 생성은 errno에 따라 다른 타입으로 바뀌지 않는다. 기존 OSError 생성자를 공유해 args, filename, filename2, winerror와 키워드 거부 동작을 유지한다.

비Windows 값은 [Apple errno.h](https://github.com/apple-oss-distributions/xnu/blob/main/bsd/sys/errno.h), [Linux asm-generic errno.h](https://github.com/torvalds/linux/blob/master/include/uapi/asm-generic/errno.h), [FreeBSD errno.h](https://github.com/freebsd/freebsd-src/blob/main/sys/sys/errno.h)의 정의와 대조했다. 기존 플랫폼 구분과 같은 Windows/Apple/FreeBSD/Linux·Android 범위로 구현했다. 비Windows 실행은 하지 않았으며 WASI 및 별도 errno 체계를 쓰는 아키텍처 지원을 주장하지 않는다.

## 구현과 구조

기존 일반 정수의 파일 오류·BlockingIOError 선택 순서를 유지하고 그 뒤에 남은 errno 선택을 연결했다. 큰 정수는 범위를 확인한 뒤에만 int로 변환한다. 새 인스턴스 필드, 조회 사전, delegate, 외부 DLL 의존성은 추가하지 않았다.

회귀 비교에서 VM의 이름→CLR 타입 조회에 ConnectionError가 없어 BrokenPipeError를 `except ConnectionError`로 잡지 못하는 문제가 드러났다. 실제 CLR/Python 상속 관계를 등록하는 것과 함께 이 조회 항목도 추가했다. 기존 예외 처리 구조의 전면 교체는 이번 범위에 포함하지 않는다.

errno int 하위 타입은 여전히 미해결이다. CPython은 errnomap 사전 조회 중 사용자 __hash__/__eq__를 호출하므로, 내부 숫자만 추출하는 구현으로 대체하면 안 된다. 먼저 조회 대상 타입을 보완했으며 다음 단계에서 특수 메서드 호출과 오류 전파를 다룬다. 그 실패 재현은 `tests/compatibility/os_error_int_subclass.py`에 유지한다.

## 검증

새 `tests/os_error_remaining_types_regression.py`는 다음을 실제 CPython 3.12.15와 비교한다.

- 9개 타입의 상속 관계, 직접 생성, 빈/단일/다중 인자, args·errno·파일명·문자열, 키워드 오류.
- ConnectionError/OSError 부모 타입으로 잡는 예외 처리.
- 호스트와 다른 플랫폼의 socket 번호, bool/float/문자열/None/큰 정수, 인자 개수에 따른 선택 생략.
- Windows broken pipe/child process/interrupted/socket winerror 변환 후 타입 선택.

수정 전 DLL에서는 새 회귀가 실패하고 수정 후에는 출력이 일치한다. Release 빌드 및 전체 151개 평가 결과는 검증 JSON에 보존한다. Godot는 SharpPy 소스를 직접 포함해 자체 빌드했고 기본 17개와 OSError 관련 7개, 총 24개 fixture를 통과했다. 관련 7개는 새 CPython 실행 출력과 직접 비교했다. 별도 SharpPy DLL을 Godot에 넣지 않았다. 모바일 실기 및 비Windows 실행은 하지 않았다.

## 성능

Windows .NET 8.0.31 Release, TieredCompilation=0. 기존 세 workload에서 10만 회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행했다. 빌드·기능 테스트와 겹치지 않았다. 아래 값은 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 양쪽 할당량 |
|---|---:|---:|---:|---:|
| os_error_fallback | 75.5905 | 75.9064 | +0.42% | 25,904,120 B |
| os_error_construction | 99.8335 | 100.8964 | +1.06% | 41,104,040 B |
| os_error_winerror | 91.9388 | 91.5902 | -0.38% | 41,104,120 B |

세 workload 모두 프로세스 중앙값 범위가 겹치며 할당량은 동일하다. 속도 개선을 주장하지 않으며 일반 생성의 +1.06% 관측도 기록한다. 앞선 회차의 시간 증가 원인이 해소되었다는 결론은 내리지 않는다. 이전에 없던 예외 타입의 생성 시간을 성능 향상 근거로 사용하지 않았다.

[검증 결과와 측정 표본](round85-validation.json)

## 다음 작업

1. errno int 하위 타입의 __hash__/__eq__, 해시 불가능한 타입, callback 오류·NotImplemented·진리값 처리를 실제 CPython과 대조한다. CPython dict의 내부 슬롯 배열을 모사할 필요가 있는지와 관찰 가능한 동작을 구분한다.
2. 일반 정수 선택 경로와 예외 생성 비용의 성능 관측을 계속 추적한다.
3. 큰 문자열에서 int 하위 타입 생성이 실패하는 별도 재현 `tests/compatibility/int_subclass_large_string.py`를 해결한다.
