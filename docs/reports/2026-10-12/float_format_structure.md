# 124차: CPython 포맷 구조와 공통 dispatch

대상은 후속 1~6 중 5번이다. 1번은 CLR의 메모리 수거와 Python의 관찰 가능한
자원 해제 시점을 구분해 설계해야 한다. 이번 변경에 참조 카운팅은 추가하지 않았다.

## 기준 구조와 변경

CPython 3.12.15 고정 소스 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의
Objects/abstract.c:PyObject_Format 및 Python/formatter_unicode.c를 읽고 비교했다.

- PyObject_Format은 인스턴스 속성 조회가 아닌 타입의 special method를 바인딩한다.
  callback 예외를 전파하고 반환값이 str인지 검사한다. 이 계약을 format(),
  str.format(), VM f-string에 공통 적용했다. exact builtin의 고정 슬롯은 직접 호출한다.
- 기존 VM·str.format의 별도 숫자 포맷 구현과 오류 무시 fallback을 제거했다.
  기존 float의 원래 literal 문자열 보존 대신 값의 shortest round-trip을 표현한다.
- CPython처럼 포맷 파싱/검증, binary64 숫자 변환, 그룹 구분/폭 정렬을 나눈다.
  숫자 변환은 .NET의 반올림 구현을 사용한다. C의 dtoa 코드를 옮기지 않았다.
- 일반 포맷의 지수 표기 선택은 반올림 후 지수를 기준으로 한다. 생략 type의
  소수점/지수 규칙, # 유효 숫자 보존, %, 그룹과 0 패딩, 명시적 fill,
  비 BMP fill/십진 숫자, 잘못된 type/precision의 오류를 검증했다.
- 기본 C locale의 n을 지원한다. 변경 가능한 LC_NUMERIC 연결, CLR 최대 문자열
  크기를 넘는 결과, 임의 중첩 str.format field 문법 전체는 검증 완료 범위가 아니다.

## 검증

`float_general_format_regression.py`는 반올림 경계와 세 진입점, custom __format__,
instance attribute hook 미호출, AttributeError 전파, 잘못된 반환값을 비교한다.
기존 float 특수값·integer 포맷 회귀도 일치한다.

CPython 3.12가 생성한 seed 124의 무작위 binary64 1,500개 × 포맷 18개,
27,000개를 native formatter와 비교해 차이 0개였다. 원본 생성기와 비교 probe를
harness에 보관했다. 이는 표본 검증이며 모든 binary64 조합에 대한 증명은 아니다.

```text
python tools/PerformanceHarness/float_format_reference.py .artifacts/float-reference.jsonl
dotnet tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll --float-format-probe .artifacts/float-reference.jsonl
```

기본 225개: 출력 일치 220, 기존 가변 필드 제외 3, native CLI 2.
이 전체 실행 뒤 exact builtin의 custom-type 우회 방지 guard를 추가했으며,
최종 바이너리로 관련 3개와 native 27,000개를 재검증했다.
Godot 소스 빌드 후 기본 17개·경로 5개·관련 3개 통과.
Godot fixture 비교 범위는 stdout이다. 전체 결과와 최종 바이너리별 검증 근거는
round124-validation.json에 구분해 저장한다.

## 성능

cc71585 바이너리와 최종 후보를 교대로 실행했다. tiering off, affinity 0x4,
10,000회, warmup 10, 표본 15, 3쌍의 프로세스 중앙값을 사용했다.

| 대상 | 이전 ms | 이후 ms | 10,000회 할당 B |
|---|---:|---:|---:|
| format(12.5, '+08.2f') | 9.0744 | 8.0691 | 3,601,264 → 2,561,264 |
| format(12.5, '.4g') | 9.2088 | 7.8104 | 3,201,264 → 2,161,264 |
| 같은 정수 누적 루프 대조군 | 3.7212 | 3.6134 | 801,176 → 801,176 |

대상당 104B 감소. 대상 시간은 각각 약 11%, 15% 감소했고 프로세스 중앙값
범위가 겹치지 않았다. 대조군은 범위가 겹쳤다. 모든 float 포맷의 속도를 뜻하지 않는다.
초기 general 변환 후보는 임시 문자열로 할당이 증가해, 일반 g/G의 정확한 .NET
변환 경로를 직접 사용하도록 수정한 뒤 위 최종 후보를 측정했다.

## 소유권 설계에 대한 정정

CLR GC는 객체 메모리를 회수한다. CPython 비순환 마지막 참조 해제 시점이나
buffer export 반납을 자동 구현하지 않는다. 현재 memoryview의 export 반납은
Release에만 연결되어 있어, CLR 수거만으로 exporter 상태가 복구되지 않는다.
전체 객체의 참조 카운팅이 유일한 해법이라고 가정하지 않는다. 자원 수명 계약과
그 소유자 전파 범위를 먼저 정의하고, 호스트 API와 성능 비용까지 검증해야 한다.
강제 GC나 삭제 시 무조건 release하는 수정은 채택하지 않는다.
