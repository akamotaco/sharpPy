# 라이브러리 surface 정책 (2026-10-29 확정)

## 정책

**CPython 표준 라이브러리의 전면 호환은 목적이 아니다.** 필요한 surface를 **C#의 특징을 고려해
구현**한다. CPython `Lib` 복사는 수단의 하나일 뿐이며, 복제 자체를 목표로 삼지 않는다.

- 필요한 것은 C#으로 구현한다. CLR이 이미 제공하는 것(임시 경로, 인코딩, 정규식, 숫자 포맷, 파일
  API)을 Python 코드로 다시 감싸지 않는다.
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

- **`tempfile` 미존재** — 정책상 결함이 아니다. 필요해지면 C# native module로 만든다. CLR은
  `Path.GetTempPath()`/`Path.GetRandomFileName()`을 이미 갖고 있으므로 CPython의 `_os` 조합 로직을
  Python으로 복제할 이유가 없다. Windows에서 `GetTempFileName`은 65,536개 고갈 시 `WinError 206`,
  `TemporaryDirectory` 정리 실패는 `PermissionError` 계열 — 메시지는 CPython에 맞춘다.
  Godot에서는 임시 경로가 `user://` 관례와 충돌할 수 있으므로 `res://`/`user://` 정책을 따른다.
- **P0-8 `io.TextIOWrapper(buffer, encoding=...)`** — `Lib/io.py`를 CPython 버전으로 교체하지 않고
  `modules/IoModule.cs`의 생성자 semantics로 해결한다. `Lib/io.py`는 이미 CPython보다 큰 shim이므로
  교체는 shim을 덮어쓸 위험이 있다.
- **P0-9 importlib 커스텀 loader/spec** — `importlib._bootstrap` 전체 이식이 아니라, SharpPy가
  실제로 호출하는 진입점(`ImportlibModule.cs`)에서 관찰되는 loader 프로토콜을 C#으로 지원한다.
- **P1 stdlib surface 항목들**은 "필요성" 기준으로 판단하고, 필요하지 않으면 미구현으로 남긴다.

## 판단 기준 (새 항목 검토용)

1. C#/CLR이 이미 제공하는가? 제공하면 C#에서 쓰고 Python shim을 만들지 않는다.
2. CPython `Lib`에 같은 이름 파일이 SharpPy에 이미 있는가? 있으면 **그 파일의 의도**를 먼저 확인하고
   부분 수정한다. 통째 교체는 금지.
3. 관찰 surface(함수·속성·예외 메시지) 중 실제로 프로그램이 사용하는 범위는 어디까지인가? 그 범위만
   만든다. 나머지는 known_gaps에 "의도적 미구현"으로 남긴다.
