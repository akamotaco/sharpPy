# 57차: SEND와 사용자 iterator의 next 프로토콜

56차를 `9ae7747`로 커밋했다. 이번 변경은 작업 트리에 남긴다.

## 구현

SEND의 None 경로가 CLR PyIterator만 처리하던 제한을 제거해 Python 클래스의 Next도 호출한다. 값이 있는 send는 generator의 직접 Send 또는 일반 send 속성 호출을 사용한다. send의 AttributeError를 삼키고 다음 항목으로 넘어가던 fallback을 제거했다. StopIteration.value로 yield from을 종료하는 기존 경로는 유지한다.

PyClassInstance.Next는 인스턴스 사전의 __next__를 무시하고 타입 MRO에서 찾은 special method에 descriptor 바인딩을 적용한다. 기존 LookupSpecialMethodUncached/BindSpecialMethod를 재사용한다. 사용자 __getattribute__를 호출하지 않으며 하위 클래스 변경을 잘못 캐시하지 않는다. 빈 호출 인자는 Array.Empty로 재사용한다. 메서드가 없을 때 무조건 StopIteration을 만들던 코드도 TypeError로 바꿨다.

list iterator의 GetTypeName을 기존 repr와 같은 list_iterator로 맞춰, send가 없을 때 오류 메시지가 CPython과 일치하도록 했다. 모든 iterator의 공개 타입 객체를 정비한 것은 아니다.

CPython v3.12.15 commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`, Python/bytecodes.c SEND의 None/tp_iternext 분기와 일반 send 호출을 대조했다. special method/descriptor 동작은 실제 CPython 3.12.15 회귀 결과로 확인했다.

## 검증

기본 106개: 출력 완전 일치 101개, 지정된 주소·시간 필드 제외 일치 3개, C# native 검사 2개. 기존 실패 재현과 새 SEND fixture를 기본 목록에 편입했고 이전 DLL에서는 둘 다 실패했다.

회귀 범위는 send 없는 iterator, next/send(None), 값이 있는 send의 결과와 오류, StopIteration.value, instance __next__ 무시, custom __getattribute__ 우회, __next__ descriptor 바인딩 및 오류 전파, list iterator의 send 누락 메시지다. 마지막 send 반환값·descriptor 실패 항목은 fixture 확장 후 따로 재검증했다.

Godot 4.7.2에서 소스 포함 빌드 후 기존 17개 및 추가 17개 fixture와 native range helper가 통과했다. 모바일 실기 검증은 하지 않았다. 전체 출력은 `.artifacts/round57-tests.json`, Godot 로그는 `.artifacts/round57-godot-*.log`에 보존한다.

## 성능

SEND를 사용하는 native range 위임과 기존에도 동작하던 Python iterator 직접 next를 측정한다. 오류가 나던 동작을 생략한 이전 경로와 새 정상 경로의 시간을 비교하지 않는다.

.NET 8.0.31 Release, TieredCompilation=0, workload당 10,000회, warmup 10회, 측정 10회, 이전/이후 3쌍을 순차 실행했다. 동일 harness에 변경 전·후 DLL을 사용했다. 아래 시간은 프로세스별 중앙값 세 개의 중앙값이다.

| workload | 이전 | 이후 | 10,000회 할당(이전 → 이후) |
| --- | ---: | ---: | ---: |
| generator_delegation | 59.642 ms | 59.297 ms | 6,962,560 → 6,962,560 B |
| python_iterator | 24.525 ms | 23.332 ms | 11,043,392 → 10,803,392 B |

Python iterator는 측정 구간에서 약 4.9% 단축됐고 빈 인자 배열 제거로 next당 24 B, 10,000회에서 240,000 B가 줄었다. Native 위임의 약 0.6% 시간 차이는 작으므로 속도 개선으로 주장하지 않는다. 모든 사용자 descriptor의 성능을 보증하는 측정은 아니다. 원시 자료와 fixture 해시는 [round57-validation.json](round57-validation.json)에 보존한다.

## 다음 순서와 한계

__iter__가 iterator가 아닌 값을 반환할 때 초기 검증이 빠져 있다. generator_invalid_iterator.py로 CPython의 정확한 TypeError를 요구하는 재현을 보존한다. 실제 CPython은 통과하고 이전·현재 DLL 모두 실패하며 기본 통과 목록에서는 제외했다. 다음은 이 검증과 __iter__ special lookup을 맞추는 작업이다.

56차의 close 속성 조회 오류에 대한 unraisable 처리와 특수 close 재진입 차이, 다른 native iterator의 타입 표현 차이도 남아 있다. 이번 수정은 유효한 iterator의 SEND 경로와 __next__ 호출을 대상으로 한다.
