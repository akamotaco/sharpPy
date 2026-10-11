# 88차: dict 키 객체 동일성

87차를 `9629fe0`으로 커밋한 뒤 진행했다. 이번 변경은 아직 커밋하지 않았다.

## 근거와 변경

고정 CPython v3.12.15 (`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`)의 `Objects/dictobject.c:983` 일반 키 조회는 저장된 키와 조회 키의 포인터가 같으면 동등 비교 없이 찾은 것으로 처리한다. 다른 객체인 경우 저장된 전체 해시를 확인하고 `PyObject_RichCompareBool`을 호출한다.

SharpPy의 `PyDict.PyObjectEqualityComparer.Equals`는 같은 객체에도 `RichCompare`를 호출했다. 그 결과 자기 자신과 같지 않은 NaN 키 및 예외를 던지는 사용자 `__eq__`에서 조회가 실패한다. 수정 전 DLL로 새 회귀를 실행하면 첫 membership 검사부터 AssertionError가 발생하며, 실제 CPython 3.12.15에서는 통과한다.

기존 양쪽 null 검사를 `ReferenceEquals(x, y)`로 교체했다. 같은 객체의 불필요한 비교를 생략하며 새로운 저장소·캐시·객체 할당을 추가하지 않는다. 서로 다른 객체의 비교 경로는 유지한다. 이 변경은 키 해시 계산 자체를 생략하지 않는다.

## 검증

`tests/dict_key_identity_regression.py`를 기본 평가에 등록했다. 같은 키의 membership/getitem/get/setdefault, 덮어쓰기, values/items, copy/update, pop/del/popitem을 검사한다. 사용자 비교 예외와 callback 횟수 0, NaN 및 일반 int/str 키를 확인한다. 서로 다른 NaN 두 개는 별도 키로 유지되어야 한다.

- Release 빌드 성공. 기존 TokensReader nullable 경고 1개, 오류 0개.
- 전체 154개: 출력 완전 일치 149개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot 소스 직접 포함 빌드 성공. 기본 17개와 관련 10개, 총 27개 fixture 통과. 관련 fixture는 새 CPython 실행 출력과 대조했다.
- 최종 DLL SHA256과 전체 평가에 기록된 SHA256 일치 확인.
- Windows x64/.NET 8.0.31에서 실행했다. 비Windows·모바일 실행 결과는 포함하지 않는다.

## 성능

동일한 기존 workload와 checksum 검사를 사용했다. Release, TieredCompilation=0, 100,000회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행. 빌드·기능 검사 종료 후 측정했다. 표의 시간은 각 프로세스 중앙값 세 개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 이전 할당 중앙값 B | 이후 할당 중앙값 B |
|---|---:|---:|---:|---:|---:|
| dictionary | 96.3208 | 95.5729 | -0.78% | 30,858,152 | 30,855,912 |
| dictionary_iteration | 90.7239 | 90.6457 | -0.09% | 26,858,544 | 26,859,904 |

첫 workload는 별도 range 반복에서 만든 정수 키로 조회하고, 두 번째는 dict가 보관한 키로 순회·조회한다. 둘 다 수정 전후 올바른 결과를 내는 작업이다. 프로세스 시간 범위가 겹치므로 속도 개선을 주장하지 않는다. 할당 표본에도 작은 변동이 있으며 전후 최소값은 각각 30,854,592 B / 26,854,584 B로 같다. 할당 감소의 근거로 사용하지 않는다.

[실행 출력·환경·전체 표본](round88-validation.json)

## 구조적 후속 과제

이번 객체 동일성 수정은 dict 해시·비교 프로토콜 전체를 해결하지 않는다. `tests/compatibility/int_subclass_hash_protocol.py`의 사용자 해시/동등 비교 누락은 그대로 후속 대상이다.

1. 수치 키의 동등 값에 같은 해시를 부여하는 계약부터 회귀로 고정한다. 현재 PyInt는 CLR BigInteger 해시, PyFloat는 int32 범위의 정수값만 별도로 처리하고 나머지는 double CLR 해시를 사용한다. 정수 하위 타입만 Python 해시로 전환하면 기존 수치 키 간 조회를 깨뜨릴 수 있다.
2. 저장 시 계산한 Python 전체 폭 해시와 키 참조를 보존하는 구조를 검토한다. CLR comparer의 32-bit 해시로만 축소하면 서로 다른 Python 해시 사이에 불필요한 사용자 동등 비교가 생길 수 있다. 비교 시 사용자 해시를 다시 계산하는 방식도 callback 횟수를 바꾸므로 부적절하다.
3. 현재 `_dict`와 `_keys` 이중 저장소, `InternalDict` 직접 소비 및 copy/values/items/repr의 키 재조회 경로를 함께 조사한다. 저장된 키를 다시 조회할 때 해시 callback을 추가하는 국소 수정으로 확대하지 않는다.
4. 다른 객체의 비교에는 하위 타입 우선순위, NotImplemented, 임의 반환값의 진리 판정, 비교 중 dict 변경이 남는다. 실제 CPython의 callback 순서와 재진입 결과를 확인한 뒤 저장소 변경을 선택한다.

큰 문자열 정수 하위 타입 파싱 및 85→86 일반 예외 생성 시간 증가의 원인도 여전히 미해결이다.
