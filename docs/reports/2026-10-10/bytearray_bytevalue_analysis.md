# 35차: bytearray 항목의 index 변환

34차는 `f57ce71`로 커밋했다. 이번에는 GetByteValue를 사용하는 bytearray 생성·append·insert·remove·대입·extend의 항목 변환을 수정했다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/bytearrayobject.c `_getbytevalue`는 정수 또는 __index__를 받아 0~255 범위를 검사한다. C long 변환을 넘는 정수도 byte 범위 ValueError다. SharpPy는 일반 PyInt의 직접 경로를 유지하고 나머지는 기존 TryGetIndex를 사용한다. 포화 변환 후 범위를 검사하여 CLR OverflowException을 피한다. bool과 일반 __index__ 객체도 처리하며, 부적합 타입의 메시지를 맞췄다.

정수 위치 대입에서는 값 변환을 먼저 하고 이후의 실제 길이로 음수 인덱스와 범위를 검사한다. 값 callback이 대상을 축소했을 때 오래된 경계로 접근하지 않는다. 위치 인덱스 자체의 큰 정수·일반 __index__ 지원은 이번 범위가 아니다.

## 검증

새 회귀는 각 변환 경로, callback 순서, bool, ±10**100 범위 오류, 비정수 및 잘못된 __index__ 반환, 값 callback의 StopIteration 객체 보존, 대상 축소와 빈 대상, 값 오류와 위치 오류의 우선순위를 검사한다. 기준 DLL은 실패하고 최종 DLL은 실제 CPython과 일치한다. 기준 DLL hash가 34차 최종 결과와 일치하는 것도 확인했다.

Release 빌드 성공. 기본 75개는 raw 일치 70, 지정 변동 필드만 다른 3, native no_reported_failure 2다. Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드와 회귀 8개 통과. 별도 SharpPy DLL은 참조하지 않는다. GODOT 심볼 및 DEBUG 명령 제한 제외 설정은 이전과 같다.

이번에는 별도 성능 측정을 하지 않았으므로 속도 향상이나 무퇴행을 주장하지 않는다. 새 복사 버퍼나 외부 의존성은 추가하지 않았다. [전후 실행·hash·Godot 로그](round35-bytearray-bytevalue.json)에 근거를 보존했다.

## CPython 비교에서 분리한 사례

최초 회귀는 값 callback이 `bytearray(b'abc')`에 100을 append한 뒤 88을 반환하면 `data[-1]` 대입의 결과가 `abcX`라고 예상했다. 실제 CPython 3.12.15는 로컬 실행에서 `abcd`를 반환했다. 이 가정이 잘못된 초기 테스트는 reference_test_failure로 보존했으며 성공 근거에서 제외했다.

원본 `bytearray_ass_subscript`는 callback 전에 buf 포인터를 보관한다. callback의 저장소 재할당 후 오래된 포인터를 쓰는 문제가 의심되지만 여기서 메모리 이동 자체를 계측하지는 않았다. 이 결과를 안정적인 Python 의미로 일반화하거나 C#에서 재현하는 분기를 추가하지 않았다. 최종 회귀는 짧은 꼬리 삭제 후 음수 인덱스 조정 및 전체 삭제 후 IndexError를 실제 CPython과 비교한다. 저장소 재할당 callback의 완전한 동작 일치는 미확인으로 남긴다.

별도로 `str(StopIteration('value conversion'))`의 기존 출력 차이를 발견했다. 변경 전후 모두 실패하며 `tests/compatibility/stop_iteration_message.py`로 보존했다. 새 항목 변환 회귀는 예외가 삼켜지지 않는지 객체 identity로 확인하고, 메시지 차이를 숨겨 통과시키지 않도록 별도 실패를 보고한다. 위 기본 75개에 이 알려진 실패는 포함하지 않는다.
