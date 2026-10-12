# 117차: 라이브러리 실행 과정, 경고, 비동기 반복 및 호스트 검증

## 기준과 수정

CPython 3.12.15 실제 실행과 고정 원본
`e848e4b09ca4bc67c0041c9ce7526dae471d4c36`을 비교했다.
결과 문자열뿐 아니라 iterable 소비, 사용자 callback, 예외 우선권,
실행 시작·중단·종료 시점을 회귀에 포함했다. 수정된 SharpPy Lib는 유지하며
CPython Lib 전체로 대체하지 않았다.

- `str.join`: 정확한 list/tuple은 직접 처리한다. 다른 iterable은
  CPython PySequence_Fast 경로처럼 먼저 전부 소비한다. 이때 두 번째
  `__iter__`, length hint, 항목 검사보다 늦은 iterator 예외의 우선권을 검증했다.
  오류의 항목 인덱스, 빈 결과, 한 개의 정확한 str 반환도 맞췄다.
- `repr(str)`/`chr`: 비 BMP scalar와 lone surrogate, 비출력 Unicode의
  `\x`/`\u`/`\U` 표현을 수정했다. ASCII 출력 여부는 category 조회 없이 판정한다.
- `warnings`: Warning 계열 생성자를 연결하고 필터/registry/record context,
  regex, stacklevel, 출력 hook, native coroutine throw 경고를 같은 경로로 연결했다.
  **순수 Python warnings.py와 기본 `_warnings` 가속 구현은 과정이 다르다.**
  C 구현은 메시지와 모듈 matcher를 모두 실행한 뒤 subclass를 검사한다.
  실제 실행으로 이를 확인해 단락 평가를 제거했다. once/module의 registry 키,
  명시적 module=None 처리도 C 구현을 기준으로 수정했다.
- `f_globals`: 매번 복사한 PyDict 대신 이미 사용하는 canonical PyGlobalsDict를
  반환한다. 함수에서 보존한 mapping의 identity와 양방향 저장·삭제를 검증했다.
- async generator: eager IEnumerator 실행을 제거하고 기존 PyGenerator 프레임에
  연결한 asend/athrow awaitable을 사용한다. 요청 생성만으로 본문을 실행하지 않는다.
  compiler의 YieldExpression에 ASYNC_GEN_WRAP을 넣고, 명시적 yield와 내부 await를
  구분한다. 재사용·동시 실행 오류, send/throw/close, finally, async for/else,
  사용자 정의 async iterator와 StopIteration/StopAsyncIteration 변환을 검증했다.
  중복 정의되어 VM이 인식하지 못하던 StopAsyncIteration은 한 타입으로 통합했다.
  __await__ callback 예외를 삼키던 fallback을 제거하고 타입 슬롯으로 조회한다.
- 파일 실행: main의 가짜 `<main>` __file__ 대신 실제 실행 경로를 연결하고,
  FileExecutor에서 스크립트 디렉터리를 sys.path[0]에 넣는다. 임베딩 Execute는
  호스트의 sys.path를 변경하지 않는다. 과거 하위 폴더 sibling import 실패를 해결했다.
- Godot: 경고 소스 줄을 읽는 linecache 경로에서 `stat(res://...)`의 FileInfo 오용을
  발견했다. res/user 리소스는 Godot FileAccess로 크기/수정 시각을 얻는다.
  stat_result는 값 snapshot이며 일반 .NET 경로도 조회 시점에 값을 얻는다.
  Godot이 제공하지 않는 리소스 생성/접근 시각은 0이다. 실제 파일의 전체 stat
  메타데이터와 같다고 주장하지 않는다.

추가 확인한 `_warnings.c` SHA256:
`5567817b35e387b6cc5c37074fdcbf0a3978242564a95460ccdc3fe5d6f08019`.
기존 공개 `PyAsyncGenerator(IEnumerator<PyObject>, string)` 생성자는
`PyAsyncGenerator(PyFrame, PyVM, string)`으로 바뀐다. 저장소 내 호출부와
Godot 소스 빌드를 검증했으며 외부 호스트가 옛 생성자를 호출하면 수정이 필요하다.

## 성능

`library_process_workloads.py`, Release, tiering off, CPU mask 0x4,
10,000회, 표본 15, 전후 교차 3쌍. before는 115차 보존 DLL
(`round116-before-harness`)이며 116차에는 런타임 변경이 없다.

