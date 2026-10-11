# 86차: errno 정수 하위 타입의 해시·동등 비교

85차를 `121e21c`로 커밋했다. 이번 변경은 OSError의 errno가 Python int 하위 타입일 때 CPython의 errnomap 조회 동작을 구현한다. 일반 PyInt의 기존 선택 분기는 유지한다. 새 변경은 미커밋 상태다.

## CPython 소스와 실제 실행으로 확인한 규칙

CPython 3.12.15 고정 소스 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`에서 다음 경로를 확인했다.

- `Objects/exceptions.c`: OSError_new → PyDict_GetItemWithError(errnomap, myerrno).
- `Objects/typeobject.c`: slot_tp_hash, slot_tp_richcompare, lookup_maybe_method, slot_nb_bool, slot_sq_length, inherit_slots/type_ready_set_hash.
- `Objects/longobject.c`: long_hash의 부호·modulus·-1 처리. C의 digit 회전 루프를 복제하지 않고 기존 BigInteger 나머지 연산을 사용했다.
- Python 정수의 numeric hash는 native signed 폭을 사용한다. 사용자 __hash__ 반환값이 그 범위 안이면 그대로 사용하고, 범위를 넘을 때만 정수 hash로 축약한다. -1은 -2로 바꾼다.

errno의 내부 값만 읽어 타입을 고르면 잘못된다. 값 999의 하위 타입이라도 hash=2이고 __eq__(2)가 참이면 FileNotFoundError가 된다. hash가 등록되지 않은 값이면 __eq__를 호출하지 않는다. __eq__의 NotImplemented는 기본 정수 비교로 이어진다. __int__/__index__로 errno를 변환하지 않는다.

특히 descriptor 바인딩 오류와 실제 메서드 실행 오류가 다르다. CPython slot_tp_hash는 바인딩 실패를 unhashable TypeError로 대체한다. slot_tp_richcompare는 바인딩 실패를 지우고 NotImplemented로 넘긴다. 반면 바인딩된 메서드 본문에서 발생한 오류는 그대로 전파한다. AttributeError뿐 아니라 ValueError 바인딩 실패도 실제 실행으로 확인했다. 일반적인 descriptor 오류 전파 규칙을 이 두 슬롯에 일괄 적용하지 않았다.

## 구현과 비용 범위

errno 하위 타입에만 별도 경로를 연결했다. 작은 양의 정수인 errnomap key는 모두 서로 다른 해시를 가지므로, native-width hash로 해당 타입을 찾은 뒤 동등 비교를 최대 한 번 실행하면 관찰 가능한 동작을 보존할 수 있다. CPython 사전의 슬롯 배열·충돌 탐색 순서를 복제하지 않았다.

하위 타입 전용 .NET Dictionary는 native long을 키로 삼고, 대응하는 PyInt 키 객체와 예외 타입을 값으로 보관한다. 최초 해당 조회 때 생성하고 재사용한다. 일반 정수 경로에는 이 사전 조회나 사용자 특수 메서드 바인딩을 넣지 않았다. 빠른 경로와 하위 타입 테이블이 같은 errno 집합을 유지하도록 회귀 파일에서 모든 지원 플랫폼의 관련 번호를 일반 int/하위 타입으로 검사한다.

PyInt 키 객체를 보존하는 것도 관찰 가능한 동작이다. 사용자 __eq__가 전달받은 키를 저장했다가 다음 호출과 `is`로 비교하면 CPython에서는 같은 객체다. Winsock처럼 작은 정수 캐시 밖인 키를 매번 만드는 중간 구현의 불일치를 재현했고, 테이블 초기화 때 한 번 만들어 재사용하도록 수정했다. 이것은 불필요한 C 내부 구조 복제가 아니라 callback에 노출되는 객체 동일성을 보존하는 조치이며, 이 키의 반복 생성도 제거한다.

필요한 기반 수정도 포함한다.

- 기존 PyWrapperDescriptor로 int.__hash__와 native-width 정수 해시 계산을 연결했다. 수신 객체·키워드·위치 인자 검사 순서와 오류 문구도 실제 CPython에 맞췄다. __hash__ 반환값이 int 하위 타입이어도 그 객체의 __hash__/__index__를 재호출하지 않는다.
- 클래스 생성 시 __eq__가 있고 __hash__가 없으면 __hash__=None을 넣는다. 조회 때 이를 추론하지 않으므로 생성 후 __eq__ 대입은 기존 해시를 유지한다.
- builtin __hash__를 자식 TypeDict에 복사하지 않고 MRO를 통해 찾는다. 복사하면 `class C(Mixin, int)`의 Mixin 해시나 hash=None을 가리는 문제가 있다.
- 비교 결과의 진리값 검사를 위해 PyObject.IsTrue를 타입 특수 메서드 조회로 정리했다. __bool__은 bool만 허용하고, __len__은 index 변환·음수·native 크기 초과를 검증한다. 인스턴스에만 넣은 __bool__은 무시하고 실행/바인딩 오류를 전파한다. native 타입의 기존 IsTrue override는 유지된다.

전체 hash() builtin, dict/set의 CLR 해시·동등 비교 구현을 이번에 통일한 것은 아니다. 지원 여부는 이번 errno 조회와 회귀 범위로 한정한다. 정수 하위 타입 전용 경로에는 필요한 메서드 조회·바인딩 비용이 있으며 이를 이미 구현돼 있던 빠른 경로라고 표현하지 않는다.

## 검증

`tests/os_error_errno_subclass_regression.py`를 기본 평가에 추가하고 기존 실패 재현 `tests/compatibility/os_error_int_subclass.py`를 통합했다.

- 모든 관련 errno, 큰 정수, bool/잘못된 hash 반환값, native-width 경계와 overflow 축약.
- 사용자 hash/eq 호출 순서, 예외, NotImplemented, 반환값의 진리값 및 오류.
- hash=None, __eq__만 정의한 클래스, 상속, int.__hash__ 명시 복원, 다중 상속 MRO.
- staticmethod/classmethod/사용자 descriptor, 바인딩 오류와 실행 오류의 구분.
- 인스턴스 메서드 무시, 클래스 메서드의 대입·삭제, hash callback 중 eq 변경.
- args·errno 객체 identity, 파일명, 직접 하위 예외 생성 시 선택 생략, kwargs 우선순위, winerror 정규화 우선순위.
- 반복 동등 비교에 전달되는 키 객체의 identity. 중간 구현에서 두 번째 조회가 실패하는 재현도 검증 JSON에 보존했다.

수정 전 DLL에서 새 회귀는 실패하고, 수정 후 실제 CPython 3.12.15와 출력이 일치한다. Release 빌드와 전체 152개 평가 통과: 완전 출력 일치 147개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개다.

Godot는 SharpPy 소스를 직접 포함해 빌드했다. 기본 17개와 OSError 관련 8개, 총 25개 fixture를 통과했고 관련 8개의 출력은 새 CPython 실행과 직접 대조했다. 별도 SharpPy DLL이나 외부 native 라이브러리를 추가하지 않았다. 검증 호스트는 Windows x64/.NET 8.0.31이며 모바일 실기·비Windows·32-bit 실행은 하지 않았다.

## 성능 대조

Windows .NET 8.0.31 Release, TieredCompilation=0. 기존 네 workload를 10만 회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행했다. 빌드·기능 테스트와 겹치지 않았다. 이전에 실패하던 사용자 해시 선택을 속도 개선의 비교 대상으로 쓰지 않았다. 아래 값은 프로세스 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 변화 | 양쪽 할당량 |
|---|---:|---:|---:|---:|
| os_error_fallback | 77.4764 | 76.9942 | -0.62% | 25,904,120 B |
| os_error_construction | 104.2709 | 105.7516 | +1.42% | 41,104,040 B |
| os_error_winerror | 96.8136 | 96.4778 | -0.35% | 41,104,120 B |
| int_subclass_construction | 548.0769 | 555.8514 | +1.42% | 124,304,032 B |

[검증 출력과 측정 표본](round86-validation.json)

네 workload 모두 할당량이 동일하다. 일반 OSError 생성과 정수 하위 타입 생성은 최종 표본에서 각각 약 1.42% 증가했다. 네 workload 모두 프로세스 중앙값 범위가 겹치지만 이 관측을 단순 잡음으로 지우지 않으며 원인은 미확정이다. 감소한 두 workload도 안정적인 속도 향상으로 주장하지 않는다.

중간 후보에서 winerror +3.33%·정수 하위 타입 생성 +2.18%가 관찰되어 두 경로는 각각 3쌍을 추가 측정했다. 이후 int.__hash__ descriptor의 인자 검사를 보완한 단계에서는 일반 생성 +1.88%, 정수 하위 타입 생성 -2.19%가 관찰됐다. 마지막 키 객체 재사용 수정 후 네 workload 전체를 다시 측정했다. 위 표는 최종 코드의 표본만 사용하며 앞선 두 단계의 표본은 별도 항목으로 보존한다. 어느 단계의 감소도 CPython 동작 구현 자체의 속도 향상 근거로 사용하지 않는다.

## 다음 작업

일반 OSError 생성의 증가를 직접 생성·builtin 조회·VM 호출로 분리해 조사한다. 기본 정수 해시 descriptor의 임시 바인딩·반환 객체 비용도 후속 측정 대상이다.

일반 hash()/dict 조회가 정수 하위 타입의 사용자 hash/eq를 무시하는 기존 문제는 `tests/compatibility/int_subclass_hash_protocol.py`로 보존했다. 수정 전후 DLL 모두 실제 CPython과 다르며 기본 통과 목록에 포함하지 않았다. 다음 호환성 작업은 이 공통 경로이며, 큰 문자열에서 int 하위 타입 생성이 실패하는 `tests/compatibility/int_subclass_large_string.py`도 재확인해 남겼다.
