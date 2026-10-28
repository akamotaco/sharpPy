# Godot 4.7.2 mono 빌드 테스트 (work/bug-hunt HEAD c40b586)

## 버전 확정

- 최신 안정 Godot: **4.7.2 stable mono** (`4.7.2.stable.mono.official.ed1daf0bf`,
 本机 `Desktop/work/Godot_v4.7.2-stable_mono_win64`).
- 4.7 계열 요구사항: .NET 8 (csproj `net8.0`, Godot.NET.Sdk **4.7.2**, 로컬 nupkgs 소스).
- 참고: 4.8 dev/beta(2026-10)는 최소 **.NET 10**으로 상향되었다는 공지 확인.
  4.8 안정화 시 SDK 마이그레이션이 별도 과제가 된다.

## 빌드 결과 (현재 HEAD, 전체 소스 직접 포함 구성)

`tools/PerformanceHarness`가 아니라 `.artifacts/round30-godot` CompatibilityProbe
프로젝트(SharpPy C# 소스 직접 포함, DLL 참조 없음) 기준.

- `dotnet build -c Release -t:Rebuild`: 오류 0, 경고 0 (Godot.NET.Sdk 4.7.2, SDK 8.0.425)
- `dotnet build -c Debug -t:Rebuild`: 오류 0, 경고 0
- 실행 호스트: Godot 4.7.2이 net8.0 어셈블리를 **`.NET 10.0.12` 런타임**에서 구동
  (roll-forward; TFM net8 유지로 최신 런타임에서 실행됨)

## headless 실행 (Release 시맨틱 dll)

- 기본 회귀 스위트(17종 fixture + 경로 검사): `SHARPPY_GODOT_PASS`, exit 0,
  `SHARPPY_PATH_CHECKS 5`
- 기록 기준 재생 `--replay`: `REPLAY_RESULT 82 matched, 0 other` — 기록된
  CPython stdout과 전 건 일치
- 추가 fixture (당일 strmeth 23 케이스: splitlines/casefold/title/startswith
  tuple/center/encode): Godot 내부 출력도 CPython 3.12.15 산출과 바이트 일치

## 관찰 — Debug 전용 instruction 가드

`PyVM`의 DEBUG 전용 무한루프 가드(약 5만 명령어)가
`slice_delete_compaction_regression.py`를 60000에서 trips한다. Release에는
없는 가드이고 같은 fixture는 CLI/evaluate에서 통과하므로 정확도 실패는 아니다.
2026-10-12 빌드(당시 Debug dll)로는 같은 스위트가 통과했으므로, 10-13 이후
커밋 어딘가에서 이 fixture의 명령어 소모가 증가한 것이다. 후속 과제:
`a40d72c` 대비 명령어 수 측정(원인 지목 전 원인 조사 먼저). 가드 상수는
임의 값이라 인상으로 '해결'하지 않는다.

## 결론

프로젝트의 Godot(dotnet) 빌드 목표는 **Godot 4.7.2 stable mono + .NET 8(TFM
net8.0, Godot.NET.Sdk 4.7.2)** 로서 현재 HEAD에서 오류/경고 0, headless 전 suite
통과. 4.8 계열은 .NET 10 요구가 확정되기 전까지 미적용.
