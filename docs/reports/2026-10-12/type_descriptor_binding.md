# type descriptor 바인딩 (95차, 작업 2)

`type.__setattr__(Number, name, value)`가 type 자체에 미리 바인딩되어 인자가 한 칸 밀리던 문제를 수정했다. native 데이터 descriptor만 metatype을 receiver로 받으며 일반 메서드 descriptor는 명시적 receiver를 받는다. 읽기 전용 getset도 CPython처럼 데이터 descriptor다. `type.__new__`는 기존 구현을 static builtin으로 노출해 명시적 cls를 유지한다. 참고: CPython 3.12.15 typeobject.c의 `_Py_type_getattro_impl`, descrobject.c의 getset descriptor.

부모·자식 int 비교 메서드 변경과 캐시 무효화, 명시적 type 메서드, metaclass와 클래스 생성 회귀를 추가했다. 전체 165개 실행 중 새 fixture의 `type.__dict__` 항목 누락이 발견됐다. 바인딩과 별개인 기존 사전 노출 차이는 `metaclass_attribute_remaining.py`에 보존하고, 수정된 fixture만 재실행해 일치를 확인했다. 최종 판정은 출력 일치 160, 변동 필드 정규화 3, native 검사 2다. Godot 소스 포함 빌드와 기본 17개·관련 fixture 21개 통과. 속도 개선은 주장하지 않는다.

원시 전체 실행·최종 대상 재검증·Godot 결과는 `round95-validation.json`에 보존했다. 사용자 클래스의 명시적 repr, 사용자 metaclass property 우선권, type 사전의 일부 native 항목 누락은 별도 실패 fixture로 남는다. 작업 3~6은 미완료다.
