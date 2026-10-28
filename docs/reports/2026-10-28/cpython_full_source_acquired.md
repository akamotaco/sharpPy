# CPython 3.12.15 전체 소스 확보 (2026-10-28)

## 확보물

- 위치: `../cpython-v3.12.15` (sharpPy 형제 디렉터리)
- 태그: `v3.12.15` = commit `e848e4b09ca4bc67c0041c9ce7526dae471d4c36` (detached, 고정)
- 규모: 트래킹 파일 4,744개 / 139MB (`.git` 포함), shallow depth 1
- 참조 런처: `C:\ProgramData\miniforge3\envs\sharppy-test-py312\python.exe` = **3.12.15** → 소스 태그와 패치 레벨 일치

## 왜 기존 스냅샷으로 부족했나

`.artifacts/cpython{,-locals,-structural}` 은 fetch 스크립트로 당긴 **표적 파일 스냅샷**
(중복 포함 78파일, 매니페스트 sha256 검증 완료)이었다. 듀던더 노출·프로토콜 작업에는
`Objects/descrobject.c`(getset/method 래퍼), `Include/cpython/object.h`(tp_ 슬롯 정의),
`Objects/listobject.c`/`dictobject.c`/`setobject.c`, `Python/errors.c` 등 **인접 파일이 필수**인데
그게 빠져 있었다. 이제 전체 트리가 있다.

## 교차 검증 (전체 클론 ↔ 기존 스냅샷)

`Objects/typeobject.c` 등 46개가 initially MISMATCH → 원인은 **Windows `core.autocrlf=true`
체크아웃이 LF→CRLF 변환** (CR 10,763개 삽입, 바이트수 339,625 vs 328,862).
`git ls-files | xargs rm` 후 `git -c core.autocrlf=false checkout -- .` 로 LF 재물질화하니
**72/72 byte-identical, MISMATCH 0**. 즉 기존 스냅샷도 공식 v3.12.15와 동일했고,
불일치는 전부 개행 변환 artifact였다. 잔여 CR 포함 파일 112개는 리포에 실제로 CR을 가진 파일
(`git status` 클린)이며 스냅샷 검증과 무관.

교훈: sha256 비교는 **개행 정규화 후** 해야 하며, 윈도우 체크아웃은 `core.autocrlf=false` 강제.

## 이제 참조 가능한 핵심 파일 (예)

| 목적 | 파일 |
|---|---|
| 슬롯 정의/slotdefs/inherit_slots | `Objects/typeobject.c` |
| getset·method 서술자(듀던더 노출의 C 모델) | `Objects/descrobject.c` |
| 타입 슬롯 레이아웃 | `Include/cpython/object.h`, `Include/object.h` |
| 리스트 아이템 프로토콜 | `Objects/listobject.c` |
| 딕셔너리/셋 | `Objects/dictobject.c`, `Objects/setobject.c` |
| 예외 클래스/`args` | `Objects/exceptions.c` |
| 예외 발생/매칭 | `Python/errors.c`, `Python/ceval.c` |
| 숫자/포맷 | `Objects/longobject.c`, `Python/formatter_unicode.c` |
| 게이지 프로토콜 | `Objects/iterobject.c`(calliter), `Python/bltinmodule.c`(map/zip strict) |
