# 122차: warnings와 async generator의 오류 발생 과정

CPython 3.12.15 _warnings.c 및 genobject.c와 실제 실행을 비교했다.

- warn의 stacklevel을 index protocol과 native ssize 범위로 변환한다.
  1000단계처럼 caller를 넘어가면 None.f_back 대신 sys:1로 귀결된다.
- filters가 list인지, 항목이 5-tuple인지, action이 str인지 같은 시점에 검사한다.
  native list/tuple 저장소를 직접 읽어 하위 타입의 __len__/__getitem__/__iter__를
  호출하지 않는다. str 하위 타입 matcher는 정확한 str literal과 구분하여 match를 호출한다.
- Warning 하위 클래스 생성에서 BaseException.__init__이 self 없이 호출되던
  문제를 method descriptor로 고쳤다. 기존 native exception의 명시적 초기화도
  args를 변경하며, 문자열 변환 callback은 registry 초기화 전에 실행된다.
- tuple.__len__ descriptor를 연결해 위 native tuple 관찰을 지원한다.
- async generator.athrow는 인자 수와 상관없이 awaitable을 생성한다.
  2개 이상이면 생성 시 DeprecationWarning을 내고, 0개/4개 인자 오류는 처음
  await할 때 검사한다. 이미 종료된 generator의 athrow()는 검사보다 종료가 먼저다.
  CPython의 잘못된 인자 경로 상태를 임의로 정리하는 코드를 추가하지 않았다.

기존/신규 관련 회귀 7개와 filter 하위 타입 확장 fixture가 일치한다.
Godot 소스 빌드, 기본 17+관련 3개, 경로 5개 통과. 수정 warnings.py를 검증
프로젝트에도 복사했다. 원시 실패·성공은 round122-validation.json.

경고 오류 경로의 호환성 수정이며 속도 개선을 주장하지 않는다.
async generator hooks/최종화, 전체 warnings 동적 변이, 모든 set API와
descriptor 오류까지 감사 완료한 것은 아니다. 신규 2개 fixture를 기본 검사에 등록했다.
