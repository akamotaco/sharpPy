# 114차: memoryview 자동 해제의 소유권 경계 — 미해결

이 항목은 구현 완료가 아니다. v3.12.15 Objects/memoryobject.c의
memory_dealloc → _memory_release → mbuf_release 경로와 현재 PyBuffer.cs를
비교했다. CPython은 마지막 Python 참조 해제 시 dealloc을 수행하지만
SharpPy에는 참조 획득/소유권 이전/반납을 추적하는 공통 계층이 없다.
CLR 객체가 나중에 수거되는 것과 Python에서 마지막 참조가 사라지는 것은 다르다.

신규 memoryview_owner_lifetime_remaining.py는 각 경계를 독립적으로 출력한다.
CPython 3.12.15에서 다음 9개 경우 즉시 resize가 가능하지만 SharpPy는 모두
export가 남아 BufferError를 발생시킨다.

1. 마지막 alias 삭제
2. list.clear
3. dict 항목 삭제
4. 마지막 tuple 소유자 삭제
5. 함수 프레임 종료
6. closure 함수 삭제
7. memoryview의 bound method 삭제
8. 마지막 child view 삭제
9. 사용하지 않는 memoryview 표현식의 POP_TOP

같은 fixture의 live alias/list/dict/frame/closure/method/child 7개는 모두
resize를 막는다. 기존 명시적 release 및 alias/child 보호 2개 fixture는 일치한다.
원시 결과는 round114-validation.json이며 실패는 기본 통과 회귀에 등록하지 않았다.

## 필요한 구조 변경

| 소유권 경계 | 현재 직접 저장 경로 | 필요한 계약 |
|---|---|---|
| 평가 스택/임시 인자 | PyStack, PyObject[] | borrowed/new/stolen reference 구분, 예외 unwind 반납 |
| local/global/cell | LocalsPlus, Variables, PyCell.Value | 교체 시 새 참조 선취득 후 이전 참조 반납 |
| 컨테이너 | list/tuple/dict/set, 객체 속성 | 요소 제거뿐 아니라 소유 컨테이너의 마지막 참조 반납 전파 |
| 프레임/함수 | 보존 frame, closure, generator | 일반 종료와 suspended/외부 관찰 frame 구분 |
| 호스트 | 공개 C# 컬렉션 및 반환 PyObject | 외부 소유 참조를 명시적으로 acquire/release하는 API |

memoryview만 참조 수를 세면 컨테이너 자체가 사라질 때의 해제를 놓친다.
모든 PyObject에 무조건 counter를 추가하는 변경은 작은 숫자 할당까지 영향을
주므로 lifetime 대상과 참조를 보유하는 객체의 비용을 분리 측정해야 한다.
순환 참조는 비순환 참조의 마지막 반납과 별도로 다뤄야 한다.

DELETE_NAME에서 무조건 release, resize 때 전체 객체 그래프 탐색,
GC.Collect 강제 실행, finalizer만 추가하는 방식은 채택하지 않았다.
첫 방식은 alias를 깨고, 나머지는 release 시점/과정이 CPython과 다르다.
이 설계 범위를 연결하지 않은 부분 구현을 자동 해제 해결로 표시할 수 없다.
런타임 변경은 없으며 직전 Godot 소스 빌드와 전체 회귀 결과를 재사용한다.
