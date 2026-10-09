# 32차: 저장된 시퀀스의 큰 slice step

31차 range 수정은 `1a3ed63`으로 커밋했다. 이번에는 list·tuple·str·bytes·bytearray에서 int 범위를 벗어나는 step의 변환 오류를 수정했다.

## 구현과 CPython 근거

CPython 3.12.15 고정 커밋 `e848e4b09ca4bc67c0041c9ce7526dae471d4c36`의 Objects/sliceobject.c `PySlice_Unpack`은 step을 인덱스 크기로 제한하고 `PySlice_AdjustIndices`로 경계를 조정한다. SharpPy의 실제 배열/List 길이는 int 범위다. 절댓값이 그 범위를 넘는 step은 선택 원소가 최대 하나이므로, 저장된 시퀀스에는 int 포화 변환으로 같은 원소와 대입·삭제 결과를 얻을 수 있다.

`StorageIndices`를 해당 다섯 타입의 읽기 및 list/bytearray 대입·삭제 경로에 적용했다. 기존 경계 정규화를 공유하며 step 변환에 `int.CreateSaturating`을 사용한다. slice 객체 자체의 Step은 변경하지 않는다. range와 memoryview는 step/stride가 결과 정보에도 관여하므로 기존 경로를 유지했다. 공개 C# Indices 및 보조 API 전체를 개선한 것은 아니다.

## 검증

새 회귀는 ±int 경계 초과, ±64비트 경계, ±10**100과 여섯 시작·끝 경계를 조합한 216개 경우를 검사한다. 빈 시퀀스, 다섯 타입의 읽기, list/bytearray 대입·삭제, 빈 bytearray 대입, callback 순서와 slice 원본 보존, 길이 불일치 예외를 확인한다. 기준 DLL은 변환 오류로 실패하고 변경 후에는 실제 CPython 3.12.15와 일치한다.

최종 Release 빌드 성공. 기본 70개는 raw 일치 65, 지정 변동 필드만 다른 3, native no_reported_failure 2다. Godot 4.7.2/.NET 8.0.31 프로젝트에 SharpPy 소스를 직접 포함한 빌드와 기존 네 회귀 및 새 큰 step 회귀까지 다섯 개가 통과했다. 별도 SharpPy DLL 참조는 없다. 30차와 같은 GODOT 심볼 및 DEBUG 제한 제외 조건이다.

## 성능 확인

변경 전후 같은 sequence slice native harness를 사용했다. DOTNET_TieredCompilation=0, 프로세스 3쌍, 각 100 warmup/10표본이다. 초기 두 BigInteger 범위 비교는 작은 입력에서 비용 증가가 보여 BCL 포화 변환으로 교체한 뒤 다시 측정했다.

최종 크기 16의 1,000회 작업 중앙값은 기준 대비 약 -10%~+2.5% 범위이며 모든 조건의 할당량은 같다. 예를 들어 bytes step=-1은 0.02345→0.02190ms, tuple step=1은 0.02440→0.02500ms다. 사분위 범위가 대부분 겹치므로 속도 향상을 주장하지 않는다. 크기 1,024에서도 할당량은 같고 시간은 양방향으로 변동했다. 이 측정은 일반 step=1/2/-1에 추가된 변환 비용을 확인하는 용도이며 전체 VM 성능이나 큰 정수 입력의 속도 향상 수치가 아니다.

[기준/최종 실행·표본·사분위수·hash·Godot 로그](round32-sequence-large-step.json)에 초기 후보 측정도 함께 보존했다.

## 미해결 항목

기존 `tests/compatibility/slice_large_step.py`는 bytes 부분을 지나 memoryview에서 여전히 실패한다. range의 임의 정밀도 step 및 memoryview stride 표현은 다음 조사 범위다.

회귀 작성 중 `bytearray(b'abc')[::2]`에 일반 list를 대입하면 StopIteration이 전파되는 문제도 재현했다. 변경 전후 모두 실패하며 `tests/compatibility/bytearray_slice_iterable_assignment.py`에 별도로 보존했다. 새 큰 step 회귀의 bytearray 대입은 bytes RHS로 이 독립된 문제를 분리했다. 위 기본 70개에는 이 두 알려진 실패를 포함하지 않는다.
