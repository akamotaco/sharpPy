# 38차: args iterable의 길이 힌트

37차는 `4869535`로 커밋했다. 일반 iterable을 예외 args로 변환할 때 빠져 있던 길이 힌트 callback을 구현했다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/abstract.c `PyObject_LengthHint`, `PySequence_Tuple`, `PyObject_GetIter`를 대조했다. iterator를 얻은 뒤 원본 객체의 __len__을 평가한다. 길이가 없거나 TypeError가 발생하면 __length_hint__로 진행한다. 힌트 호출의 TypeError 및 NotImplemented는 기본값을 사용하지만, descriptor 바인딩 오류·잘못된 반환형·음수·다른 예외는 전파한다.

길이의 반환값은 __index__를 허용하고 힌트의 반환값은 정수여야 한다. 음수인 __len__은 초대형 음수도 ValueError지만, __length_hint__의 초대형 음수는 인덱스 크기 변환의 OverflowError가 우선한다. native 정수 크기는 nint로 판단한다. 이 규칙들을 실제 CPython 실행과 비교했다.

변환 실패는 args를 교체하지 않는다. 테스트 중 기존 사용자 클래스 GetIterator가 __iter__의 RuntimeError를 TypeError로 감싸는 문제도 발견하여 Python 예외는 그대로 전달하도록 수정했다.

## 검증

새 회귀는 호출 순서, 성공한 len의 힌트 생략, TypeError fallback, NotImplemented, 부정확한 작은/큰 힌트, bool, 음수·초대형 정수·실수, __index__ 반환, 힌트 descriptor 실패와 iterator 실패 우선순위를 검사한다. 기준 DLL은 새 회귀와 기존 최소 재현에 실패하고 최종 DLL은 실제 CPython과 일치한다. 최초 전체 실행에서 드러난 iterator 예외 변환 문제를 수정한 뒤 전체 검사를 다시 수행했다.

Release 빌드 성공. 기본 81개는 raw 일치 76, 지정 변동 필드만 다른 3, native no_reported_failure 2다. Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드와 선별 회귀 11개 통과. 별도 SharpPy DLL 참조 없이 기존 GODOT 심볼 및 DEBUG 제한 제외 조건을 유지했다. [전후 실행·소스 hash·Godot 로그](round38-length-hint.json)를 보존했다.

## 할당과 범위

유효한 힌트는 List 초기 용량으로 사용한다. 힌트가 없으면 CLR List의 기본 확장 방식을 사용하며 CPython의 초기 tuple 용량 10을 복제하지 않는다. 새 외부 의존성은 없고 성능 수치는 측정하지 않았다. CLR 배열의 실제 길이 한계를 넘는 예약 요청은 MemoryError로 처리한다. 최대 용량 근처의 실제 대규모 할당 및 다른 런타임의 자원 고갈 동작까지 CPython과 같다고 주장하지 않는다.

새 내부 LengthHint는 현재 args 변환에서 사용한다. 기존 `_operator.length_hint`는 아직 모든 오류를 기본값으로 처리하는 별도 구현이다. `tests/compatibility/operator_length_hint.py`에서 CPython 통과·SharpPy 실패를 확인했으며 기본 81개에는 포함하지 않았다. 다음은 이 공개 API의 기본값 검증과 공통 경로 연결이다.
