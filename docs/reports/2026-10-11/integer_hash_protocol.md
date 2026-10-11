# 87차: 정수 hash() 호환성과 임시 바인딩 제거

86차를 `2b71fa8`로 커밋했다. 이번에는 Python 정수의 hash()를 CPython 3.12 규칙에 연결하고, errno와 해시 계산 경로를 공유한다. builtin 해시 wrapper의 임시 바인딩을 제거하며 작은 해시 반환값에는 기존 정수 캐시를 사용한다. 새 변경은 미커밋 상태다.

## 먼저 확인한 이전 성능 관측

이번 수정 전에 85차(`121e21c`)와 86차(`2b71fa8`) DLL을 다시 비교했다. 기존 ExceptionFactoryProbe로 직접 CreateInstance, Call, builtin 조회 후 Call을 분리했다. 각 경로는 10만 회, 워밍업 5회, 표본 10회이고 전후 3쌍을 교대 실행했다. 결과는 다음과 같다. 값은 프로세스 중앙값 3개의 중앙값이다.

| 한 인자 경로 | 85차 ms | 86차 ms | 변화 |
|---|---:|---:|---:|
| OSError 직접 생성 | 1.1983 | 1.1227 | -6.30% |
| OSError Call | 1.5856 | 1.5502 | -2.23% |
| OSError builtin 조회 + Call | 2.3950 | 2.3266 | -2.86% |
| FileNotFoundError 직접 생성 | 1.0726 | 1.0242 | -4.50% |
| FileNotFoundError Call | 1.4242 | 1.4154 | -0.61% |
| FileNotFoundError builtin 조회 + Call | 2.2656 | 2.2784 | +0.56% |

이 경로들의 할당량은 모두 12,800,000 B로 동일하다. 대조군 ValueError와 두 인자 경로도 측정해 JSON에 보존했다. 일부 대조군도 감소했으므로 위 감소를 86차의 속도 개선으로 주장하지 않는다.

반면 VM의 os_error_construction을 같은 10만 회·워밍업 10회·표본 15회·전후 3쌍으로 재측정하면 102.5972 → 105.3832 ms, 약 +2.72%였다. 85차 프로세스 중앙값 범위는 101.3005~104.4543 ms, 86차는 105.0206~106.1562 ms로 겹치지 않는다. 이 최종 대조는 새 workload 등록까지 끝낸 동일한 Python 파일로 수행했다. 직접 생성·호출·builtin 조회의 지속적인 증가로 설명되지 않아 이전 지연의 원인은 여전히 미확정이다. VM과 호스트 비용을 제외한 native probe의 수치를 VM 전체 효과로 환산하지 않는다.

## 구현과 CPython 기준

