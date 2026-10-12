# 130차: CLR 수거 이후에도 남던 buffer export 정리

후속 1번의 부분 수정이다. Python 마지막 참조 해제 시점의 일치는 아직 해결하지 않았다.

2026-10-13 재평가: 이 GC 누수 수정을 원래 즉시 해제 문제의 해결로 대체해서는 안 된다.
원래 실패를 남긴 채 다른 부분 수정으로 이동한 판단 오류와 재발 방지 기준은
[별도 기록](../2026-10-13/process_failures_and_corrective_actions.md)에 명시했다.

native 검사에서 WeakReference로 view가 실제 수거된 것을 확인한 뒤에도 bytearray
resize가 막혔다. GC 지연만의 문제가 아니라 기존 ReleaseBufferExport 호출 경로가
누락되어 카운터가 영구히 남는 문제였다.

bytearray를 참조하는 view에만 finalizer를 가진 내부 타입을 사용한다. 명시적
release는 finalization을 억제하며, 수거 시에는 Python callback 없이 기존 export
카운터를 반납한다. bytes/고정 크기 배열 view는 finalizer가 없다. Python에서 보이는
타입은 memoryview로 동일하다. 자식 생성과 실패한 slice 정리에도 같은 규칙을 쓴다.

finalizer thread와 동시 접근하므로 export 증감은 Interlocked, resize 검사는 Volatile로
변경했다. native span만 남는 IO/복사 구간에는 GC.KeepAlive로 view의 export 소유권을
유지한다. 전체 PyObject 참조 카운터나 추가 필드는 도입하지 않았다.

## 검증

- 수거된 view/child의 export 반납, 살아 있는 view 보호, 명시적 중복 release 후
  새 view의 보호를 native 검사로 확인했다. 강제 GC는 검사 도구에만 있다.
- memoryview 관련 7개와 파일 IO 1개 출력 일치. 기존 즉시 해제 fixture 2개는 여전히
  실패/차이다. 따라서 CPython의 자동 해제 완료로 분류하지 않는다.
- 기본 230개는 225개 출력 일치, 가변 필드 제외 3개, native CLI 2개. 이 전체 검사는
  마지막 KeepAlive 두 곳 추가 전이며, 추가 후 관련 8개와 native 검사/Godot을 재검증했다.
- Godot 직접 소스 빌드, 기본 17개·경로 5개·관련 3개 통과.

10,000회 생성/release, samples 15, 독립 3쌍, affinity 0x4, tiering off:

| 작업 | 이전 ms | 이후 ms | 할당 |
| --- | ---: | ---: | --- |
| mutable view | 9.6208 | 9.8494 | 2,321,184B 동일 |
| immutable view | 9.6305 | 9.6288 | 2,321,184B 동일 |
| 정수 loop | 3.6132 | 3.5359 | 801,176B 동일 |

mutable은 약 2.4% 지연, 반복당 약 23ns의 정리 비용이다. immutable 시간 범위는
겹친다. 이 측정은 명시적 release 경로이며 대량 미해제 객체의 finalizer 처리량을
측정한 것은 아니다. 수거 후 누수를 고친 비용으로 기록하며 최적화라고 주장하지 않는다.
원자료 round130-validation.json.

## 남은 결정적 차이

CPython은 비순환 마지막 Python 참조 반납 시 즉시 memory_dealloc → _memory_release를
실행한다. CLR finalizer는 그 시점을 보장하지 않는다. list/dict/tuple, frame/closure,
bound method, host 보존 참조까지 연결하는 소유권 계약은 여전히 필요하다.
이번 수정은 그 계약을 대체하지 않으며 resize에서 강제 GC나 그래프 탐색을 하지 않는다.
