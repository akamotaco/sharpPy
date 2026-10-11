# 100차: metaclass 속성 우선권과 명시적 type repr

기준은 로컬 CPython v3.12.15 `Objects/typeobject.c`의
`_Py_type_getattro_impl`, `type_repr`, `type_dict`와 실제 3.12.15 실행이다.

- 클래스 자체 속성보다 metaclass의 data descriptor를 먼저 호출한다.
  기존 MRO 조회·descriptor helper를 재사용한다. 부모 변경 시 하위 타입을
  무효화하지 않는 기존 메서드 캐시는 이 새 경로에 사용하지 않는다.
- 명시적 `type.__repr__`는 클래스의 module/qualname metadata를 직접 사용한다.
  builtins·문자열 아닌 module·빈 module의 CPython 차이를 보존한다.
- `type.__dict__`에 빠진 native descriptor를 초기화 시 연결한다.
  기존 metaclass 생성용 `__new__` 등은 보존한다.
- 원래 실패 fixture와 data/nondata/property 우선권, 상속, 부모 변경,
  descriptor 오류, descriptor identity를 기본 회귀에 등록했다.

검증: Release 빌드, 기본 회귀 173개, Godot 소스 직접 빌드와 기본 17개 및
관련 fixture 3개. 원시 결과는 `round100-validation.json`에 보존한다.
성능 개선율은 주장하지 않는다. 속성 읽기에 필요한 metatype 우선 조회가 추가됐다.

추가 발견: 컴파일러가 중첩 클래스의 qualname을 단순 이름으로 생성한다.
`tests/compatibility/nested_class_qualname_remaining.py`로 보존했으며 이번
repr metadata 소비 수정과 별개의 컴파일러 과제로 추적한다.
type의 ClassDict/TypeDict 전체 통합과 모든 descriptor identity를 완료한 것은 아니다.
