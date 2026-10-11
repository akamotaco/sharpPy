# NameError와 AttributeError 생성·속성 — 75차

74차를 `35c2e0f` (`fix: preserve positional arguments in basic exception constructors`)로 커밋했다. 이번 변경은 다음 검토를 위해 작업 트리에 남긴다.

## 수정

NameError와 UnboundLocalError는 원래 위치 인자를 args로 보존하고 name 키워드를 별도 속성에 저장한다. AttributeError도 위치 인자를 보존하며 name과 obj 키워드를 별도로 저장한다. 이전에는 위치 인자를 메시지 문자열로 축약하고 이들 키워드를 무시했다.

NameError의 기존 name 저장·조회·수정·삭제 경로를 재사용했다. AttributeError에는 name/obj 슬롯을 추가했다. 둘 다 초기값과 삭제 후 값은 None이고 객체 참조를 그대로 보존한다. args를 변경해도 이 속성들은 바뀌지 않는다. C# 내부 메시지 기반 생성자는 유지했다.

전용 키워드 검사에서는 CPython의 인자 수 제한을 먼저 적용하고 알 수 없는 키워드를 거부한다. UnboundLocalError는 상속한 NameError 생성자의 오류 문구를 사용한다. 따옴표·줄바꿈이 있는 키워드 이름은 repr로 이스케이프하지 않고 CPython처럼 원문을 오류 메시지에 넣는다.

참조는 실제 CPython 3.12.15 및 pinned commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/exceptions.c:NameError_init, AttributeError_init와 member 정의다. 공통 인자 보존 경로를 사용하며 CPython의 임시 빈 tuple 할당은 C#으로 복제하지 않았다.

## 검증

- .NET Release 빌드 성공.
- 기본 142개: 완전 출력 일치 137개, 지정 변동값 제외 일치 3개, native 검사 2개 통과.
- lookup_exception_constructor_regression.py: 세 타입의 빈 인자·빈 문자열·숫자·None·여러 인자, str/repr, name/obj 기본값·identity·대입·삭제·반복 삭제, args 변경과 속성 독립성, raise/catch identity, 키워드 개수·이름 오류를 비교했다.
- 최종 키워드 문자열 보완 후 이 fixture를 .NET과 Godot에서 재검증했다. 이전 DLL에서는 실패한다.
- Godot 소스 포함 빌드 성공. 기본 17개 및 native helper, 추가 54개 fixture 출력 비교 통과. 합계 71개. 기존 traceback fixture 1개는 컴파일 입력 파일명만 res://로 바꾸어 비교했다. 별도 SharpPy DLL 의존성을 추가하지 않았으며 모바일 실기는 실행하지 않았다.
- CFG reachability probe 및 git diff --check 통과.

검증 원본은 [round75-validation.json](round75-validation.json), 상세 로그는 `.artifacts/round75-full.json`, `round75-final-targeted.json`, `round75-godot-comparison.json`에 보존했다.

## 성능

정상 문자열 인자로 NameError와 AttributeError를 각각 1만 개 만드는 workload를 추가했다. 이전 구현과 수정 구현에서 같은 args·str 결과인 경우를 사용했다. 기존 ValueError 생성 workload는 대조군이다.

.NET 8.0.31, TieredCompilation=0, warmup 10회·측정 10회, 전후 교대 3쌍. 빌드·기능 검증과 겹치지 않게 실행했고 workload 반환 합계 검증을 통과했다. 아래 시간은 프로세스별 중앙값 3개의 중앙값이다.

| workload | 이전 ms | 이후 ms | 이전 할당 B | 이후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| NameError+AttributeError 각 1만 회 | 10.338 | 9.920 | 4,561,192 | 3,441,192 |
| ValueError 1만 회 대조군 | 6.775 | 7.212 | 2,001,184 | 2,001,184 |

대상 workload 할당은 총 1,120,000 B 감소했다. AttributeError의 두 전용 참조 슬롯은 객체당 16 B를 추가하지만, 두 예외의 메시지 재포장에 쓰이던 임시 문자열 객체·인자 배열을 없앤 효과가 더 크다. C# 내부에서 생성되는 AttributeError도 이 슬롯 크기는 부담한다.

시간 중앙값은 대상 약 4.05% 감소, 대조군 약 6.45% 증가다. 대상 프로세스 중앙값 범위는 이전 10.077–10.658 ms/이후 9.881–10.174 ms, 대조군은 이전 6.649–6.917 ms/이후 6.584–7.857 ms다. 양쪽 모두 범위가 겹친다. 확실한 개선은 대상의 할당 감소이며 시간 차이를 일반적인 속도 개선 또는 확정적 회귀로 주장하지 않는다.

## 다음 순서와 범위

exception_specialized_arguments.py를 다시 실행했다. NameError와 AttributeError의 빈 args는 이제 일치하며 KeyError('key')의 따옴표 표현, OSError(2, 'missing')의 인자·표현은 계속 다르다. 다음은 KeyError의 원래 key 보존과 단일 인자 repr 규칙이다. OSError는 errno에 따른 하위 타입 선택 등 규칙이 별도여서 이후 분석한다.

이번 범위는 세 builtin 타입의 생성과 인스턴스 member 접근이다. 재호출 __init__, 직렬화, 타입 수준 member descriptor, VM의 모든 자동 오류에 대한 name/obj 채움까지 완전 호환이라고 주장하지 않는다. 기존 builtin 이름 등록, type.__base__, 구식 throw 경고와 나머지 후속 항목을 유지한다.
