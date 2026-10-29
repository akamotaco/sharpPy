# 라이브러리 surface 정책 (2026-10-29 확정)

## 정책

**CPython 표준 라이브러리의 전면 호환은 목적이 아니다.** 필요하면 아래 순서로 채운다.

1. **CPython에 같은 이름 파일이 있으면 그대로 복사한다.** (예: `Lib/tempfile.py` → `Lib/tempfile.py`)
2. 복사한 파일이 필요한 요소를 점검한다. **기존 SharpPy `Lib`의 다른 py 파일로 충족되면 그걸 쓰고**,
   그 파일도 없으면 그 CPython 파일도 복사한다. Python으로 되는 것을 C#으로 다시 만들지 않는다.
3. **`_thread`·`errno`·`_weakref`처럼 C 전용 구현인 것만 C#으로 구현해 연결한다.** CLR이 제공하는
   primitives(lock, 경로, 인코딩, GC)는 이 단계에서 C# 쪽 구현체로 삼는다.

즉 **Python 우선, C#은 C-전용 잔여분**. 단 1단계의 "복사 우선"은 **없는 모듈**에 대한 규칙이고, 이미
있어서 자기 의도를 가진 파일(예: `Lib/io.py`)을 통채로 교체하는 것은 금지한다 — 그 경우 부분 수정.

- C#이 이미 주는 것을 Python shim으로 두 겹으로 감싸지 않는다. 얹어야 한다면
  실제 호출자와 이유, 제거 조건을 남긴다(AGENTS의 신규 adapter 금지 규칙과 동일).
- 이미 C# native module이 surface를 제공하면 그 위에 Python shim을 얹지 않는다. 얹어야 한다면
  실제 호출자와 이유, 제거 조건을 남긴다(AGENTS의 신규 adapter 금지 규칙과 동일).
- CPython `Lib`에서 가져온 파일은 **가져온 의도**를 확인해서 유지한다. CPython 파일로 일괄 교체하지
  않는다.
- C# 구현이라 해도 **관찰 결과(출력·예외 타입·메시지)는 CPython 3.12.15와 같아야 한다.** 메커니즘
  차이는 허용, 출력 차이는 버그다.

## 실제 구성 (측정치)

- `Lib/**/*.py` **41개 파일 / 18,922 줄**, native C# module **20개**
  (`MathModule.cs`, `IoModule.cs`, `AbcModule.cs`, `ImportlibModule.cs`, `ItertoolsModule.cs`,
  `RandomModule.cs`, `StatisticsModule.cs`, `SysModule.cs`, `NtModule.cs`, `TimeModule.cs`,
  `AsyncioModule.cs`, `BuiltinsModule.cs`, `ModuleRunner.cs` 외)

세 가지 방식이 혼재한다.

| 방식 | 예 | 근거 |
|---|---|---|
| **C# native-only** | `math` | `Lib/math.py` 가 존재하지 않는다. `modules/MathModule.cs` 뿐 |
| **Python + native 위임** | `abc`, `_collections_abc`, `io`, `os`, `re/*`, `types`, `typing`, `collections/abc`, `random`, `tokenize` | 파일이 `import _io` / `_abc` / `_collections_abc` / `_sre` / `_sys` 등을 참조 |
| **CPython 그대로** | `enum.py` 2108줄 = CPython 2108줄, `re/__init__.py` 385 = 385 | 줄 수가 완전히 일치 |

같은 파일 크기 비교로 본 수정 정도:

| 파일 | SharpPy | CPython | 해석 |
|---|---|---|---|
| `abc.py` | 79 | 188 | 축약 |
| `collections/__init__.py` | 460 | 1592 | 축약 |
| `functools.py` | 177 | 1009 | 축약 |
| `_collections_abc.py` | 692 | 1173 | 축약 |
| `warnings.py` | 233 | 598 | 축약 |
| `io.py` | 302 | 99 | **오히려 큼** — re-export 외에 shim이 붙었다 |
| `types.py` | 396 | 333 | **오히려 큼** |

즉 "일부는 py 코드를 가져오고, 일부는 C# builtin으로 보강했다"는 이해가 정확하다.

## 백로그에 주는 영향

라이브러리 항목은 **"CPython Lib를 옮긴다"가 아니라 "필요 surface를 C#으로 채운다"로 스코프한다.**

- **`tempfile` — 정책 3단계를 적용한 첫 사례 (측정).** `Lib/tempfile.py` 956줄, 외부 심볼 사용은
  **4개뿐**이다:

  | 심볼 | 출처 | 처리 |
  |---|---|---|
  | `_errno.EEXIST`, `_errno.ENOENT` | `Lib`/`Modules`에 py 없음(C 전용) | **3단계**: C# `errno` native module (CLR 오류코드→errno 정수 매핑) |
  | `_shutil.rmtree` | CPython `Lib/shutil.py` 1548줄, 자기 deps는 전부 py(os, sys, stat, fnmatch, collections, errno, warnings) | **2단계**: `shutil.py` 복사 (`stat`/`fnmatch`는 추가 복사 또는 축약) |
  | `_thread.allocate_lock` | C 전용 (`_thread` py 없음) | **3단계**: C# `_thread.allocate_lock` (CLR `Monitor`/`Lock`) |
  | `_weakref.finalize` | CPython `Lib/weakref.py` 674줄 (내부에서 `_weakref` C module 사용) | **2단계 + 3단계**: `weakref.py` 복사 + `_weakref` primitives는 CLR GC로 C# 구현 |

  이미 있는 것: `functools`, `warnings`, `io`, `os`, `random`, `sys`, `types`. 즉 **복사 3개
  (`tempfile`·`shutil`·`weakref`) + C# 3개(`errno`·`_thread`·`_weakref`)** 로 열린다. Godot에서는 임시
  경로가 `user://` 관례와 충돌할 수 있으므로 그 정책을 따른다.
- **P0-8 `io.TextIOWrapper(buffer, encoding=...)`** — `Lib/io.py`를 CPython 버전으로 교체하지 않고
  `modules/IoModule.cs`의 생성자 semantics로 해결한다. `Lib/io.py`는 이미 CPython보다 큰 shim이므로
  교체는 shim을 덮어쓸 위험이 있다.
- **P0-9 importlib 커스텀 loader/spec** — `importlib._bootstrap` 전체 이식이 아니라, SharpPy가
  실제로 호출하는 진입점(`ImportlibModule.cs`)에서 관찰되는 loader 프로토콜을 C#으로 지원한다.
- **P1 stdlib surface 항목들**은 "필요성" 기준으로 판단하고, 필요하지 않으면 미구현으로 남긴다.

## 판단 기준 (새 항목 검토용)

1. CPython에 같은 이름 py 파일이 있는가? 있고 SharpPy에 없으면 **그대로 복사**한다.
2. 복사한 파일의 import와 사용 심볼을 나열한다. SharpPy `Lib`에 이미 있으면 그대로 쓰고, 없으면 그
   CPython 파일도 복사한다. 이미 SharpPy에 있는 파일이면 **그 파일의 의도**를 먼저 확인하고 부분
   수정한다(통째 교체 금지).
3. 남은 것 중 C 전용(`_thread`, `errno`, `_weakref`, `_io`, `_sre`, `_abc` …)만 C#으로 구현해 연결한다.
   C#에서는 CLR이 제공하는 primitives를 쓰고, 관찰되는 오류 타입과 메시지는 CPython에 맞춘다.
4. 관찰 surface 중 실제로 필요한 범위를 정한다.넘치는 부분은 known_gaps에 "의도적 미구현"으로 남긴다.
