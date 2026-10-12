# 110차: coroutine 구식 throw 경고와 memoryview 수명 조사

후속 5번은 부분 해결이다. coroutine_throw_legacy_warning.py는 stderr까지
CPython 3.12.15와 일치한다. Objects/genobject.c의 gen_throw에서 인자 수
검증 후, 예외 정규화 전에 경고하는 순서를 적용했다. 경고를 error로 바꾸면
coroutine은 아직 시작되지 않은 상태를 유지한다. wrapper 경로도 검증했다.

원본 genobject.c SHA256:
7c13a1de31fc8af6e082fc9aa055bcfe980786b4104fec0f7564e40735e44cad.
고정 기준은 v3.12.15/e848e4b09ca4bc67c0041c9ce7526dae471d4c36이다.

수정된 Lib/warnings.py를 유지하면서 기본 필터와 simplefilter 변경 세대를
native 경고 경로에 연결했다. ignore/error, 단일 인자 비경고, stderr 교체를
비교했다. 단일 인자 경로는 경고 객체/registry를 만들지 않는다.
warnings.warn, filterwarnings의 패턴 처리, catch_warnings 등 라이브러리 전체를
CPython 수준으로 구현한 것은 아니다. 기존 stub 범위는 별도 감사 대상이다.

memoryview_automatic_release.py는 여전히 실패한다. 현재 export 횟수는
명시적 release에서 줄어들며 마지막 Python 참조 소멸을 알리는 경로가 없다.
DELETE_NAME에서 무조건 release하면 살아 있는 alias/child view가 망가진다.
finalizer나 강제 GC만으로 CPython의 즉시 해제를 보장할 수도 없다.
이를 숨기는 우회 코드는 넣지 않았다. 별칭·자식 view 보호와 명시적 release
회귀를 추가했으며, 자동 수명은 VM 참조 소유권 설계 과제로 유지한다.

기본 204개(199 출력 일치, 3 지정 변동 필드, 2 native CLI) 통과.
Godot는 수정된 warnings.py를 리소스로 포함하고 C# 소스 빌드,
기본 17개+관련 3개 통과. 자동 해제 실패는 통과 집계 밖이며
round110-validation.json의 focused 결과에 원문을 보존했다.
