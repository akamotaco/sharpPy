# 127차: str.format 필드 처리 과정

후속 5번의 중첩 필드 처리. 고정 CPython 3.12.15 revision
e848e4b09ca4bc67c0041c9ce7526dae471d4c36의
Objects/stringlib/unicode_format.h를 직접 확인했다.

필드 구문을 읽고, 위치/키/속성을 조회하고, !s/!r/!a 변환을 수행한 다음,
중첩 format spec을 확장하여 공통 __format__ 경로를 호출한다. 자동 번호 상태는
중첩 확장과 공유하고 깊이는 CPython과 같은 2로 제한한다. format_map은 일반
mapping의 __getitem__을 사용하며 위치 필드를 거절한다. !a는 비BMP 문자도 처리한다.
사용하지 않는 인자를 미리 ToString으로 변환하던 배열과 중복 파서를 제거했다.

회귀는 callback 순서, unused 인자, 중첩 번호, 키 안의 특수 문자, Unicode 번호,
변환, 구문 오류를 포함한다. 관련 3개 CPython 출력 일치. 기본 227개는 222개 일치,
가변 필드 제외 3개, native CLI 2개. Godot 직접 소스 빌드와 관련 2개 통과.
검증 뒤 호출되지 않는 기존 FormatValue helper만 삭제했다.

10,000회, 15 samples, 3쌍, affinity 0x4, tiering off:

| 작업 | 이전 ms | 이후 ms | 이전 B | 이후 B |
| --- | ---: | ---: | ---: | ---: |
| 단일 float 필드 | 10.8060 | 9.7621 | 7,041,184 | 5,441,184 |
| 위치+keyword 필드 | 12.8188 | 11.4591 | 13,281,272 | 12,321,272 |
| 정수 loop 대조군 | 3.5776 | 3.5770 | 801,176 | 801,176 |

대상은 각각 약 9.7%, 10.6% 단축, 반복당 160B/96B 감소했다.
대조군 시간 범위는 겹친다. baseline은 125차이며 사이 126차 set 필드 압축은
이 workload에 set 생성이 없어 영향을 주지 않는다. 원자료 round127-validation.json.

변경 가능한 locale, CLR 대형 문자열 한계는 이번 수정에 포함하지 않았다.
