# 80차: Windows OSError winerror 변환

79차를 `acaa18a`으로 커밋했다. 이번 변경은 Windows에서 정수 winerror를 errno로 변환하고, 변환된 값으로 현재 지원하는 예외 하위 타입을 선택한다. .NET 단독 실행과 Godot 소스 포함 빌드가 같은 코드를 사용하며 외부 DLL이나 native 호출을 추가하지 않았다.

## 근거와 구현

CPython 3.12.15의 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`을 기준으로 [oserror_parse_args](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/exceptions.c#L1713)와 [PC/errmap.h](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/PC/errmap.h)를 확인하고 실제 Windows CPython 출력과 대조했다.

- Windows에서 인자가 4~5개이고 네 번째 인자가 int/bool이면 변환한다. __index__는 호출하지 않는다.
- Windows C long의 signed 32-bit 범위를 검사한다. 범위를 벗어나면 CPython과 같은 OverflowError 문구를 사용한다.
- FACILITY_WIN32 HRESULT, Winsock 별칭 및 나머지 오류 번호를 CPython의 매핑과 맞춘다. 미등록 Windows 코드는 EINVAL(22)이다.
- 원본 winerror 객체는 보존하고 errno 및 args 첫 항목을 교체한다. 호출자 인자 배열은 수정하지 않는다.
- OSError 팩터리는 변환 후 하위 타입을 선택한다. 직접 FileNotFoundError/PermissionError/FileExistsError를 생성하면 요청한 타입을 유지한다.
- kwargs 거부가 정수 변환 오류보다 먼저 발생한다. 범위를 벗어난 인자 개수에서는 변환하지 않는다.
- 비Windows에서는 기존처럼 네 번째 인자를 변환하지 않는다. 이번 실행 검증은 Windows에서만 수행했다.

정상화 여부를 내부 생성자에 전달해 팩터리에서 수행한 변환과 배열 복사를 다시 하지 않는다. 실제 정수 winerror 변환에만 대체 배열과 errno 객체를 만들며 일반 생성에는 배열을 추가하지 않는다. 파일명이 있으면 기존의 두 항목 args 배열도 생성된다. 이 변환 경로의 추가 할당을 속도 개선으로 표현하지 않는다.

## 회귀 검증

새 `tests/os_error_winerror_regression.py`는 -2~269 전체와 높은 오류 코드, HRESULT, Winsock 별칭/경계, 32-bit 경계/초과, bool/비정수, 직접 하위 타입 생성, 파일명 유무, 원본 인자 보존, kwargs 우선순위 및 속성 변경 후 타입 유지 등을 비교한다. 매핑 전체의 검증은 직접 FileNotFoundError 생성으로 수행해 아직 없는 하위 타입 등록 문제와 구분했다.

- 수정 전 DLL: 새 회귀 파일 출력 불일치 확인.
- 수정 후 전체 평가 147개: 출력 일치 142개, 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot 소스 포함 자체 컴파일/빌드 통과. 기본 17개와 OSError 관련 3개, 총 20개 fixture 실행 통과. 추가 3개는 실제 CPython 출력과 일치했다.
- `git diff --check` 통과.

Godot 관련 검증은 기존 전체 추가 목록을 재실행한 것이 아니라 기본 목록과 이번 변경에 관련된 예외 파일을 실행한 결과다. 비Windows 및 모바일 실기는 실행하지 않았다.

## 기존 경로 성능

.NET 8.0.31 Release, TieredCompilation=0. 수정 전후 동일 하네스에서 각 10만 회, 워밍업 10회, 표본 15회, 3쌍의 프로세스를 교대 실행했다. 빌드/회귀 실행과 겹치지 않았다. 값은 프로세스별 중앙값 3개의 중앙값이며 단위 ms다.

| workload | 이전 | 변경 후 | 차이 | 양쪽 할당량 |
|---|---:|---:|---:|---:|
| os_error_fallback | 73.5976 | 73.9126 | +0.43% | 25,904,120 B |
| os_error_construction | 97.9880 | 97.4973 | -0.50% | 41,104,040 B |

프로세스별 범위가 겹치며 속도 개선을 주장하지 않는다. 이 대조는 winerror 없는 기존 경로의 회귀 관찰이다. 수정 전에는 winerror 결과가 달랐으므로 해당 경로의 전후 속도를 동등한 작업의 개선으로 비교하지 않았다.

[전체 출력과 측정 표본](round80-validation.json)을 보존한다.

## 남은 차이와 다음 작업

Windows 코드 변환은 구현했지만 전체 OSError 하위 타입은 아직 완성되지 않았다. 예를 들어 winerror 267의 errno는 20으로 맞춰졌지만, NotADirectoryError가 없어 타입은 OSError로 남는다. 89, 109, Winsock 10035/10054 등도 해당 하위 타입 등록이 필요하다.

`tests/compatibility/os_error_type_selection.py`를 남은 타입 선택 차이로 갱신하고 실제 출력 불일치를 확인했다. 이 파일은 통과하는 기본 평가 목록에 넣지 않는다. 다음에는 CPython의 errno별 하위 타입과 상속 관계를 대조해 하나씩 보완한다.
