# 43차 진행 중: range 큰 정수 표현과 실행 정책 차단

42차는 `8365517`로 커밋했다. 아래 변경은 아직 커밋하지 않았으며 완료된 성능 개선으로 취급하지 않는다.

## 작성한 변경

- 일반 PyRange의 3개 long 필드는 유지하고, long 범위를 벗어난 인수는 내부 PyBigRange에서 BigInteger로 보관한다. Python 타입은 둘 다 range다.
- 큰 정수 생성·slice·index·repr·반복을 연결했다. 일반 slice는 기존 int 정규화를 사용하고 계산이 overflow하면 이미 정규화한 값으로 큰 정수 계산을 수행한다. callback을 재호출하지 않는다.
- 생성 인수에 __index__를 적용하고 두 len 진입점 및 LengthHint에 native 길이 제한을 연결했다.
- interop와 legacy AST의 직접 long for 루프를 GetValues 호출로 바꿨다.
- C# 공개 Start/Stop/Step 속성 타입은 BigInteger로 바뀌었다. 기존 C# 소비자는 재컴파일 및 long 대입부 검토가 필요하다. 큰 정수 범위의 반복자는 GetIterator로 얻어야 한다. 외부 C# 소비자 호환 검증은 남아 있다.

## 실제 검증 상태

Godot 4.7.2/.NET 8.0.31 프로젝트에 소스를 직접 포함해 컴파일·빌드·실행했다. 기존 15개, 기존 range_large_step 최소 재현, 신규 range_bigint_regression까지 총 17개가 통과했다. GODOT 심볼을 사용하고 DEBUG instruction cap은 제외한 기존 조건이다. 로그는 `.artifacts/round43-godot-build.log`, `round43-godot-run.log`에 있다.

사용자가 Smart App Control을 끈 뒤 CPython 3.12.15와 standalone .NET 실행을 다시 확인했다. 코드 생성기를 포함한 일반 Release 빌드가 경고/오류 0개로 통과했다. 신규 range_bigint_regression 및 기존 range_large_step 재현은 실제 CPython과 출력이 일치하여 기본 평가에 추가했다.

전체 89개 평가 결과: 출력 완전 일치 84개, 기존 시간·주소 변동값 제외 일치 3개, C# 전용 검사 2개 실패 보고 없음. `.artifacts/round43-resumed-tests.json`에 프로세스별 출력과 실행 정보가 있다. standalone harness의 `--range-extended-check`, `--range-boundary-check`도 통과했다. Harness 빌드는 기존 nullable 경고 2개, 오류 0개다. range equality/ordering 및 다른 기존 프로토콜 누락을 이번 수정으로 해결했다고 주장하지 않는다.

정책 차단 중에도 Godot 소스 포함 방식으로 기준 커밋과 변경본 각각 82개 fixture 출력을 42차 CPython 기록과 대조해 일치했다. 이는 과거 출력 재사용 검사이며 위의 새 CPython 프로세스 비교와 구별한다.

Godot 전후 3쌍 측정(각 작업 10,000회)의 중앙값은 직접 생성 0.02180→0.02000 ms, builtin 생성 0.20675→0.22690 ms, slice 0.11745→0.13265 ms, index 0.08320→0.08310 ms였다. 해당 작업의 할당과 반복 probe 24개 조건의 할당은 동일했다. **일반 slice 약 13% 지연과 builtin 생성 비용은 추가 최적화 대상으로 남아 있다.** 원시 자료는 `round43-godot-progress.json`에 보존했다. 전체적인 성능 향상을 달성했다고 판단하지 않는다.

## 차단 원인 조사

일반 Release 빌드는 tokenizer 코드 생성 도구 실행 단계에서 Windows 오류 4551로 중단됐다. 이 시점에 새 main 코드는 아직 컴파일되지 않았다. 기존 conda CPython 3.12.15 실행 파일도 Application Control 정책에 차단됐다. 상세 빌드 로그는 `.artifacts/round43-build.log`다.

읽기 전용으로 확인한 Windows 상태:

- `VerifiedAndReputablePolicyState = 1`.
- CodeIntegrity 3077/3033: 해당 EXE들이 서명 수준 요건을 충족하지 못했거나 정책을 위반했다는 기록. Policy ID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}`.
- 3118: Smart App Control 차단 기록. 확인한 Python 기록의 ThreatName은 비어 있고 IsUnfriendlyFile은 false였다.
- 두 EXE의 Authenticode 상태는 NotSigned였다.
- 조회한 최근 45분의 Defender Operational 1116/1117 탐지·조치 이벤트는 없었다. 이는 전체 시스템이나 전체 저장소의 무해함을 보증하는 검사가 아니다.

새 range 변경에는 네트워크 접속, 외부 프로세스 실행, 보안 설정 변경 코드를 추가하지 않았다. 에이전트는 보안 정책이나 백신 제외 설정을 변경하지 않았다. 이후 사용자가 직접 Smart App Control을 껐다고 알렸으며 위 실행들이 정상화됐다. Microsoft 공식 FAQ에 따르면 Smart App Control은 개별 앱 예외를 지원하지 않는다. 최신 업데이트에서는 재설치 없이 재활성화할 수 있다고 안내하지만 이 PC의 적용 여부는 확인하지 않았다.

공식 안내: https://support.microsoft.com/en-gb/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions

## 재개 순서

1. 일반 slice와 builtin 생성 비용을 줄이고 같은 조건의 전후 측정으로 확인한다. 기준 DLL은 `.artifacts/round43-baseline`에 보관했다.
2. 외부 C# API 변경 및 interop/legacy AST 호출 경로를 검토한다. 생성 오류·callback 순서·경계 slice overflow·C# GetSlice는 현재 회귀에서 통과했다.
3. 후속 코드 변경에 맞는 CPython 대조와 Godot 소스 통합 회귀를 수행하고 최종 보고서를 갱신한다. 그 전에는 이번 변경을 완료된 성능 개선으로 커밋하지 않는다.
