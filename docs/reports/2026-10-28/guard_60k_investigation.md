# 60k 명령어 가드 조사 (2026-10-28)

## 질문
Godot 4.7.2 헤드리스 기본 프로브가 `slice_delete_compaction_regression.py`에서
`Instruction limit exceeded: 60000`으로 실패. AGENTS 지침상 가드 상향 전에
"명령어 수 증가" 여부를 조사해야 했다.

## 방법
규칙대로 계측은 별도 worktree(`../sharpPy-instr`)에서만 수행했다.
계측 패치(`.artifacts/tmp/instr_patch.py`): 가드 사실상 무력화 + 프레임 탈출 시
`instructionCount`를 stderr에 출력. 저장소 working tree는 건드리지 않았다.

## 사실
1. **증가 없음**: fixture의 모듈 프레임 명령어 수
   - `a40d72c` (Claude-era baseline): **365,172**
   - HEAD (`a7d9b2d`, p1~p16 포함): **365,172** (완전 동일)
   → p era 회귀 가설 기각. 이 fixture는 처음부터 60k 가드를 초과했다.
2. 가드 이력: `e26e46d`(2025-09)에서 1,000,000으로 도입 → `2e55141`
   (2025-11-18, "fix bug : chache jump")에서 **50,000으로 하향**. 60,000에
   발동하는 것은 `% 10000` 검사 간격의 부수 효과.
3. 가드는 EXECUTE된 명령어뿐 아니라 CACHE 슬롯도 센다. `int_loop`(200k 반복)
   하나만으로도 단일 프레임이 1M을 넘므로, 고정 개수 상한은 어떤 값에서도
   정상 스크립트에 오탐을 낸다(1M로도 부족).
4. Godot round30 "PASS"는 Debug 어셈블리(=DEBUG 정의)가 아니었던 상태에서
   관측된 것. Debug 어셈블리로는 가장 오래된 커밋에서도 동일 실패해야 한다.
5. 부수 확인: Debug 어셈블리는 stdout에 진단 로그를_print_하기 때문에
   `-- --replay`(녹화 stdout과 문자열 비교)에는 **Release** DLL이 표준이다.
   Debug DLL 재실행 시 30/82만 매치 — 전부 `[TEMP]` 로그 오염 때문으로,
   Release DLL 재배치 후 **82 matched, 0 other** 복원. VM 회귀 아님.

## 수정 (최소)
`runtime/PyVM.cs` DEBUG 가드에서 **개수 상한 제거**, **시간 가드 유지**:
- 탈출 조건: 30초 (기존 검사 주기 그대로, runaway 감지의 실질 신호)
- 초과 시 기존 diagnostic ring buffer(last 100 instructions) + frame/IP 출력 유지
- 실행 코드 순감(-9줄), Release에는 영향 없음(`#if DEBUG`)

## 검증
- Debug `slice_dbg.py`: **1440 case PASS**, 2.6s
- Debug 무한루프(`while True`): 30.0s에 `Execution timeout after 30.0 seconds
  (5770000 instructions)` + 링 버퍼 덤프 — 루프 감지 기능 정상
- Godot 기본 프로브(Debug DLL): `SHARPPY_GODOT_PASS`
- Godot replay(Release DLL을 Debug 경로에 재배치): `REPLAY_RESULT 82 matched, 0 other`
- bughunt **23/23**, evaluate `225 + 3 + 2`, perf probe 전 항목 ±5.4% 이내

## 절차 확정 (Godot 검증 프로토콜)
1. 기본 프로브(경로/IO/스트레스): Debug DLL 그대로 — 이제 가드 오탐 없음
2. 리플레이(녹화 stdout 비교): Release DLL을 `.godot/mono/temp/bin/Debug/`에
   재배치 후 실행 (stdout 진단 로그 없는 구성이 표준)
3. 계측이 필요하면 worktree + instr patch, 원복 확인 후 삭제
