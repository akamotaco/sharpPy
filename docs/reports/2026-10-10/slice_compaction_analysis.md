# 30차: slice 삭제를 선형 처리하고 Godot 소스 통합 확인

29차를 `9ec2391`로 커밋했다. 이번에는 list와 bytearray의 반복 RemoveAt/Pop을 공통 `PySlice.DeleteItems<T>`로 바꿨다. 남는 구간을 Span.CopyTo로 한 번씩 이동하고 마지막에 RemoveRange로 크기를 줄인다. 연속 삭제는 RemoveRange를 바로 사용한다. 임시 인덱스 배열이나 새 외부 의존성은 없다.

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/listobject.c `list_ass_subscript`, Objects/bytearrayobject.c `bytearray_ass_subscript`의 생존 구간 이동을 대조했다. CLR에서는 참조 계수용 임시 garbage 배열을 복제하지 않는다. List의 마지막 RemoveRange가 폐기된 참조 슬롯을 비운다. Span을 사용하는 동안 Python callback이나 List 크기 변경은 없다. Python 소멸자 시점과 callback 재진입의 전체 호환성은 이번 범위가 아니다.

## 정확성

새 회귀는 1,440개 길이·경계·step 조합에서 list 삭제, bytearray 삭제와 빈 RHS 대입을 CPython과 비교한다. 삭제 후 append, alias, list 객체 identity도 검사한다. 기준 DLL과 변경 DLL 모두 일치하며 이번 변경은 성능 개선이다. 일반 Release 빌드와 기본 68개 검사 결과는 raw 일치 63, 지정된 변동 필드만 다른 3, native no_reported_failure 2다.

초기 round30 기준 실행은 runtimeconfig 누락으로 host 시작에 실패해 제외했다. 설정을 보완한 round30-baseline-valid.json을 근거로 사용했다. 동일한 문제가 발견된 28차 기준 증거도 재실행 후 해당 보고서와 JSON에 정정했다.

## 성능

동일 native harness로 전후 3쌍의 프로세스를 순차 실행했다. 각 100회 warmup/10개 표본이며 양쪽 모두 DOTNET_TieredCompilation=0이다. 삭제 시간에는 매 작업의 bytearray 생성도 포함한다.

| 크기 / step | 작업 수 | 기준 중앙값 ms | 변경 중앙값 ms | 전후 할당 B |
| --- | ---: | ---: | ---: | ---: |
| 16 / 2 | 1,000 | 0.09725 | 0.04995 | 112,000 |
| 16 / -2 | 1,000 | 0.09040 | 0.05465 | 112,000 |
| 1,024 / 2 | 100 | 0.63820 | 0.10105 | 112,000 |
| 1,024 / -2 | 100 | 0.61775 | 0.09815 | 112,000 |

큰 입력의 삭제 중앙값은 약 84% 감소했다. 할당량은 동일하다. 함께 측정한 일반 대입 경로는 바꾸지 않았으므로 그 시간 변화를 이번 최적화 효과로 주장하지 않는다. list의 속도와 전체 VM 성능도 이 수치로 추정하지 않는다. [표본·사분위수·회귀·Godot 구성](round30-slice-compaction.json)에 측정 근거를 보존했다.

## Godot 및 라이브러리 조건

사용자의 통합 방식은 **SharpPy 소스를 Godot 프로젝트에 직접 넣어 함께 빌드**하는 것이다. Android·iPhone 배포에서도 이 구조를 사용한다. 일반 .NET뿐 아니라 이 경로를 변경의 제약 조건으로 삼고 수정된 Python 라이브러리·전용 C# 모듈·미지원 stub의 기존 역할을 확인한다. 이번 변경은 라이브러리 파일을 교체하지 않았다.

로컬 Godot 4.7.2 .NET SDK 프로젝트가 SharpPy 소스와 Generated 코드를 직접 컴파일하도록 구성했다. SharpPy DLL 참조나 ProjectReference 없이 Windows headless/.NET 8.0.31에서 다음 테스트가 모두 PASS를 출력했다.

- slice_delete_compaction_regression.py
- memoryview_slice_assignment_regression.py
- bytes_comparison_regression.py

최초 Debug 실행은 기존 VM의 60,000명령 제한에 걸렸다. 최종 검증은 `dotnet build ... -c Debug -p:DefineConstants=GODOT`로 GODOT 경로를 유지하면서 DEBUG 제한을 제외했다. Godot SDK의 Debug 출력 위치를 사용하지만 일반 Debug 동작 검증은 아니다. 임시 프로젝트는 `.artifacts/round30-godot`에 있으며 구성·실행 로그를 JSON에도 보존했다. 다른 PC에서는 `${REPO}`와 NuGet.Config의 Godot 로컬 패키지 경로를 해당 PC에 맞춰야 한다.

이는 Windows의 세 가지 회귀 검증이다. 실제 게임 프로젝트의 전체 import와 전체 기본 회귀의 Godot 실행은 아직 검증하지 않았다. 사용자 확인에 따라 호환성 완료 기준은 Godot 프로젝트의 소스 통합 컴파일·빌드·실행이며, 모바일 실기기 검증을 추가 조건으로 두지 않는다. 사용자의 Android·iPhone 배포에서 별도 SharpPy DLL 방식은 오류가 발생하므로 제외한다. 후속 작업도 이 통합 방식을 유지하며 큰 step의 기존 미해결 변환 문제를 별도로 진행한다.
