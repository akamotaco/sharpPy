# 33차: bytearray iterable 변환의 정상 종료

32차를 `5da8e90`으로 커밋했다. bytearray slice에 일반 iterable을 대입할 때 정상 종료의 StopIteration이 전파되는 오류를 수정했다. 기존 `ConvertToBytes`는 null 반환만 종료로 인정했지만 SharpPy의 Python iterator는 StopIteration을 던진다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/bytearrayobject.c에서 `bytearray_ass_subscript`가 RHS를 bytearray로 변환하는 경로와 `bytearray___init___impl`의 iterator 소진 루프를 대조했다. 정상 종료만 소비하고 항목 변환 오류는 전파한다. SharpPy도 `Next()` 호출에만 catch 필터를 적용했다. 기존 native iterator의 null 종료는 유지한다. bytes/bytearray의 직접 복사 경로는 바꾸지 않았다.

## 검증

새 회귀는 list·tuple·list iterator·generator·사용자 iterator의 slice 대입, 길이가 바뀌는 대입, 빈 RHS 삭제, extend, 비정상 iterator 예외와 항목 범위 오류, 길이 불일치, buffer export 중 같은 크기의 변경 및 resize 거부를 검사한다. 예외 발생 시 대입 대상 보존과 iterator 호출 횟수도 확인한다.

기준 DLL은 새 테스트와 기존 최소 재현 모두 StopIteration으로 실패했다. 변경 후 두 테스트는 실제 CPython 3.12.15와 일치한다. 기존 최소 재현을 기본 평가에 편입했다. 최종 Release 빌드와 기본 72개 검사 결과는 raw 일치 67, 지정 변동 필드만 다른 3, native no_reported_failure 2다.

Godot 4.7.2/.NET 8.0.31의 소스 직접 포함 빌드와 여섯 회귀가 통과했다. 별도 SharpPy DLL 참조는 없으며 기존 DEBUG 명령 제한을 제외한 GODOT 빌드 구성을 사용했다. [전후 실행·hash·Godot 로그](round33-bytearray-iterable.json)를 보존했다.

기준 구현이 일반 iterable 대입을 완료하지 못하므로 같은 작업의 전후 속도 향상률은 제시하지 않는다. 이번 수정은 정상 종료 처리이며 새 버퍼나 외부 의존성을 추가하지 않았다.

## 후속 범위

CPython은 RHS가 별도 bytearray가 아니면 변환 후 원래 slice를 재평가한다. SharpPy는 현재 한 번만 평가하므로 callback 횟수 및 callback/RHS가 대상을 변경하는 경우를 별도로 수정해야 한다. `tests/compatibility/bytearray_slice_callback_order.py`에서 실제 CPython 통과·SharpPy 실패를 확인했고 위 72개에는 포함하지 않았다. 이는 range/list의 불필요한 중복 callback 제거와 다른 bytearray 고유 동작이다.

항목의 일반 __index__ 변환, memoryview 순회, range/view의 큰 step도 이번에 해결한 것으로 취급하지 않는다. 새 회귀의 buffer 관찰은 직접 인덱싱을 사용한다.
