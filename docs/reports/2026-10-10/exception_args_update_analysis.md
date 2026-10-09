# 37차: 예외 args 재대입과 관찰 identity

36차는 `bc14d9d`로 커밋했다. 예외 args 대입이 실제 인자 저장소를 갱신하지 않던 오류를 수정했다. CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/exceptions.c `BaseException_get_args`/`BaseException_set_args`, Objects/abstract.c `PySequence_Tuple`을 대조했다.

정확한 tuple은 그대로 사용하고 정확한 list는 복사한다. 일반 iterable은 정상 종료까지 변환한 뒤 args를 교체한다. iterator의 다른 예외는 전파한다. 실패 전 callback이 args를 직접 바꿨다면 그 부작용은 남기고 오래된 값을 강제로 복구하지 않는다. args 삭제는 TypeError로 거부하며 StopIteration.value는 독립적으로 유지한다.

반복 조회는 같은 tuple을 반환한다. 이를 위해 인자 배열 참조 필드를 배열 또는 관찰된 tuple을 담는 하나의 참조 필드로 바꿨다. 추가 캐시 필드나 예외 생성 시 tuple 할당은 없다. 빈 tuple은 기존 Empty를 공유하고, list에서 변환한 비어 있지 않은 tuple은 값이 같더라도 임의로 intern하지 않는다. C# Args API는 배열 접근을 유지한다.

## 검증

새 회귀는 StopIteration·ValueError·RuntimeError에서 tuple identity, list 복사, 빈 값과 문자열 변환, str 갱신, 비iterable 오류와 이전 args 보존, 삭제 거부, generator 순회 중 기존 args 관찰, 실패 및 재진입 부작용을 검사한다. 기존 StopIteration 최소 재현도 기본 평가에 포함했다. 기준 DLL은 실패하고 최종 DLL은 실제 CPython과 일치한다.

Release 빌드 성공. 기본 79개는 raw 일치 74, 지정 변동 필드만 다른 3, native no_reported_failure 2다. Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드와 선별 회귀 10개 통과. 별도 SharpPy DLL 참조는 없고 기존 GODOT 심볼 및 DEBUG 제한 제외 조건이다. [실행 결과·소스 hash·Godot 로그](round37-exception-args.json)를 보존했다.

성능 벤치마크는 수행하지 않았다. 캐시 필드를 추가하지 않았으며 반복 args 조회의 tuple 재생성은 제거했지만 전체 예외 생성·문자열 처리의 속도 향상이나 무퇴행을 주장하지 않는다.

## 후속 범위

일반 iterable의 CPython PySequence_Tuple 경로는 iterator를 얻은 후 원본의 길이 힌트를 평가한다. 현재 args 변환은 이 callback을 아직 실행하지 않는다. `tests/compatibility/exception_args_length_hint.py`에서 __len__의 RuntimeError와 호출 순서 차이를 재현했다. 기본 79개에 이 알려진 실패는 포함하지 않았다. 길이 힌트 프로토콜과 실패 우선순위가 다음 범위다. 사용자 정의 예외 wrapper 및 다른 예외 생성자의 전체 인자 보존도 이번에 완성한 것으로 취급하지 않는다.
