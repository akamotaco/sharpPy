# SharpPy 작업 지침

작업 전에 [현재 계획](docs/evaluation_and_performance_plan.md)의 최신 상태와
[2026-10-13 판단 오류 기록](docs/reports/2026-10-13/process_failures_and_corrective_actions.md)을 읽는다.
사용자의 최신 지시가 이 문서보다 우선한다.

- CPython 3.12의 관찰 가능한 처리 순서, callback, 예외, 참조 수명과 결과를 기준으로 한다.
  C 자료구조나 bytecode를 그대로 복제하는 것이 목적은 아니다.
- 수정 전에 원래 실패 사례와 완료 조건을 고정하고, 고정 revision의 CPython 소스에서
  해당 진입점과 호출 경로를 확인한다. 수정 뒤 같은 사례를 다시 실행한다.
- 새 통과 사례, 커밋 수, 문서 작성량으로 원래 실패의 해결을 대신하지 않는다.
  범위를 줄였으면 원래 요청은 부분 해결로 남긴다.
- 한 번에 하나의 문제를 끝낸다. 현재 우선 대상은 namespace의 중복 저장·동기화 경로다.
  선행 문제가 필요하면 의존 관계를 기록하고, 관련 없는 새 항목으로 이동하지 않는다.
- 새 adapter/cache/fallback을 추가하기 전에 기존 경로를 교체·삭제할 수 있는지 확인한다.
  호환 경로를 유지하려면 실제 호출자, 필요한 의미, 제거 조건을 기록한다.
- 변경의 실행 코드 추가·삭제량, 남은 중복 경로, 시간·할당의 개선과 악화를 함께 보고한다.
  미해결 성능 저하를 기록만 한 뒤 최적화 완료로 처리하지 않는다.
- CLR 수거 후 정리와 마지막 Python 참조 삭제 직후 해제를 구별한다.
  전체 PyObject 참조 카운팅이나 강제 GC를 근거 없이 도입하지 않는다.
- .NET과 Godot의 소스 포함 빌드·실행을 유지한다. SharpPy 전용 DLL 배포를 전제하지 않는다.
  수정된 Lib의 기존 의도를 확인하며 CPython 라이브러리로 일괄 교체하지 않는다.
- 현재 상태는 계획 문서 상단 한 곳에서 관리한다. 과거 보고서와 원자료는 이력으로 보존한다.
  기본 통과 회귀와 별도 compatibility 실패, 최종 코드 검증과 이전 코드 검증을 구분한다.

## 변환 API 관례 (PyObject)

- `To~()` = **C#(CLR) 값** 추출: `ToInt()`→`int`, `ToFloat()`→`double`, `ToBool()`→`bool`,
  `ToClrString()`→C# `string`.
- `As~()` = **Python 타입** 변환: `AsInt()`→`PyInt`(임의정밀), `AsFloat()`→`PyFloat`,
  `AsBool()`→`PyBool`, `AsString()`→`PyString`(= Python `str()`).
- `ToStr()`은 `AsString()`으로 위임하는 **비가상 호환 별명**이다(p39). 서브클래스는
  반드시 `AsString()`을 override 하며, 그래서 `ToStr`/`AsString` 이중 경로가 생기지 않는다.
- `AsString()`/`ToRepr()`는 CPython `PyObject_Str`/`PyObject_Repr` 의미: 특수메서드가
  **없을 때만** repr로 폴백한다. 호출 중 예외는 전파, 반환이 str이 아니면 `TypeError`.
- `As~`는 절대 `To~` 캐스트로 bound되어선 안 된다. 예: `AsInt()`는 `ToInt()`(Int32)가
  아니라 BigInteger로 절단해야 큰 float에서 넘치지 않는다 (p35/p36).
- `ToString()`은 예외: **디버깅 전용 identity**(`<type object at 0x…>`). Python `str()`과
  무관하며 `ToStr()`으로 위임하지 않는다.
- 신규 코드는 `AsString()`을 쓰고 `ToStr()` 별명은 기존 호출부 정리 전까지 유지한다.
  과거 커밋의 `AsString()`은 C# `string`을 반환했던 이름이므로 이력 조회 시 혼동 주의.

## 실수 방지 대책 (2026-10-28 p16 세션 사고 기반)

- 셸 명령은 한 번에 하나 동작. here-document 중첩 금지(직전 사고: `||` 대체 실행 안에
  heredoc을 넣어 명령이 중간에 중단됨). 인라인 Python 스크립트가 필요하면
  `.artifacts/tmp/*.py`로 먼저 작성한 뒤 실행한다.
- 소스 계측·임시 패치는 working tree를 직접 두드리지 않는다(git worktree/stash 사용).
  부득이 수정했다면 즉시 `git status --porcelain`과 `git diff --stat`으로 의도한 파일만
  바뀌었는지 확인하고, 실험 종료 후 반드시 되돌린다.
- 검증(읽기) 명령과 변경(쓰기) 명령을 같은 호출에 섞지 않는다. 재빌드·diff·비교는
  변경이 완료·확인된 뒤 separately 실행한다.
- 프로브 임시 파일은 저장소 루트에 만들지 않고 `.artifacts/tmp`의 고유 디렉터리
  (tempfile.mkdtemp)를 쓰며 finally로 정리한다. 이전 크래시가 남긴 잔여물은
  재실행 전 삭제해야 `mode='x'`·rename 계열 판정이 오염되지 않는다.
- 가드·임계값 초과는 상향 전에 3가지를 먼저 실측한다: Release wall-clock, Debug
  wall-clock, 발동 시점( slice fixture 실측 0.5s). 명령어 수 가드는 시간이 먼저
  걸리는 장치가 아니므로 "느려서 30분" 같은 해석은 측정으로 기각한다.
- 회귀 의심은 HEAD 대조(stash/worktree 재빌드)로 "신규 변경 vs 기존 상태"를 분리한 뒤
  원인을 선언한다. Godot 기본 프로브 60k 초과가 p16 회귀가 아니었던 절차가 표준.
