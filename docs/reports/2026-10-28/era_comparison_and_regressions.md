# 에라 비교와 회귀 추적 — 2026-10-28 (work/bug-hunt)

기준: CPython 3.12.15 (conda-forge `sharppy-test-py312`). 비교 대상 트리는 세 구간:
legacy(2025-08~12), Claude(2026-01), 무트레일러 버스트(2026-10). 현재 `41d5107`+probe.

## 1. raw.githubusercontent.com vs github.com — 무엇이 같고 다른가

실측(md5): 같은 리퍼런스에 대해서는 두 호스트가 **바이트 단위로 동일**하다
(`raw.githubusercontent.com/python/cpython/<ref>/<path>` == `github.com/python/cpython/raw/<ref>/<path>`,
v3.12.15 typeobject.c = `c1be25b5...`, 328,862 B). 차이가 관찰되는 경로는 호스트가 아니라
**ref 해석**이다:

| ref | typeobject.c | 의미 |
|---|---|---|
| `main` / `HEAD` | 399,841 B | 개발 트렁크(3.14+ 계열) — "CPython 소스"로 무의식 지정 시 이것을 얻는다 |
| `3.12` (브랜치) | 328,862 B | **이동 가능**. 지금은 v3.12.15와 동일하지만 3.12.16 배서가 쌓이면 달라진다 |
| `v3.12.15` (태그) | 328,862 B | 고정 커밋 |
| `v3.12.0` (태그) | 324,578 B | 패치 릴리스 간에도 소스가 다르다(4,284 B) |

추가로 raw 호스트는 CDN 캐시(~300초)를 쓰기 때문에 **브랜치/HEAD ref는 시각에 따라 다른 본문**을
줄 수 있다. 프로젝트의 취득 방식(`tools/fetch_cpython_reference.py`: 태그 → API로 commit SHA 해석 →
SHA 고정 raw URL → sha256 매니페스트)은 이 함정들을 피하는 올바른 방식이며,
`.artifacts/cpython*` 64개 파일 전량의 sha256이 매니페스트와 일치함을 검증했다.
실무 귀결: **비교 런타임이 3.12.15이므로 참조 소스는 v3.12.0 사본이 아니라 v3.12.15 사본을 기준으로 한다**
(패치 릴리스에서 에러 메시지·파서 동작이 잔잔히 바뀌기 때문).

## 2. 커밋 에라 지도와 "잘 동작한 코드 파괴" 사례

메시지 스타일 + 트레일러 분류(`Co-Authored-By: Claude` 39건, 전수 분류):

| 기간 | 커밋 | 귀속 | 근거 |
|---|---|---|---|
| 2025-08-13~12 | ~670 | legacy(사람) | `add : x`/`bug fix : ing...` 스타일, 트레일러 0 |
| 2026-01 | 32 | **Claude** | 32/32 `Co-Authored-By: Claude`, `fix: x` + CPython 경로 인용 본문 |
| 2026-10-09~13 | 129 | **무트레일러 에이전트 버스트** | 전량 `fix:/perf:/test:` 한 줄, 본문·트레일러 0, 라운드 JSON 자동생성. `docs/reports/2026-10-13` 자기비판 문서의 "125~130차"와 동일 구간 |
| 2026-10-28 | 1 | 본 브랜치 | probe/문서 |

### 2.1 Claude 기준선 대비 현재 — 회귀 1건 (257 라벨 전수 비교)

Claude 시대 최종 `a40d72c`에서 probe 9종(257 observable label)을 직접 실행해
CPython 일치 항목만 채점한 결과, 현재와 다른 것은 **정확히 하나**:

- `a, *b = <iterator>` — Claude-era `(1, [2, 3])`(CPython 일치) → 현재 module 수준에서
  `System.Exception: Invalid assignment target expression: StarredExpression` **CLR 유출**,
  함수 수준에서는 `TypeError: cannot unpack non-sequence list_iterator`.
- first-bad(격리 clone 수동 bisect, 부모/대상 2회 재현 확인):
  **`bc14d9d` 2026-10-10 "fix: preserve StopIteration arguments and value semantics"** —
  StopIteration 생성자·Message semantics만 건드린 커밋인데 starred unpack의 컴파일 경로가
  UNPACK_EX에서 ast 런타임 폴백으로 떨어져 파괴됐다. **무관한 fix가 동작 중이던 코드를 망친 사례.**

### 2.2 legacy 시대의 같은 부류 발견

