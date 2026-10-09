# 21차: 기존 출력 차이 네 항목의 원인과 수정

기존 차이를 유지해야 하는 성능상 근거는 없었다. 기존 회귀와 신규 회귀를 구분한 상태를 반복 보고하면서 실제 오류와 비교 방법의 한계를 함께 묶어 둔 것이 문제였다. 이번에는 네 파일의 raw diff를 각각 확인했다.

| 파일 | 원인 | 처리 |
| --- | --- | --- |
| tests/level7_comprehensions.py | set 표시 순서만 다름 | 실제 set 동등성을 assert하고 출력은 sorted로 결정적으로 변경. 런타임 set 순서를 바꾼 것은 아님 |
| test_python312_missing_features.py | bound method의 함수 속성 누락, memoryview 쓰기가 원본에 반영되지 않음, 주소 차이 | 기능 오류 수정. 메모리 주소만 별도 volatile 필드로 비교 |
| test_ultimate_complex_features.py | type 생성 시 무조건 DEBUG 출력 68행, 시각·경과 시간 차이 | 임시 로그 제거. 지정한 시간 필드만 별도 비교 |
| examples/advanced.py | 실제 실행 시간 차이 | 지정한 Execution time 필드만 별도 비교 |

## 런타임 수정

`PyMethod.GetAttribute`는 method 자체 속성/descriptor를 먼저 처리하고 그 밖의 속성을 underlying function에서 읽는다. [CPython method_getattro](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/classobject.c#L179)를 기준으로 `__override__` 및 사용자 함수 속성의 최신 값/참조를 검사했다. 호출 경로는 변경하지 않았다.

`PyMemoryView`는 bytearray의 List 저장소를 직접 참조한다. 슬라이스는 동일 저장소와 offset/length/stride를 보관하며 bytes의 읽기 전용 상태도 유지한다. [CPython memory_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/memoryobject.c#L2547)와 integer 반환 경로를 확인했다. 바이트 읽기는 기존 `SmallIntCache`를 재사용한다. 신규 캐시는 없다. 공개 C# `Data` 접근자는 연속 byte[] 이외의 경우 materialize하며 Python indexing/slicing은 이 접근자를 사용하지 않는다.

bytearray의 정수 및 같은 크기 슬라이스 쓰기는 export 중에도 허용하고, 크기가 바뀌는 쓰기는 계속 거부한다. 검사 중 발견한 `GetIndices`(선택된 인덱스 배열)를 `(start, stop, step)`으로 잘못 해석한 오류는 `Indices`로 수정했다. 기존 bytearray 삭제 구현을 Python descriptor에 연결했다.

슬라이스 대입 컴파일의 두 경로는 경계 생략을 None으로 처리하고 step이 있으면 BUILD_SLICE/STORE_SUBSCR 경로를 사용한다. [CPython compiler_subscript](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Python/compile.c#L6496)의 분기와 비교했다. 이전에는 생략된 경계에서 null 참조 오류가 나거나 step이 무시됐다.

## 평가 방법과 검증

`tools/evaluate.py`는 원본 stdout/stderr를 그대로 보관한다. 세 파일의 지정된 출력 행에서 시간/주소 필드만 다르면 `matching_except_volatile_fields`로 분류한다. 일반 `matching_output`과 구분한다. 시간은 유한한 비음수, 주소는 0이 아닌 hex 형식인지 확인한다. 의미 있는 값 차이, stderr 차이 및 실패 마커는 이 분류로 숨기지 않는다. 설명되지 않은 `output_difference`도 이제 evaluator 실패 exit code에 포함한다.

정규화 검사 3개는 파일 범위, 허용된 필드, 잘못된 숫자/주소, 기능 결과가 보존되는지 검증했다. set 순서는 일반 정규화하지 않는다. 해당 회귀의 set equality assertion과 정렬 출력만 변경했다.

CPython 3.12.15와 신규 bound method 및 memoryview alias 회귀를 비교했다. 기존 DLL은 두 기본 재현 모두 실패했고 변경 후 통과한다. 원본↔view 변경, 중첩/역방향/step slice, 읽기 전용, export 중 resize 금지, bytearray slice 조회·쓰기·삭제, 인자 오류를 검사했다.

전체 57개 결과: raw 출력 일치 52, 지정된 volatile 필드를 제외하면 일치 3, native no_reported_failure 2. 설명되지 않은 출력 차이와 새 실패는 없다. 마지막 descriptor 인자 검사 추가 후에는 영향을 받는 memoryview/bytearray 회귀를 재실행해 통과했다. Release 본체/harness 빌드 및 evaluator 단위 검사 성공.

이는 모든 Python 기능이 일치한다는 뜻은 아니다. 예를 들어 기존 memoryview의 release/export 수명 관리, multidimensional/format 및 slice assignment 전체 구현은 이번 범위에 포함하지 않았다. module 이중 저장소와 dict cursor resize의 별도 실패 fixture도 남아 있다.

## 비용 확인

동일 native harness에 기준 commit `38f6956` DLL과 최종 DLL을 교체해 3쌍을 번갈아 실행했다. 각 process는 10 warmup/10 sample이며 준비·build·다른 테스트 실행을 제외했다. 버퍼 크기는 1,024바이트다. 읽기 결과와 생성된 view 길이를 검증한다. 의미 있는 쓰기 정합성은 위 Python 회귀에서 검증한다.

| 작업 | 전 중앙값 ms (Q1–Q3) | 후 중앙값 ms (Q1–Q3) | 전 → 후 할당 B |
| --- | ---: | ---: | ---: |
| 생성 10,000회 | 0.27470 (0.2689–0.2876) | 0.36920 (0.2395–0.5396) | 10,880,000 → 560,000 |
| slice 10,000회 | 5.32085 (5.1364–7.0165) | 0.83380 (0.8224–0.8737) | 10,880,000 → 560,000 |
| 읽기 100,000회 | 2.09310 (1.9780–2.3300) | 1.86820 (1.8617–1.8997) | 4,000,000 → 0 |

slice 시간은 약 84.3%, 생성/slice 할당은 약 94.9% 감소했다. 생성 시간 중앙값은 증가했지만 분포가 겹치고 변동이 크므로 생성 속도 개선이나 동일성을 확정하지 않는다. 모든 크기의 view 비용이 감소했다는 의미도 아니다. view 객체 자체는 slice 위치 정보로 16B 커졌으며, 이번 크기에서는 버퍼 복사 제거 효과가 더 크다.

중간 구현에서는 interface 저장소 읽기를 추가하고 매번 PyInt를 생성해 읽기 시간이 증가했다. 이 결과도 보존했다. CPython과 동일한 작은 정수 반환 방식을 적용한 최종 구현은 위와 같다. Python VM 전체 속도 향상은 이 native 측정에서 추론하지 않는다.

[원래 네 diff, 측정 sample, DLL hash, 테스트 분류](round21-output-gaps.json). 전체 raw stdout은 `.artifacts/round21-tests-final.json`에 보관한다.
