# NameError 제안은 표시 시점 것이다 (p06 클러스터 종료)

## CPython 3.12.15 실측으로 확정한 의미
- `NameError` 자체는 항상 맨 메시지다:
  `name 'prtin' is not defined`, `.name = 'prtin'`.
  제안 문자열은 예외 args에 절대 섞이지 않는다.
- `Did you mean: ...?` / `Did you forget to import '...'` 표시는
  traceback **표시 layer** 전용이다 (`Lib/traceback.py:_compute_suggestion_error`).
- 후보 풀은 locals → globals → builtins 순서, 먼저 제안이 나온 풀에서 종료.
  - `self.<name>` 힌트가 먼저다 (`self`가 locals에 있고 `<name>` 속성이면 성공)
  - `__`로 시작하는 후보 이름은 건너뛴다.
  - 후보가 750개 이상이면 포기한다.
  - 임계값: `((len(이름)+len(후보)+3) * 2) // 6`, Levenshtein(이동 2, ASCII
    5bit 동일 시 1)이 임계값 이하인 최소 후보 하나.
- `sys.stdlib_module_names`에 있으면 `Did you forget to import 'x'?`
  (모듈명이면서 근접 후보도 있으면 두 문장 함께).

## 수정 내역
- `core/StdlibModuleNames.cs` (신규, 생성 데이터): 3.12.15 기준
  `sys.stdlib_module_names` 301개를 동결. `sys`에 등록.
- `runtime/PyVM.cs`, `core/PyScope.cs`: NameError 생성을 순수 메시지로 되돌림
  (raise 시점 제안 결합 삭제).
- `runtime/ErrorSuggestions.cs`:
  - `levenshtein_distance` DP를 CPython과 동일 재귀로 정정 — 포트 원본은
    (a) 접두어 trim 후 `b` 윈도우 시작 오프셋을 잃고,
    (b) 행 내 대각선(`distance`) 갱신 순서가 달라
    `prtin5→print`를 5(기각)로 계산했다. 실측 C는 4(허용).
  - 후보 상한/`__` skip/임계값 수식을 CPython과 일치.
  - 표시 전용 `GetDisplaySuggestionForNameError()` 신설: tb 최내부 프레임의
    locals/globals/builtins를 CPython 순서로 훑고 `self.` 힌트를 우선한다.
  - 죽은 raise 시점 헬퍼(`GetSuggestionForNameError`/`FormatNameErrorWithSuggestion`) 삭제.
- `runtime/PyInterpreter.cs`: traceback 표시 마지막 줄에 제안 접미 추가
  (`traceback.format_exception`이 참조하는 `f_locals`에도 동일 데이터 노출).
- `runtime/PyVM.cs` `f_builtins`: 모듈 객체 대신 **builtins 모듈 dict**를
  반환 — `Lib/traceback.py`의 `list(frame.f_builtins)`가 동작한다.
  (CPython은 read-only mappingproxy를 반환; sharpPy는 동기화 dict를 노출 —
   `type()` 이름 차이는 후속 정리 항목.)

## 검증
- `tests/bughunt/matrix_nameerror_suggestions.py`: caught 4종 +
  format_exception 3종 + 미캐치 표시 4종 = CPython과 **바이트 동일**.
- `p06_closures_scopes.py`: 14 → 0 (probe 완전 일치).
- `tools/evaluate.py`: 225 + 3 volatile + 2 self-test 변화 없음.
- bughunt 총량 `30 → 28` (p05 4, p07 10, p08 14).
- perf probe: 전 workload 노이즈 범위 내 (+2.4/+2.2/+6.0/+0.7/…).

## 남은 잔여
- `frame.f_builtins` 타입 이름 (dict vs mappingproxy) 후속.
- p05 4줄(nonlocal 검증, `(1)()` SyntaxWarning), p07 10줄(hybrid 예외
  체이닝/with_traceback), p08 14줄(property 메시지, `__slots__` dict,
  `__init_subclass__` kwargs, bool 우선순위).