CPython 3.12.15 고정 소스 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/longobject.c` long_hash, `Objects/typeobject.c` slot_tp_hash와 실제 Python 실행을 기준으로 삼았다. hash()의 인자·keyword 오류 문구와 우선순위도 실제 실행으로 확인했다.

기존 hash()는 모든 객체의 CLR int 반환 ToHash()만 호출했다. 이는 큰 정수의 Python 해시를 보존하지 못하고 int 하위 타입의 __hash__도 무시했다. 이제 PyInt/PyBool은 native-width Python 해시를 반환하고, PyIntSubclass는 타입 MRO에서 __hash__를 찾아 호출한다. 기존 errno 전용 구현을 PyIntSubclass.GetPythonHash로 옮겨 중복 구현을 피했다.

다음 규칙을 보존한다.

- 기본 정수 해시의 modulus·부호·-1→-2 처리.
- 사용자 __hash__ 반환값이 signed native 범위 안이면 그대로 사용하고 overflow일 때만 정수 해시로 축약.
- bool/int 하위 타입 반환 허용, 그 외 반환값의 TypeError. 반환된 하위 타입의 __hash__/__index__ 재호출 금지.
- hash=None, 암시적 hash=None, MRO, 클래스 메서드 변경, 인스턴스 속성 무시.
- descriptor 바인딩 실패는 unhashable TypeError로 바꾸고, 이미 바인딩된 메서드 실행 오류는 그대로 전파.

정확히 PyWrapperDescriptor인 builtin 슬롯은 바인딩 시 사용자 코드를 실행하지 않는다. 기존 wrapper 구현에 수신 객체와 빈 인자 배열을 직접 전달하여 임시 PyBoundWrapperDescriptor를 만들지 않는다. 다른 descriptor 및 C# 파생 descriptor는 기존 바인딩 경로를 유지한다. 사용자 Python 메서드 자체를 생략하거나 결과를 캐시하지 않는다.

hash()의 작은 결과는 SmallIntCache를 사용해 새 PyInt 생성을 피한다. OSError는 기존 비교 키 identity와 eq/NotImplemented/진리값 처리 경로를 유지한다. 별도 DLL, native 의존성이나 인스턴스 필드를 추가하지 않았다.

## 검증

`tests/integer_hash_regression.py`를 기본 평가에 추가했다. 큰 양수·음수, native 경계, bool, 정수 하위 타입, 잘못된 반환값, hash=None, descriptor/classmethod/staticmethod, 다중 상속, 동적 변경, 오류 순서와 작은 해시 반환 객체의 캐시를 CPython과 비교한다.

- 수정 전 DLL에서 새 회귀의 출력 불일치를 확인했다.
- 새 회귀와 기존 errno 하위 타입 회귀 모두 실제 CPython 출력과 일치한다.
- Release 빌드 및 기본 153개 통과: 완전 일치 148개, 지정 변동 필드 제외 일치 3개, native 검사 실패 보고 없음 2개.
- Godot는 SharpPy 소스 직접 포함으로 빌드하고 기본 17개 + 관련 9개, 총 26개 fixture 통과. 관련 9개는 새 CPython 실행 출력과 대조했다.
- Windows x64/.NET 8.0.31에서 검증했다. 비Windows·32-bit·모바일 실기 실행은 하지 않았다.

## 이번 변경의 성능

86차 DLL과 이번 DLL을 비교한다. Release, TieredCompilation=0, 10만 회, 워밍업 10회, 표본 15회, 전후 3쌍 교대 실행이며 빌드·기능 검증과 겹치지 않았다. 모든 workload의 반복 결과 checksum을 검사한다. 새 두 workload도 변경 전후에 결과가 같은 경우를 선택했다. 기본 int 하위 타입의 errno=2와 hash(2)를 사용하며, 이전에 잘못되던 사용자 해시나 큰 정수를 성능 개선 근거로 쓰지 않았다.

| workload | 이전 ms | 이후 ms | 변화 | 이전 B | 이후 B |
|---|---:|---:|---:|---:|---:|
| os_error_fallback | 79.5783 | 78.3111 | -1.59% | 25,904,120 | 25,904,120 |
| os_error_construction | 105.9909 | 105.6977 | -0.28% | 41,104,040 | 41,104,040 |
| os_error_winerror | 92.0936 | 92.7215 | +0.68% | 41,104,120 | 41,104,120 |
| os_error_subclass_lookup | 88.1128 | 88.1412 | +0.03% | 33,905,280 | 29,905,280 |
| integer_hash | 68.2142 | 67.2039 | -1.48% | 16,304,032 | 12,304,032 |

[검증 결과와 전체 표본](round87-validation.json)

os_error_subclass_lookup와 integer_hash에서 각각 10만 회당 4,000,000 B, 호출당 40 B가 줄었다. 전자는 임시 bound wrapper, 후자는 작은 PyInt 반환 객체의 제거와 일치한다. 이 두 경로의 측정 시간 변화는 각각 +0.03%/-1.48%다. 기존 세 생성 경로의 할당은 동일하다. 다섯 workload 모두 프로세스 중앙값 범위가 겹치므로 시간 개선을 주장하지 않는다.

새 두 workload는 최초 시도에서 하네스 허용 목록 누락으로 측정 전에 종료됐다. 등록을 보완한 뒤 전후 3쌍을 다시 실행했으며, 실패한 실행은 표본에 포함하지 않았다.

## 남은 범위와 다음 작업

이번 hash() 수정 범위는 int/bool/int 하위 타입이다. float와 일반 사용자 객체 등 다른 타입의 Python 해시 호환성을 모두 해결한 것은 아니다.

일반 dict 조회는 여전히 기존 CLR 32-bit ToHash와 자체 equality comparer를 사용한다. `tests/compatibility/int_subclass_hash_protocol.py`에서 hash()의 callback과 결과는 이제 CPython과 일치하지만, dict 삽입·조회 callback은 누락되어 lookup missing이 남는다. 이 파일은 통과 목록에 포함하지 않았다. 큰 문자열 파싱 실패도 별도로 유지한다.

ToHash만 새 해시로 바꾸는 것은 충분하지 않다. dict의 수치 타입 간 동등 값은 같은 해시를 가져야 하며, 현재 float의 CLR 해시와 큰 int의 해시는 그 조건부터 검토해야 한다. 또한 CLR comparer는 32-bit 해시를 받으므로 Python native-width 해시를 단순 축소하면 CPython에 없던 동등 비교 callback이 발생할 수 있다. comparer의 PyBool 강제 캐스팅도 임의의 Python 진리값 반환을 처리하지 못한다. 다음 단계는 저장된 전체 해시와 Python 동등 비교를 어떻게 보존할지 검토하고, callback 순서·재진입·수치 키를 실제 CPython과 비교하는 것이다.

이전 일반 생성 시간 증가의 원인이 해결됐다고 결론 내리지 않는다. 직접 생성과 VM 비용의 분리 결과를 유지하면서 후속 변경의 비용을 대조한다.