| 경로 | 이전 ms | 현재 ms | 이전/현재 할당 B |
|---|---:|---:|---:|
| 정확한 tuple의 join | 7.9474 | 7.5543 | 4,001,176 / 3,601,176 |
| ASCII repr | 7.7901 | 7.9031 | 3,361,176 / 3,361,176 |
| frame.f_globals 관찰 | 16.2037 | 10.2212 | 21,761,264 / 1,761,264 |

join 약 -4.95%, f_globals 약 -36.92%다. repr은 전후 프로세스 중앙값 범위가
겹치므로 개선/퇴행을 확정하지 않는다. 새로 정상 실행되는 async generator와
warnings API에는 유효한 기존 동작 대조군이 없어 속도 개선을 주장하지 않는다.
async generator는 기존 프레임 실행과 wrapper pool을 재사용한다.

## 검증과 남은 경계

- 기본 회귀 220개: 출력 일치 215, 명시한 변동 필드 3, native CLI 검사 2.
- compatibility 디렉터리를 **하위 폴더까지** 포함해 74개 실행: 72개 일치,
  memoryview 자동 해제 실패 1개와 소유권 경계 출력 차이 1개.
  과거 72개 집계는 최상위 fixture만 센 것이며 이번에는 script_import의
  entry.py와 sibling.py도 포함한다.
- Godot 4.7.2에서 저장소 C# 소스를 직접 포함해 컴파일/빌드/실행:
  기본 17개와 관련 fixture 13개, 경로 검사 5개 통과.
  검증 프로젝트에 수정된 Lib 전체를 복사했다. 초기 최소 Lib 구성에는
  re/linecache가 없어 실패했으며, 의존 파일을 포함한 뒤 실제 리소스 stat 문제를
  수정했다. 새 source_warning 회귀는 오류를 숨기는 fallback이 아니라 실제
  소스 줄이 출력되는지 검사한다. 모바일 실기와 packed export는 실행하지 않았다.
- native `--host-namespace-probe`: 8개 관찰 중 5개 일치, 3개 미일치.
  PyGlobalsDict를 통한 쓰기는 동기화된다. 외부 C# 코드가 공개 Dictionary를
  직접 교체/추가/삭제하면 lookup/length는 최신이지만 copy 저장소는 오래된 상태다.
  공개 저장소 API/소유권 구조 변경 없이 이를 해결했다고 표시하지 않는다.

원시 출력·오류·DLL 해시·측정값은 `round117-validation.json`에 보존한다.
전체 회귀 후 최종 코드 검토에서 async generator descriptor의 잘못된 self가
native cast 오류가 될 수 있어 타입 검사를 추가했다. CPython의 TypeError 메시지를
비교하는 회귀를 추가하고 해당 fixture를 .NET/Godot에서 다시 통과했다.
원시 자료는 전체/성능 측정 당시 DLL과 이 마지막 타입 검사 이후 DLL을 구분한다.
이번에 통과한 async generator 실행, Unicode repr, iterable join 보존 재현과
새 과정 회귀들을 기본 검사에 등록했다. 전체 CPython 테스트 통과를 뜻하지 않는다.
warnings 전체 잘못된 인자/동적 필터 변이, async generator hooks·최종화·모든
descriptor 오류, Unicode 버전 전체 대조는 아직 감사하지 않았다.
소스 줄 검증은 현재 수정 Lib의 UTF-8 텍스트 읽기 경로를 사용하며 binary open의
기존 미지원은 이번 범위에 넣지 않았다.

## 이번 1~6 요청의 최종 구분

1. keyword 임시 프레임 제거 및 직접 비교 완료 — ce3817b.
2. set 저장 해시·재진입·복사 개선 — a6fd5c1. 작은 집합 할당 증가가 남는다.
3. memoryview 소유권 분석/9개 실패 보존 — f1865bc. **자동 해제는 미해결**.
4. 큰 정수 format/literal 제한 및 진법 변환 개선 — 8a414a6.
5. 비교/예외 비용 분리 측정 — 48cc585. 과거 단일 지연 원인은 미확정.
6. 이 문서의 라이브러리/호스트/비동기 실행 수정·검증. 외부 Dictionary 직접
   변경 동기화는 위 native probe의 미해결 3개로 남는다.

여섯 항목에 각각 커밋과 결과를 남기는 작업이며, 모든 호환성 문제의 해결 완료가 아니다.
다음 우선순위는 memoryview 소유권 구조, 호스트 mapping 저장소 통합,
작은 set 할당 비용과 남은 API 과정 감사다.
