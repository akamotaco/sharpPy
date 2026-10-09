# Windows Terminal / .NET Host 종료 분석

분석일: 2026-10-09. 시각은 한국 표준시(UTC+09:00).

## 결론

.NET Host 종료 두 건은 PerformanceHarness 검사 코드의 처리되지 않은 관리 예외로 원인이 확인됐다. Windows Terminal 종료는 별도 프로세스의 XAML 메모리 접근 오류다. 현재 증거로 두 종료의 공통 원인이나 직접적인 인과관계를 확인할 수 없다. Terminal의 정확한 실패 함수와 호출 경로는 덤프 부재로 미확정이다.

## 확인한 이벤트

| 시각 | 프로세스 | 기록 | 확인된 내용 |
| --- | --- | --- | --- |
| 14:08:30 | dotnet.exe, PID 21200 | .NET Runtime 1026 / Application Error 1000, record 1528–1529 | ArgumentOutOfRangeException: index 242, 허용 범위 [0,242). KERNELBASE.dll, e0434352 |
| 14:08:45 | dotnet.exe, PID 6552 | 동일, record 1531–1532 | 동일한 검사 코드와 예외. 두 WER bucket도 동일 |
| 14:22:48 | WindowsTerminal.exe, PID 4296 | Application Error 1000, record 1536 | Windows.UI.Xaml.dll 10.0.26100.9444, c0000005, offset d93c2 |

Terminal은 11:10:53에 시작한 프로세스이며 마지막 dotnet 종료로부터 약 14분 뒤 종료됐다. 실행 파일 버전은 1.18.2401.30001, 패키지 버전은 1.18.10301.0이다. 두 버전 표기는 서로 다른 필드다.

.NET 스택은 다음 경로다:

```text
PyCodeObject.InstructionIndexToByteOffset — runtime/PyBytecode.cs:805
ExceptionTableInspection.<Inspect>g__Visit — tools/PerformanceHarness/ExceptionTableInspection.cs:20
ExceptionTableInspection.Inspect — :13
Program.Main — :35
```

당시 작성한 검사 코드가 배열 길이인 Count를 유효한 명령어 인덱스로 변환하려 했다. 현재 검사 코드는 최종 명령어 배열의 바이트 길이를 Count * INSTRUCTION_WORD_SIZE로 구하고, 개별 인덱스 검사는 Count 미만으로 제한한다. Windows가 이 실행을 `.NET Host`로 표시한 것이며, KERNELBASE.dll이 오류 모듈로 기록됐다는 사실만으로 CLR 자체 결함으로 해석할 수 없다. e0434352는 CLR 예외 코드다([Microsoft 설명](https://learn.microsoft.com/en-us/shows/inside/e0434352)).

수정된 검사기를 CPython 3.12와 다시 실행한 결과 exception table presence/coverage/bounds 비교가 PASS했다. 이 검사는 .NET 종료 원인이 해결됐는지 확인하는 것이며 Terminal 안정성을 검증하지 않는다.

## Terminal 및 GPU 관련 증거와 한계

Terminal의 c0000005는 메모리 접근 위반이다([Microsoft 설명](https://learn.microsoft.com/en-us/shows/inside/c0000005)). WER archive에는 Report.wer만 남았으며 해당 종료 이벤트가 참조한 임시 덤프는 현재 WER Temp에 없다. 따라서 fault offset만으로 특정 XAML/Terminal 버그를 지정하지 않았다. 공식 Terminal 저장소에도 XAML 접근 위반 사례가 있지만, 같은 모듈/예외 코드만으로 이번 사건과 동일하다고 판단할 수 없다.

13:23:20에 LiveKernelEvent 141 두 건이 보고됐다. 참조된 WATCHDOG 파일명은 20261009-0139 및 20261008-2246이며, 보고 시각과 실제 장애 시각이 같다고 가정할 수 없다. 141은 video engine timeout이다([Microsoft 설명](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/bug-check-0x141---video-engine-timeout-detected)). GPU/드라이버 경로를 추가 조사할 근거는 되지만, 이번 Terminal 종료와의 연결이나 GPU 하드웨어 고장을 입증하지 않는다.

GPU는 AMD Radeon 8060S, 드라이버 32.0.31041.1004다. Terminal에는 AMD 그래픽 DLL이 로드돼 있었으나 이 자체는 오류 증거가 아니다. 13:50–14:30 System 로그 전체 조회에서 경고/오류 및 Display/WHEA/Dxg/Resource-Exhaustion 제공자 기록은 반환되지 않았다. WATCHDOG 디렉터리 및 Kernel_141 Report.wer는 Windows ACL에 의해 접근이 거부돼 실제 kernel event time과 덤프 스택을 확인하지 못했다.

프로세스의 과거 부모/자식 관계는 현재 프로세스 목록으로 복원할 수 없다. dotnet의 콘솔 출력이 Terminal 오류를 간접적으로 촉발했을 가능성도 완전히 배제하지 않았지만, 이를 뒷받침하는 스택이나 재현 사례는 없다.

## 보존 자료와 다음 조사

원본 Application 이벤트 XML, 세 WER 보고서 및 수정 검사 결과는 git에서 제외된 `.artifacts/crash-analysis-20261009/`에 보존했다. 기존 round9 수정사항은 유지했다. 시스템 설정, 드라이버, Terminal 설치 및 덤프 수집 레지스트리는 변경하지 않았다.

재발 시에는 WindowsTerminal.exe의 예외 덤프를 확보해 native stack과 접근 대상 주소를 먼저 확인해야 한다. WATCHDOG 덤프는 관리자 권한의 디버거에서 확인해야 한다. Terminal 버전 변경이나 렌더링 설정 비교는 그 뒤 분리된 조건으로 시행해야 하며, 이번 기록만으로 특정 업데이트가 해결책이라고 단정하지 않는다.
