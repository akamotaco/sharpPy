# 116차: 비교 슬롯 비용과 예외 생성 원인 분리

새 --comparison-dispatch-probe는 VM loop 없이 동일한 int/float 값을
숫자 payload 슬롯, int subclass 슬롯, 공통 Compare 진입점으로 각각 호출한다.
delegate/결과 확인만 수행하는 control도 측정한다. 100만 회, warmup 5,
표본 15, tiering off, CPU mask 0x4. 보존 90차와 현재 DLL을 동일 하네스에서
교차 3쌍 비교했다. 과거에는 Compare API가 없어 --slots-only를 사용한다.
첫 일반 probe 실행은 이 API 차이로 MissingMethodException을 발생시켰고,
공통 API로 제한한 뒤 재측정했다.

| native 슬롯 | 90차 ms | 현재 ms |
|---|---:|---:|
| control | 1.2021 | 1.1987 |
| payload int/float | 8.2707 | 8.4741 |
| subclass int/float | 10.0900 | 12.1104 |

반복 할당은 모두 0이다. 현재 subclass-payload 차이는 약 3.64ns/회,
과거 약 1.82ns/회. 숫자 연산보다는 사용자 비교 메서드 조회/캐시와 슬롯
위임 경로에 추가 비용이 있다. 이 차이를 과거 VM +2.73%의 단일 원인으로
확정하지 않는다. 코드가 달라진 전체 바이너리 비교이기 때문이다.

RichCompare AggressiveInlining 후보는 동일 현재 런타임끼리 비교했다.
subclass 슬롯 12.1197→12.1086ms, 전체 dispatch 16.4228→16.4257ms로
유효한 개선이 없어 제거했다. 사용자 callback/NotImplemented/host virtual
dispatch를 생략하는 최적화는 넣지 않았다.

VM 10만 회·표본 15·교차 3쌍 비교:

| workload | 과거 ms | 현재 ms | 해석 |
|---|---:|---:|---|
| int subclass/float (90차) | 139.5869 | 140.1624 | 범위 겹침, 약 +0.41% |
| exact number 대조군 (90차) | 140.5495 | 141.3288 | 범위 겹침, 약 +0.55% |
| OSError 구성 (85차) | 99.2043 | 97.5333 | 현재가 빠름 |
| ValueError 대조군 (85차) | 66.9908 | 63.8429 | 대조군도 빠름 |

할당은 각 전후 동일하다. 직접 10만 회 생성은 OSError 한 인자
1.08465→0.9296ms, FileNotFoundError 0.99685→0.9725ms,
ValueError 0.8638→0.8355ms이며 할당은 동일하다. 과거 예외 지연을
현재의 지속적 회귀라고 볼 근거는 없다. 과거 단일 원인은 여전히 미확정이다.

기존 native 검사도 클래스 1000개 1,032,000B, 첫 비교 40,000B,
반복 비교 0B, invalid enum 8개, C# override 12개를 유지한다.
런타임 변경은 없으므로 기존 의미 회귀 결과를 사용한다.
원시 값/해시는 round116-validation.json. 이 항목은 **측정 경로 분리 완료**이며
미확정 역사적 원인을 해결했다고 표시하지 않는다.
