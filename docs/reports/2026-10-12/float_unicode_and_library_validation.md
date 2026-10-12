# 123차: float 경계, Unicode 전수 비교와 통합 검증

CPython 3.12.15 (`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`)의
Python/formatter_unicode.c와 실제 실행을 기준으로 비교했다.

## 변경

- float 포맷을 먼저 검증하고 값 변환, 부호 결정, 폭/정렬 순서로 처리한다.
  inf/nan도 잘못된 type, 남는 포맷 문자, precision 누락을 검사한다.
- inf/nan의 E/F/G 대문자, % 접미사, 부호 뒤 패딩을 일반 숫자와 공통 처리한다.
  별도 special padding 구현은 제거했다.
- 음수 0은 부호 비트로 보존한다. z 옵션은 반올림된 숫자가 0일 때 부호를 제거한다.
  e/E 지수의 불필요한 세 번째 선행 0을 제거한다.
- 새 기본 회귀는 7개 값 × 10개 포맷 및 잘못된 포맷 6개를 CPython과 대조한다.
  float formatter 전체를 완성한 것은 아니다. 생략 type의 repr 방식, 일반 형식의
  반올림/후행 0, alternate/grouping 조합, locale n, 매우 큰 width/precision은 남는다.
  이번 변경에 대한 속도 개선 수치는 주장하지 않는다.

## Unicode

`unicode_repr_reference.py`가 CPython 3.12의 repr(chr(i))를 생성하고
`--unicode-repr-probe`가 native PyString.ToRepr와 대조한다.
서로게이트를 포함한 1,114,112개 코드 포인트에서 차이 0개였다.
참조 파일 SHA256은
`D251DCEBA37AF0DADEA5982C594A2DDAEA9D126F6955E85EC9D39A276CC65572`다.
이 결과는 현재 .NET 8.0.31 환경의 단일 코드 포인트 비교이며,
모든 문자열 조합·VM 경로·다른 플랫폼의 Unicode 테이블까지 보증하지 않는다.
추가 런타임 코드는 필요하지 않았다.

재현은 CPython 3.12로 다음 참조 파일을 만들고 Release harness를 실행한다.

```text
python tools/PerformanceHarness/unicode_repr_reference.py .artifacts/unicode-reference.txt
dotnet tools/PerformanceHarness/bin/Release/net8.0/PerformanceHarness.dll --unicode-repr-probe .artifacts/unicode-reference.txt
```

## 파일 API의 남은 범위

새 `tests/compatibility/file_binary_open_remaining.py`는 rb/read(3)의 실패를 보존한다.
CPython은 bytes를 반환하지만 SharpPy는 rb를 지원하지 않으며 잘못된 모드 오류도
OSError로 감싼다. 기본 성공 회귀에 넣지 않았다.
코드상 open 시점 대신 context enter에서 여는 구조, text read(size), 쓰기 truncate,
close 오류 처리도 후속 감사가 필요하다. 결과만 맞추는 rb 분기를 추가하지 않았다.
Godot의 res/user 경로와 일반 파일에 공통으로 적용할 열기·수명·오류 계약이 먼저다.

## 최종 통합 검증

- 기본 224개: 출력 일치 219, 기존 가변 필드 제외 일치 3, native CLI 실패 보고 없음 2.
- compatibility 하위 폴더 포함 75개: 일치 72, memoryview 수명 차이/실패 2,
  신규 보존 binary open 실패 1. 이 검사는 미해결 사례 때문에 실패 종료한다.
- Godot 4.7.2에 C# 소스를 직접 포함해 빌드하고 실행했다. 기본 17개와 경로 5개 통과,
  관련 6개 fixture의 stdout이 CPython과 일치했다. stderr 일치를 검사한 것은 아니다.
- native 호스트 snapshot/동기화 11개 일치, Unicode probe 차이 0개.
- 비교 캐시를 원래 구현으로 복원한 상태에서 첫 비교 1,000회 할당 40,000B,
  반복 비교 0B, 잘못된 enum 8개 및 C# override 12개 검증 통과.
- .NET Release 및 Godot 소스 빌드 오류/경고 0개.

실패 재현과 최종 실행 원문은 `round123-validation.json`에 함께 보관했다.
성능 측정과 이번 기능 회귀는 별개다. 119차 호스트 Copy의 추가 스캔 비용,
120차 작은 set 할당 감소, 121차 기각 후보 수치는 각 보고서가 기준이다.

## 후속 우선순위

1. 소유권 계약을 VM 스택·컨테이너·프레임·호스트에 연결하여 memoryview 마지막 참조 해제 구현.
2. 호스트 mapping 단일 저장소/변경 API로 외부 Dictionary 순서와 iterator 동작 보장.
3. 작은 set의 잔여 할당 비용과 비교 dispatch 최적화. 채택 전 대조군과 반복 측정.
4. warnings/async generator/set의 미감사 경계 및 hooks/최종화.
5. float 일반 포맷과 파일 IO의 위 미지원 범위를 각각 과정·결과 회귀로 확대.

118~123차는 여섯 항목을 조사·수정·검증한 기록이며 모든 미지원 기능을 해결했다는
의미가 아니다. 특히 소유권 후보는 측정만 했고, 비교 캐시 압축은 비용 증가로 기각했다.
