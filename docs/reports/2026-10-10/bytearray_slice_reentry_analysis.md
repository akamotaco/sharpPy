# 34차: bytearray slice 재평가와 RHS 복사 제거

33차는 `04c6fda`로 커밋했다. 이번에는 CPython bytearray의 slice callback 재평가와 변환 중 대상 크기 변경을 맞췄다.

## 동작과 구현

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 `Objects/bytearrayobject.c:bytearray_ass_subscript`는 bytes·일반 iterable·자기 자신을 RHS로 받으면 별도 bytearray로 변환한 뒤 원래 slice로 다시 진입한다. 별개의 bytearray는 재진입하지 않는다. 따라서 사용자 __index__는 전자에서 두 번, 후자에서 한 번 실행된다.

SharpPy는 slice 정수 변환과 길이에 따른 경계 조정을 분리했다. 대입에서는 callback과 RHS 변환이 끝난 뒤의 실제 대상 길이로 경계를 조정한다. CPython의 첫 경계 조정은 Python callback이나 오류가 없는 계산이므로 생략하고 최종 조정만 수행한다. 불변 int/bool/None 경계는 두 번째 정수 변환을 생략하고 사용자 callback은 재실행한다. CPython과 같은 관찰 결과를 얻기 위한 분리이며 재귀 호출 자체를 모사하지 않는다.

불변 bytes는 원본 버퍼를 직접 읽고, 별개 bytearray도 callback이 끝난 뒤 원본 저장소를 읽는다. 자기 대입과 일반 iterable은 독립된 변환 결과를 보존한다. Span은 모든 Python callback이 끝난 뒤 사용한다. 원본 저장소와 대상이 구분되는 경우에만 복사를 생략하며 새 외부 의존성은 없다.

## 정확성

새 회귀는 RHS별 callback 횟수, callback의 대상 확장, 자기 대입 snapshot 시점, RHS 순회 중 축소·확장, 두 번째 step 반환값 변경, 두 번째 callback 예외, RHS 실패 시 재평가 미실행을 검사한다. 기존 최소 callback 재현도 기본 평가로 편입했다. 기준 DLL은 두 테스트에 실패하고 최종 DLL은 실제 CPython 3.12.15와 일치한다.

Release 빌드 및 기본 74개 검사: raw 일치 69, 지정 변동 필드만 다른 3, native no_reported_failure 2. Godot 4.7.2/.NET 8.0.31 소스 직접 포함 빌드와 선별 회귀 7개 통과. 별도 SharpPy DLL 참조는 사용하지 않는다. 기존 GODOT 심볼 및 DEBUG 명령 제한 제외 조건을 유지했다.

## 성능

동일 native bytearray mutation harness로 전후 3쌍의 프로세스를 순차 실행했다. DOTNET_TieredCompilation=0, 각 100 warmup/10표본이다. 초기 함수 분리만 한 구현은 작은 대입이 느려졌고, 인라인 조정 후에도 일부 비용이 남아 불변 bytes의 중복 복사까지 제거했다. 모든 단계의 표본을 보존했다.

최종 bytes RHS 대입 결과:

| 크기 / step | 작업 수 | 기준 중앙값 ms | 최종 중앙값 ms | 기준 → 최종 할당 B |
| --- | ---: | ---: | ---: | ---: |
| 16 / 2 | 1,000 | 0.05895 | 0.01620 | 64,000 → 0 |
| 16 / -2 | 1,000 | 0.03120 | 0.01650 | 64,000 → 0 |
| 1,024 / 2 | 100 | 0.03465 | 0.02370 | 56,800 → 0 |
| 1,024 / -2 | 100 | 0.03385 | 0.02300 | 56,800 → 0 |

큰 입력의 대입 시간은 약 31.6~32.1% 감소했다. 이 할당 수치는 미리 만든 RHS와 target에 대한 native 대입만 측정한 것이며 Python 실행 전체의 무할당을 뜻하지 않는다.

혼합 측정의 크기 16/step=2 삭제는 0.04715→0.11535ms로 느려졌다. 대입과 삭제를 별도 프로세스로 분리할 수 있도록 harness에 operation 선택을 추가하고 동일 harness를 양쪽 DLL에 사용해 확인했다. 삭제 단독은 0.09380→0.08380ms이며 사분위 범위가 크게 겹쳤다. 혼합 결과의 하락은 단독 측정에서 재현되지 않았으나 원인을 확정하지 않는다. 삭제의 무조건적인 속도 향상은 주장하지 않으며 두 결과를 모두 보존한다.

[표본·사분위수·전후 회귀·소스 hash·Godot 로그](round34-bytearray-reentry.json).

## 다음 범위

이번 변경은 bytearray slice 대입에 적용했다. 다른 타입과 bytearray 읽기·삭제에서 callback에 따른 길이 변경, 항목의 일반 __index__ 변환, range/view의 큰 step 표현은 별도 검증 대상이다. unsupported RHS의 상세 오류 등 bytearray 전체 호환성을 완료했다고 주장하지 않는다.
