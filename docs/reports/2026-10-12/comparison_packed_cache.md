# 121차: 비교 캐시 압축 후보 — 반복 비용으로 채택하지 않음

별도 ComparisonMisses 객체를 64-bit 필드로 압축하는 후보를 구현·측정했다.
6개 연산의 miss와 native base 문맥(null/int/tuple), 전역 mutation epoch를 함께
보존한다. epoch 범위를 넘으면 캐싱을 중단하여 잘린 epoch가 일치하지 않게 했다.
후보 patch를 comparison_packed_cache_candidate.patch에 남기고 런타임은 복원했다.

첫 2-bit base tag 후보는 native 비교가 약 7% 느려졌다. tag helper inline으로
일부 회복했고, 최종 후보는 세 문맥의 18-bit miss mask를 사용했다.
클래스 1000개 생성은 기존 1,032,000B, 첫 비교 40,000→0B,
반복 비교 0B였다. 잘못된 enum 8개와 C# override 12개 검사를 통과했다.

Release, tiering off, affinity 0x4, 100만 회·15표본·교차 3쌍의 native 중앙값:

| 경로 | 기존 ms | 후보 ms |
|---|---:|---:|
| payload slot | 8.2885 | 8.2888 |
| subclass slot | 11.9271 | 12.1103 |
| subclass dispatch | 16.1164 | 16.3409 |

핫 경로가 약 1.5% 느려져 첫 사용 할당 절감을 채택 근거로 삼지 않았다.
VM 1만 회의 int subclass/float 중앙값 13.8482→13.8947ms는 범위가 겹친다.
대조군도 별도 원시 결과에 보존했다. 최초 affinity 없는 측정은 대조군부터 크게
흔들려 판정에서 제외하고 위 고정 affinity 교차 측정을 사용했다.

과거 단일 지연 원인은 여전히 미확정이다. 이번 항목은 새로운 구조 후보를
검증·기각한 결과이며 반복 성능 개선 완료가 아니다. 원시 결과는 round121-validation.json.
