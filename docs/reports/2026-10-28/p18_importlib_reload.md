# p18 — importlib.reload 실동작화 + module repr (2026-10-28)

## 스코프
`tests/bughunt/p18_importlib_reload.py`: reload 반환/정체성, dict 미클리어 재실행,
실패 시 부분 갱신·원 예외 전파·sys.modules 잔존, 인자/타입 에러, builtin(sys) reload.
기준 CPython 3.12.15 (Lib/importlib/__init__.py reload).

## 수정 전
`reload`는 스텁: 모듈을 그대로 반환(재실행 없음). → 소스 변경 후 reload 해도 새
정의(NEW)가 반영 안 됨. `repr(module)`도 기본 `<module object at 0x..>`.

## 수정
1. **`PyModule.ExecuteForReload(source)`** (`core/PyModule.cs`)
   - 기존 `ModuleDict`에 parse→compile→exec(exec-in-place, dict 미클리어).
   - **원 예외 그대로 전파**(ImportError 래핑 없음), **sys.modules 제거 없음**.
   - CPython reload은 실패 시 부분 갱신(A=2) 후 예외, 잔존 — 재현.
2. **`reload` 재작성** (`modules/ImportlibModule.cs`)
   - positional-only: 0개 `reload() missing 1 required positional argument: 'module'`,
     2개+ `reload() takes 1 positional argument but N were given`,
     비모듈 `reload() argument must be a module`, kwargs `... unexpected keyword argument 'k'`.
   - `HasSourceFile`이면 `FileName`을 읽어 `ExecuteForReload`, 항상 같은 모듈 반환.
   - builtin은 파일 없음 → 그대로 반환 = `reload(sys) -> sys`.
3. **`import_module` 시그니처 메시지**: 0개→`missing ...: 'name'`, 3개+→`takes from 1 to 2 ...`(cp 측정 일치).
4. **module repr** (`core/PyModule.cs`)
   - `HasSourceFile` 필드 도입(LoadModuleFromFile에서만 true; builtin은 false).
   - `ToString`: 파일 모듈 `<module 'n' from 'path'>`, builtin `<module 'n' (built-in)>`.
   - `ToRepr` override → repr(module)==str(module) (CPython과 동일; 기존 repr은 주소형).

## 근거
- CPython reload은 `exec(code, module.__dict__)` — dict를 지우지 않으므로 stale 속성 잔존
  (probe `v2 ... keep`), `COUNT+1`이 1이 됨. 정체성·sys.modules 유지.
- reload 실패는 원 예외 그대로(ZeroDivisionError), 부분 갱신, sys.modules 잔존(측정).
- module repr: 파일 `from`, builtin `(built-in)` (Objects/moduleobject.c module_repr).
  SharpPy는 빌드 freezing이 없어 `os`는 파일 로드 → `<module 'os' from '..os.py'>`.
  CPython 배포본은 `os`가 frozen이라 `(frozen)`. 이는 배포 산출물 차이로 미추적(프로브 외).

## 검증
- p18 stdout **diff 0**(CR 정규화). failing-reload 케이스 포함.
- bughunt **25/25**, evaluate `225 + 3 + 2`(불변).
- Godot 4.7.2 mono replay(Release DLL) **82 matched, 0 other**.
- perf: 노이즈 내(예외·hot 경로 무관).

## 알려진 한계
- frozen stdlib(`os` 등) repr은 배포본 `(frozen)`과 차이 — SharpPy는 파일 로드 전제.
- namespace 패키지 repr은 `(namespace)`(CPython은 `from [...]` 목록) — 프로브 범위 외.
- reload은 `__spec__` 기반 로더 재호출이 아니라 `__file__` 재실행 기반. 스펙 커스텀
  로더 시나리오는 미지원(SharpPy 임포트 체계가 파일 로더 단일).