- `reversed(range(3))`: 2025-11-01(`c37e414`) 정상 → first-bad **`09c52e4` 2025-11-22
  "add : __reversed__"** — reversed()를 새로 구현하면서 range를 sequence로 처리하지 못해
  기존 정상 동작을 대체했다(부모 `864de44` 정상 재현 확인).
- `{x=}` f-string: 2025-08-20 `9511016` 계보에서는 정상이었으나 메인 계보에서는 2025-10-01
  시점에 이미 깨짐(정확한 커밋은 아래 방법론 한계로 미특정).
- 반면 A2 CLR-MRO 유출, bool 프로토콜 무시, unhashable set 허용, casefold, `%` 포맷,
  `float.hex` 등은 **2025-08 말 앵커에서 이미 전부 깨져 있었다** — GPT-era가 만든 회귀가 아니라
  GPT-era fix들이 고치지 않고 통과시킨 원결함이다.

### 2.3 방법론 교훈 (재현 가능한 검사만 남긴다)

- 초기 CLI는 `sys.exit(code)`를 무시하고 0을 반환 → **exit-code 기반 bisect 판정은 무효**.
  stdout 마커(`RES:`) 기반으로 전환했다.
- 저장소에 `README.md`/`readme.md` 대소문자 충돌이 있어 Windows에서 빌드 산출물과 함께
  checkout 상태가 오염 → **in-place bisect가 오판**(docs 커밋을 first-bad로 지목 등).
  격리 clone + 충돌 파일 제거 + skip-수렴 루프로 교정했다.
- 단독 재현과 컨텍스트 재현을 구분한다: dict-comprehension 중복키 크래시는 단독 실행에서는
  정상이어서(257 라벨 비교에서 확인), p04 컨텍스트 상호작용 결함으로 재산정했다.

### 2.4 GPT-era가 남긴 검사 인프라 부패

현재 `41d5107`에서 **`tools/PerformanceHarness`가 컴파일되지 않는다** — 런타임 API 변경의
영향으로 16개 프로브 파일이 깨진 채 방치(`dotnet build sharppy.sln` 경로 전체가 아니라
csproj 개별 빌드만 돌아 온 궤적으로 본다). 따라서 `tools/compare_bytecode.py`(harness 의존)도
HEAD에서 실행 불가. 검증 도구 자체의 부패는 회귀 은폐와 직결된다.

## 3. 바이트코드 근접성 (신규 `tools/bytecode_proximity.py`, harness 불필요 — `--dis` 사용)

- opcode enum: `ByteCodeOp` 166개 중 **CPython 3.12 opmap 129개 전부 존재 + 번호 100% 일치**
  (miss=0). 별도 커스텀 op가 37개라는 뜻이고, 그 경계가 근접성 개선 대상이다.
- 코드개체별 명령열 유사도(difflib ratio, cp 기준):
  - `tools/fixtures/bytecode_comparison.py`: module/add/loop 1.000, select 0.833,
    protected 0.811, delegated 0.842 → **mean 0.914**
  - `examples/advanced.py`: 1.000~0.364, **mean 0.875**; gap은 프로토콜 dispatch와
    예외 테이블이 있는 본문, class body 섹션은 `--dis`가 노출하지 않음(MISSING 처리)
  - **`--dis` 자체가 중첩 클로저 lambda 파일에서 크래시**(`EmitLoadDeref`/`EmitLoadClosure`
    가문 — p09와 동일 계열). p06도 같은 이유로 비교 불가.
- 사용자 기준("결과는 동일, 과정 무관하되 bytecode 유사도가 높을수록 좋다")에 따른 현재 점수:
  매핑 충실도 129/129, 열 유사도 표본 평균 ~0.89. generator/try-except/프로토콜 시퀀스가
  열 유사도 손실 집중 구간이며, `--dis` 크래시 복구가 근접성 측정 자체의 전제조건이다.

## 결론 (우선순위 반영 제안)

1. starred-unpack 회귀(`bc14d9d` 계열, CLR 유출 포함) — **GPT-era가 만든 회귀이므로 최우선**,
   기준선 `a40d72c` 동작으로 복구.
2. `--dis`/EmitLoadClosure 가문 컴파일 크래시 — 회귀 추적과 bytecode 근접성 측정 모두의 전제.
3. PerformanceHarness 재건(16 파일) 또는 공식 삭제 — 회귀 은폐 방지.
4. 그 외 A/B 목록은 legacy 원결함으로 분류하고 `bughunt_probe_findings.md` 순서대로 진행한다.
