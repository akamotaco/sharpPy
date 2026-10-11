# PermissionError와 FileExistsError 선택 — 78차

작업 시작 시 77차는 `0688409`로 커밋되어 `work/oserror-compatibility` 브랜치에 푸시된 상태였다. 이번 변경은 작업 트리에 남긴다.

## 수정

PermissionError와 FileExistsError를 OSError 하위 타입으로 등록하고 생성자와 builtins에 연결했다. 기존 OSError의 인자·전용 속성 저장 구조를 상속하며 추가 인스턴스 필드는 없다.

OSError의 인자가 2~5개이고 errno가 정수이면 1/13은 PermissionError, 17은 FileExistsError를 선택한다. 기존 errno 2의 FileNotFoundError 선택은 유지한다. bool은 CPython에서 int 하위 타입이므로 True는 errno 1로 선택하되 args와 errno에는 원래 True를 보존한다. 직접 하위 타입을 생성하면 다른 errno를 주어도 해당 타입을 유지한다. 문자열·float·__index__ 객체를 정수로 강제 변환하지 않는다. 큰 정수도 좁은 정수로 변환하지 않아 overflow를 만들지 않는다.

참조는 CPython 3.12.15와 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/exceptions.c:OSError_new, 하위 타입 정의, errnomap의 EPERM/EACCES/EEXIST 등록이다. 실제 Windows CPython 실행으로 비교했다. 나머지 errno 타입·Windows 오류 코드 변환까지 완료한 것은 아니다.

## 검증

- .NET Release 빌드 성공.
- 기본 146개: 완전 출력 일치 141개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- os_error_subtype_regression.py: builtin identity, errno별 타입, 직접 생성, 키워드 거부, 상속 관계, isinstance/issubclass, except 분기, 파일명, 인자 개수 경계, bool·float·문자열·음수·큰 정수·__index__ 미호출을 비교했다. 이전 DLL에서는 실패한다.
- 최종 코드에서 하위 타입과 OSError 인자 회귀 2개를 다시 비교했다.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper, 추가 58개 출력 비교 통과. 합계 75개 fixture. 기존 traceback 1개는 컴파일 입력 파일명만 res://로 바꾸어 비교했다. 별도 SharpPy DLL 의존성은 추가하지 않았다. Windows 호스트 검증이며 모바일 실기와 비Windows 실행은 검증하지 않았다.
- CFG reachability probe와 git diff --check 통과.

원본 요약은 [round78-validation.json](round78-validation.json), 상세 로그는 `.artifacts/round78-full.json`, `round78-final-targeted.json`, `round78-godot-comparison.json`에 보존했다.

## 성능: 시간 증가 관측이 남아 있음

이전과 결과가 같은 OSError(999, "message") 생성 workload를 추가해 새 선택 분기를 통과하지만 일반 OSError로 남는 경로를 측정했다. 한 문자열 인자로 OSError와 FileNotFoundError를 각각 생성하는 기존 workload도 대조했다.

.NET 8.0.31, TieredCompilation=0, 반복 1만 회, warmup 10회·측정 10회다. 처음 교대 3쌍에서 시간 증가가 관측되어 시작 순서를 반대로 한 3쌍을 추가했다. 총 6쌍이며 빌드·기능 검증과 측정은 겹치지 않았다. workload 반환 합계 검증을 통과했다. 시간은 프로세스 중앙값 6개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| OSError errno 999, 1만 회 | 6.962 | 7.293 | 2,481,272 | 2,481,272 |
| 한 문자열 인자 두 타입 각 1만 회 | 9.312 | 9.800 | 4,001,192 | 4,001,192 |

할당은 동일하지만 시간은 각각 약 4.76%, 5.25% 증가했다. 프로세스 중앙값 범위는 첫 workload 이전 6.813–7.046 ms/이후 6.988–7.499 ms, 두 번째 이전 9.090–9.892 ms/이후 9.605–9.986 ms다. 일부 겹치더라도 반복 측정에서 증가가 남았으므로 이를 단순 잡음으로 처리하거나 성능 개선으로 주장하지 않는다.

한 인자 경로는 errno 선택 분기에 들어가지 않아 추가 정수 비교만으로 증가를 설명할 수 없다. 타입별 CreateInstance 분리 대조에서도 회복되지 않았다(단일 쌍: errno 999 이전 6.890/이후 7.134 ms, 한 인자 이전 9.228/이후 9.847 ms). 추가 구조를 유지할 근거가 없어 최종 구현은 기존 PyType 생성 분기 방식을 따른다. 측정 로그는 `.artifacts/round78-dispatch-*.json`에 남겼다. 원인은 확정하지 않았다.

## 다음 순서

1. **추가 호환성 확장에 앞서 이번 시간 증가의 원인을 우선 분리한다.** builtin 이름 등록에 따른 조회 경로와 생성 분기 비용을 구분하고, 정상 결과를 유지하며 회귀를 줄일 방법을 확인해야 한다.
2. os_error_type_selection.py의 errno 13/17 출력은 일치하게 됐다. 정수 winerror→errno 변환은 계속 다르며 실패 fixture로 유지한다. 이후 CPython의 플랫폼별 변환 규칙을 확인한다.
3. 다른 OSError 하위 타입·BlockingIOError.characters_written, 사용자 정의 하위 클래스와 기존 후속 항목은 유지한다. 이번 수정으로 전체 OSError API 호환성이나 성능 문제가 완료되었다고 보지 않는다.
