# 81차: 디렉터리 OSError 하위 타입

80차를 `2d39f9e`으로 커밋했다. NotADirectoryError와 IsADirectoryError를 등록하고 errno 20/21 선택을 구현했다. 이번 변경은 작업 트리에 남긴다.

## 구현 범위와 근거

CPython 3.12.15 고정 소스 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/exceptions.c`에 있는 두 하위 타입 정의와 errnomap의 ENOTDIR/EISDIR 등록을 확인했다. 실제 Windows CPython 실행 결과와 비교했다.

두 타입은 OSError를 직접 상속하며 기존 인자·속성·winerror 처리와 출력 구현을 재사용한다. 추가 인스턴스 필드나 캐시는 없다. builtin 모듈과 PyType 생성 경로에 연결하고, 기존 errno 선택 뒤에 두 정수 비교를 추가했다. 직접 하위 타입을 생성하면 다른 errno를 주어도 타입을 유지한다. float/문자열/__index__ 객체를 정수로 강제 변환하지 않는다.

Windows 오류 267과 이에 대응하는 signed HRESULT는 이전 회차의 변환으로 errno 20이 된 후 NotADirectoryError를 선택한다. 일반 파일 I/O 모듈 전체의 오류 변환을 변경한 것은 아니다.

BlockingIOError는 세 번째 인자를 characters_written으로 처리하는 별도 규칙이 있어 단순 타입 등록에서 제외했다. 다음 검증용 `tests/compatibility/blocking_io_arguments.py`에 실제 CPython 동작과 현재 SharpPy 실패를 보존했다.

## 검증

- Release 빌드 성공.
- 새 `os_error_directory_regression.py`: builtin identity, 상속, isinstance/issubclass, 직접 생성, errno 선택, except 분기, 인자 개수, kwargs 거부, 큰 정수/비정수, Windows 변환을 대조했다.
- 수정 전 DLL에서는 새 회귀 파일이 실패하고, 수정 후 CPython과 출력이 일치한다.
- 전체 평가 148개: 출력 일치 143개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot 소스 포함 빌드 성공. 기본 17개와 OSError 관련 4개, 총 21개 fixture 통과. 추가 4개는 CPython 출력과 일치했다. 별도 SharpPy DLL은 사용하지 않았다.
- `git diff --check` 통과.

Windows 호스트에서 검증했으며 비Windows 및 모바일 실기는 실행하지 않았다. Godot의 기존 모든 추가 fixture를 재실행한 수치는 아니다.

## 성능: 작은 시간 증가 관측이 남음

.NET 8.0.31 Release, TieredCompilation=0, 반복 10만 회, 워밍업 10회, 측정 15회다. 최초 3쌍에서 일반 생성 시간 증가가 관찰되어 시작 순서를 반대로 한 3쌍을 추가했다. 총 6쌍이며 빌드/회귀 실행과 겹치지 않았다. 반환 합계도 검사했다.

아래는 프로세스별 중앙값 6개의 중앙값이다. 단위 ms.

| workload | 이전 | 변경 후 | 변화 | 양쪽 할당량 |
|---|---:|---:|---:|---:|
| os_error_fallback | 74.52985 | 75.29455 | +1.03% | 25,904,120 B |
| os_error_construction | 100.12805 | 102.55915 | +2.43% | 41,104,040 B |

fallback의 프로세스 중앙값 범위는 이전 73.5534–78.6490, 이후 73.0406–77.2086 ms이며 쌍별 변화 방향이 섞였다. 일반 생성 범위는 이전 97.6809–103.5594, 이후 98.6549–105.0701 ms로 겹치지만 6쌍 중 5쌍에서 증가했다. 범위가 겹친다는 이유만으로 회귀가 없다고 결론 내리지 않는다.

일반 생성 workload는 한 문자열 인자로 OSError와 FileNotFoundError를 생성하므로 새 errno 비교에 들어가지 않는다. 따라서 추가 비교만으로 증가를 설명할 수 없다. 이 회차는 호환성 확장이며 속도 개선을 주장하지 않는다. 할당은 동일하고 시간 증가의 원인은 미확정이다.

[전체 출력과 성능 표본](round81-validation.json)을 보존한다. 이전 79차에서 재현되지 않았던 시간 차이와 이번 관측을 동일한 원인으로 단정하지 않는다.

## 다음 순서

다음 확장 전에 생성·타입 호출·builtin 조회 비용을 분리해 이번 시간 증가를 조사한다. 타입 선택 자체의 선형 비교가 늘어나는 비용도 함께 검토하되, 측정 근거 없는 캐시나 사전 교체는 추가하지 않는다.

호환성 후속은 BlockingIOError의 characters_written 규칙과 남은 errno 하위 타입이다. `os_error_type_selection.py`에서는 해결한 20/21 및 winerror 267을 제거하고 남은 차이를 보존했다. 이 파일의 출력 불일치와 `blocking_io_arguments.py`의 미지원 타입 실패를 실제 실행으로 확인했으며, 두 파일은 기본 통과 목록에 넣지 않았다.
