# 5차 locals 관찰 호환성과 cell 삭제

기준일: 2026-10-09. 기준 커밋은 `f742c29`. 이 보고서는 5·6차 구현과 함께 커밋한다.

## 실제 실행으로 확인한 문제

fast locals와 scope dictionary의 중복 저장을 줄이기 전에 실제 CPython 3.12.15와 비교했다. SharpPy에서는 매개변수 재할당 후 `locals()`가 이전 값을 반환하고 새 지역 변수와 삭제를 반영하지 않았다. `f_locals`는 fast locals를 수집한 뒤 scope의 오래된 값으로 덮어썼다. closure의 free variable도 두 경로에서 누락됐다.

새 테스트를 기존 바이너리에 실행하면 CPython은 통과하고 SharpPy는 실패한다. 이 차이를 수정한 뒤 같은 파일을 다시 비교했다. 테스트는 다음 동작을 assertion으로 확인한다.

- [locals 관찰 회귀](../../../tests/locals_observation_regression.py): 재할당·신규 변수·삭제, 반복 조회 시 dictionary identity, 조회 전 snapshot과 조회 시 갱신, dictionary 수정이 fast locals에 직접 반영되지 않음, 사용자 추가 키 유지, closure 변수, 반환 후 frame 참조, generator 중단·재개, 매개변수 종류, 호출 간 독립성.
- [cell 삭제 회귀](../../../tests/cell_delete_regression.py): captured local을 삭제한 뒤 closure에서 읽으면 NameError가 발생함.

cell 삭제 테스트를 추가하면서 컴파일러가 captured local에 `DELETE_FAST`를 생성하는 오류도 발견했다. 해당 변수는 cell에 저장되므로 `DELETE_DEREF`를 생성해야 한다. 기존 cell offset 보정 경로를 사용하도록 수정했다.

## CPython 소스 비교

공식 v3.12.0 및 v3.12.15 소스를 commit으로 고정하고 hash를 기록했다. 이번에 `Objects/frameobject.c`와 `Python/bltinmodule.c`를 참조 다운로드 목록에 추가했다. 출처는 [manifest](round5-cpython-manifest.json)에 보존했다.

- [v3.12.15 frameobject.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/frameobject.c): `_PyFrame_GetLocals`는 frame의 dictionary를 필요 시 생성하고 fast locals/cells를 갱신한다. 비어 있는 값의 키는 삭제한다. 알려진 변수 밖의 사용자 추가 키를 전부 지우지 않는다.
- [v3.12.15 bltinmodule.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/bltinmodule.c): `builtin_locals_impl`의 locals 조회 경로를 확인했다.
- [v3.12.15 compile.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/compile.c): dereference 대상의 Del 연산은 `DELETE_DEREF`를 생성하고 이후 offset을 보정한다.

두 patch 버전의 소스 전체가 동일하다고 가정하지 않았다. 해당 함수의 의미를 확인하고 실제 실행 기준은 설치된 3.12.15로 고정했다. Python 3.13 이후 locals semantics를 적용하지 않는다.

## 수정 범위와 비용

최적화 함수 frame에 lazy locals dictionary를 추가했다. `locals()`와 `f_locals`는 같은 갱신 경로를 사용한다. 값의 기준은 fast locals와 cells이며 scope의 오래된 매개변수 값은 덮어쓰지 않는다. dictionary는 관찰 시에만 생성하고 frame은 재사용하지 않는다.

매개변수 scope 저장은 아직 제거하지 않았다. module/class namespace 경로, tracing에 의한 locals writeback, inlined comprehension의 hidden locals 표현은 이번 변경의 검증 범위 밖이다. CPython `_PyFrame_GetLocals` 전체의 호환성 완료를 뜻하지 않는다.

참조 필드 추가로 frame당 8B가 증가했다. 기존 할당 probe를 같은 1,000회 조건으로 비교했다. 빈 함수 frame은 368→376B, 두 인자 frame은 544→552B, 직접 빈 함수 호출은 672→680B, 직접 두 인자 호출은 888→896B다. scope와 인자 배열의 비용은 동일했다. 관찰 시 dictionary/key 할당 비용은 이 probe의 측정 대상이 아니다. 이번 단계는 호환성 수정이며 속도 향상을 주장하지 않는다.

## 검증 결과와 다음 작업

Release와 Debug 빌드 성공. 최종 비교 33개 항목은 출력 일치 27개, 기존 출력 차이 4개, native 테스트 실패 보고 없음 2개다. 새 회귀 파일 두 개는 변경 전 SharpPy에서 실패하고 최종 구현에서 CPython과 출력이 일치했다. Debug에서도 locals 관찰 회귀가 통과했다.

원자료: [변경 전 회귀](round5-regressions-before.json), [최종 비교](round5-tests.json), [할당 probe](round5-allocations.json).

다음 단계에서는 scope dictionary를 읽는 호출·class body·exec/eval 경로를 조사한다. 관찰 경로를 fast locals로 바꿨다는 사실만으로 나머지 중복 저장을 안전하게 제거할 수 있다고 가정하지 않는다. class/module 및 hidden locals 회귀 사례를 먼저 늘린다.

```powershell
& $referencePython tools/evaluate.py --dotnet "$taskDotnet\dotnet.exe" --files tests/locals_observation_regression.py tests/cell_delete_regression.py
```
