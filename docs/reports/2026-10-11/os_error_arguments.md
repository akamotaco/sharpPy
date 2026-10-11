# OSError 인자와 전용 속성 — 77차

76차를 `d0a1064` (`fix: preserve KeyError keys and defer exception formatting`)로 커밋했다.

## 수정 범위

OSError와 FileNotFoundError의 Python 생성자가 원래 위치 인자를 보존하도록 연결했다. CPython처럼 인자 2~5개일 때 errno·strerror를 저장하고, None이 아닌 filename이 있으면 filename/filename2를 별도 속성으로 저장하면서 args에는 처음 두 인자만 남긴다. 0~1개 또는 6개 이상이면 일반 예외의 args 처리를 사용한다.

errno, strerror, filename, filename2는 기본 조회값이 None이며 대입·삭제할 수 있다. 미설정과 명시적인 None은 내부적으로 구별해 문자열 표현에 반영한다. args 변경은 전용 속성을 덮어쓰지 않는다. Windows에서는 winerror 속성과 그 출력 우선순위도 구현했다. 플랫폼 판정에는 .NET의 OperatingSystem.IsWindows를 사용하며 네이티브 DLL 호출을 추가하지 않았다.

기존에 타입이 구현되어 있는 FileNotFoundError는 OSError의 errno가 정수 2이고 인자가 2~5개인 경우 선택한다. 직접 FileNotFoundError를 생성하면 errno로 다른 타입을 선택하지 않는다. 키워드는 거부한다. C# 내부의 문자열 메시지 생성자는 유지했다.

**아직 전체 errno 매핑이나 정수 winerror→errno 변환을 구현한 것은 아니다.** 예를 들어 errno 13의 PermissionError 선택과 Windows 오류 코드 2의 errno 2 변환은 다음 작업이다. 이번 변경은 인자·속성 기반과 기존 FileNotFoundError 타입 연결을 먼저 검증한 단계다.

참조는 CPython 3.12.15 및 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/exceptions.c:oserror_parse_args, oserror_init, OSError_new, OSError_str 및 실제 실행 결과다.

## 검증

- .NET Release 빌드 성공.
- 기본 145개: 완전 출력 일치 140개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- os_error_arguments_regression.py: 인자 개수별 처리, errno 2의 기존 하위 타입 선택, 직접 FileNotFoundError 생성, None·문자열 errno, 두 파일명, args와 속성의 독립성, 속성 identity·대입·삭제·반복 삭제, 키워드 거부, __index__ 미호출을 비교했다.
- Windows winerror의 비정수 객체·명시적 None·삭제 후 표현을 추가해 .NET/Godot에서 최종 재검증했다. 정수 변환은 통과 범위에서 제외하고 별도 실패로 보존했다.
- exception_specialized_arguments.py를 기본 통과 목록으로 승격했다. 두 새 통과 fixture 모두 이전 DLL에서 실패하거나 출력이 달랐다.
- Godot에 저장소 소스를 직접 포함해 빌드했다. 기본 17개 및 native helper, 추가 57개 출력 비교 통과. 합계 74개 fixture. 기존 traceback 1개는 컴파일 입력 파일명만 res://로 바꾸어 비교했다. 별도 SharpPy DLL 의존성을 추가하지 않았다. Windows 호스트 검증이며 모바일 실기·비Windows 런타임 실행을 검증했다는 의미는 아니다.
- CFG reachability probe와 git diff --check 통과.

원본 요약은 [round77-validation.json](round77-validation.json), 상세 로그는 `.artifacts/round77-full.json`, `round77-final-targeted.json`, `round77-godot-comparison.json`에 보존했다.

## 성능

수정 전후 args·str 결과가 같은 문자열 인자 OSError와 FileNotFoundError를 각각 1만 개 생성하는 workload를 추가했다. ValueError 생성은 대조군이다. 잘못된 errno/filename 처리와 정상 결과를 처리량 개선율로 비교하지 않는다.

.NET 8.0.31, TieredCompilation=0, warmup 10회·측정 10회, 전후 교대 3쌍. 빌드·기능 검증과 측정은 겹치지 않았다. workload 반환 합계 검증을 통과했다. 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| OSError+FileNotFoundError 각 1만 회 | 9.328 | 9.229 | 4,481,192 | 4,001,192 |
| ValueError 1만 회 대조군 | 6.427 | 6.360 | 2,001,184 | 2,001,184 |

대상 할당은 총 480,000 B 감소했다. 다섯 전용 참조 슬롯으로 객체당 40 B가 추가되지만 Python 생성자의 메시지 재포장 객체·배열을 없앤 효과가 더 크다. C# 내부 문자열 생성자는 전용 슬롯 크기를 추가로 부담한다.

시간 중앙값은 대상 약 1.05%, 대조군 약 1.04% 감소했다. 대상 프로세스 중앙값 범위는 이전 9.205–9.429 ms/이후 9.143–9.277 ms, 대조군은 이전 6.217–6.585 ms/이후 6.325–6.405 ms다. 모두 겹치므로 유의한 속도 개선으로 주장하지 않는다.

## 다음 순서

os_error_type_selection.py에 errno 13/17의 PermissionError/FileExistsError 선택과 Windows winerror 변환 차이를 보존했다. 다음은 errno별 예외 타입 계층·등록·선택 규칙이며, 이어서 Windows 오류 코드 변환과 BlockingIOError의 characters_written 등 개별 규칙을 분석한다. CPython의 플랫폼별 errno 값을 구분하고 네이티브 DLL 의존성을 추가하지 않는 방향으로 진행한다.

예외 직렬화·사용자 정의 OSError 하위 클래스의 __new__/__init__·모든 사용자 객체 포맷·기존 구식 throw 경고 등은 이번 검증 범위에 포함하지 않는다. 이전 후속 항목도 유지한다.
