# 22차: memoryview 명시적 해제

기존 검증 변경은 `f472cce`로 커밋했다. 이번 변경은 `release()`와 context manager 종료, view 복제 및 해제 후 접근을 구현한다. 자동 회수까지 완성된 것으로 취급하지 않는다.

## 동작과 구현

[CPython 3.12.15의 _memory_release와 release/enter/exit](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Objects/memoryobject.c#L1095)를 확인했다. 명시적 해제는 반복 호출해도 성공하고, 해제 후 데이터 접근·len·bool·itemsize·재진입·view 복제는 ValueError다. repr와 추가 release는 가능하다. 부모를 해제해도 자식 view는 살아 있어야 하고, 마지막 활성 view가 해제되어야 bytearray의 크기를 바꿀 수 있다.

기존 List 저장소 참조를 실제 bytearray exporter 참조로 바꿨다. view 생성/복제/slice가 exporter 계수를 증가시키고 release가 한 번만 감소시킨다. 저장소 참조를 null로 바꿔 해제 상태를 표현하고 원본 참조도 내려놓는다. 추가 view 필드, 별도 추적 객체, 강제 GC 또는 자체 회수 정책은 없다. Python에 노출되지 않는 계수는 CPython의 내부 구조를 복제하는 대신 활성 view의 resize 보호 목적에 사용한다.

인덱스는 BigInteger 연산을 반복하는 대신 CPython의 Py_ssize_t 변환처럼 먼저 64비트 정수로 변환한 뒤 범위를 검사한다. 현재 x64 환경에서 큰 양/음수의 IndexError 종류와 메시지를 CPython과 비교했다. slice 경계는 한 번만 정규화하고 그 결과로 길이를 계산한다. 기존 코드는 Indices와 GetLength에서 같은 정규화를 두 번 수행했다.

## 검증

`tests/memoryview_release_regression.py`는 actual CPython 3.12.15와 다음을 비교한다: 반복 release, 해제 후 접근/진리값/metadata/복제/재진입 오류, repr, 부모·자식·독립 view·빈 slice의 resize 보호, memoryview(view)의 alias, 정상/예외 with 종료, 읽기 전용 view 해제, 잘못된 인자, 큰 인덱스 오류.

기준 DLL은 release 재현에 실패했고 변경 후 통과했다. Release 본체/harness 빌드 성공. 전체 기본 검사 58개는 raw 출력 일치 53, 지정한 시간·주소 필드만 다른 3, native no_reported_failure 2이며 새 실패는 없다. 이전 alias/slice 회귀도 통과했다.

## 성능

동일 harness에 기준/최종 DLL만 교체해 3쌍의 process를 번갈아 실행했다. process별 10 warmup/10 sample, build·준비·다른 테스트는 측정에서 제외했다. 버퍼 크기는 1,024바이트이며 결과 길이/읽은 값을 검증한다. release 의미는 별도 Python 회귀로 검사한다.

| Native 작업 | 전 중앙값 ms (Q1–Q3) | 후 중앙값 ms (Q1–Q3) | 전후 할당 B |
| --- | ---: | ---: | ---: |
| 생성 10,000회 | 0.20285 (0.1996–0.2542) | 0.22875 (0.2049–0.2913) | 560,000 동일 |
| slice 10,000회 | 0.79445 (0.7904–0.8066) | 0.48165 (0.4743–0.4856) | 560,000 동일 |
| 읽기 100,000회 | 1.88505 (1.7920–2.0172) | 1.50435 (1.4903–1.5304) | 0 동일 |

slice 중앙값은 약 39.4%, 읽기는 약 20.2% 감소했다. 생성 시간 중앙값은 증가했으나 분포가 겹치므로 개선/동일성을 확정하지 않는다. Python VM 전체 속도 개선을 뜻하지 않는다. 해제 검사만 추가한 중간 구현과 효과를 확인하지 못해 제거한 강제 인라인 지정의 측정도 원자료에 보존했다.

## 남은 차이와 다음 우선순위

`tests/compatibility/memoryview_automatic_release.py`는 `del view` 후 bytearray append가 CPython에서 즉시 가능하지만 sharpPy에서는 여전히 BufferError임을 재현한다. 별도 실행 결과는 **CPython 통과, sharpPy 실패**다. 기본 58개 통과 현황에는 이 실패를 포함하지 않았으며 자동 해제 지원을 주장하지 않는다.

자동 해제는 다음 우선 조사 대상이다. 현재 VM/CLR의 객체 수명과 CPython의 참조 해제 시점 차이를 해결해야 한다. CLR finalizer만 추가해도 CPython의 즉시 해제와 같아지는 것은 아니므로, 정확성 및 생성/GC 비용을 함께 검증해야 한다. module 저장소와 dict cursor 등 기존 별도 호환성 차이도 남아 있다.

[전체 측정 sample, DLL hash, 검사 상태](round22-memoryview-release.json). 전체 raw 출력은 `.artifacts/round22-tests-final.json`, 자동 해제 실패는 `.artifacts/round22-automatic-gap.json`에 보관했다.
