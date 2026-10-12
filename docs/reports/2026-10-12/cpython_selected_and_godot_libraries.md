# 105차: CPython 선별 테스트와 Godot 라이브러리 조합

## 검증 범위

고정 CPython v3.12.15 (`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`)의
`Lib/test/test_int.py`, `Lib/test/test_sort.py`에서 의존성이 작은 테스트
메서드 10개를 선별했다. 함수 본문 AST와 기대 예외는 바꾸지 않고 unittest
runner만 작은 assertEqual/assertRaises helper로 대체했다. 전체 두 모듈이나
CPython unittest runner를 통과한 것으로 표현하지 않는다.

선별 목록·출처 URL·SHA256은 `tests/cpython312_selected_manifest.json`,
upstream 라이선스는 `tests/cpython312_LICENSE.txt`에 있다.
`tools/extract_cpython_tests.py`는 source hash가 다르면 생성을 거부한다.
초기 변형된 소스를 현재 고정 기준으로 잘못 표시하지 않도록 했다.

```powershell
& <python312> tools/extract_cpython_tests.py --source-root .artifacts/cpython-locals/v3.12.15
& <python312> tools/evaluate.py --dotnet <dotnet> --files tests/cpython312_selected_regression.py tests/library_composition_regression.py
```

조합 fixture는 itertools chain/islice/accumulate, functools.reduce,
operator.itemgetter/add/mul, math gcd/sqrt, stable sort, 숫자 키,
generator 소비 시점, 사용자 비교를 함께 확인한다. CPython은 자신의
표준 라이브러리, SharpPy는 저장소의 수정된 Lib 파일과 native C# 모듈을 사용한다.

## Godot에서 발견한 경로 문제

초기 검증 프로젝트에 functools/operator Python 파일이 없었고, 파일을
포함해도 기본 search path가 .NET assembly의 빌드 캐시에서 역산되어 있었다.
Godot 빌드는 프로젝트 리소스 `res://`, `res://Lib`, `res://modules`를 사용하도록
수정했다. sys 로드 전 import 기본 경로도 같은 factory를 사용한다.
일반 .NET은 기존 assembly 기반 경로를 유지한다.

CombinePath는 `res://` 뒤의 추가 slash를 정리하면서 protocol을 보존한다.
임시 문자열 치환을 제거해 실제 파일명 `GODOT_RES_TEMP.py`도 훼손하지 않는다.
`tools/PerformanceHarness/GodotPathProbe.cs`에 res/user/absolute 및 중복 slash
5개 조건을 보존했다. Godot 테스트 프로젝트에 해당 소스를 직접 포함해 Run을 호출했다.

Godot 프로젝트에는 저장소의 `Lib/functools.py`, `Lib/operator.py`를
`res://Lib`에 그대로 복사했다. SharpPy C# 소스를 프로젝트에 직접 포함해
컴파일·실행했으며 별도 SharpPy DLL 참조는 사용하지 않았다.
Godot export에서도 이 Python 리소스 파일의 포함 여부는 배포 구성에서 관리한다.

## 결과 및 제한

최종 기본 회귀 188개(출력 일치 183, 지정 변동 필드 3, native CLI 2).
Godot 소스 빌드 오류/경고 0, 기본 17개+관련 fixture 16개와 경로 검사 5개.
선별 CPython 메서드 10개는 fixture 1개 안에 포함되며 중복 합산하지 않는다.
전체 결과와 Godot 출력은 round105-validation.json에 보존한다.

조합 조사에서 발견한 `math.isclose` 미구현은
`tests/compatibility/math_isclose_remaining.py`로 따로 보존했다. 이 실패를
통과 fixture에서 예외를 삼켜 숨기지 않는다. 지원 중인 조합 fixture와
미지원 API fixture를 분리했으며 후자는 기본 통과 집계에 넣지 않았다.
104차 실패 12개와 이 신규 1개는 후속 과제다. 전체 표준 라이브러리 호환성,
Godot export/모바일 실기 실행 완료를 주장하지 않는다.
