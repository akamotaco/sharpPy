# 111차: math.isclose와 후속 1~6번 상태

후속 6번의 math.isclose 누락을 해결했다. 참조는 CPython v3.12.15 commit
e848e4b09ca4bc67c0041c9ce7526dae471d4c36의
[Modules/mathmodule.c](https://github.com/python/cpython/blob/e848e4b09ca4bc67c0041c9ce7526dae471d4c36/Modules/mathmodule.c),
math_isclose_impl 및 Argument Clinic 입력이다.
2026-10-12 다운로드 SHA256:
436a01da195e3c2b9576a4b9188d5d62e9883e62e74f2d60bc55194bfce98281.

음수 tolerance 검사, 동일한 무한대, 반대 무한대, NaN과 상대/절대 오차의
판정 순서를 유지했다. a/b keyword, keyword-only tolerance, 중복/누락 인자,
__float__/__index__와 큰 정수 overflow도 실제 CPython과 비교했다.
500개 수치 조합과 callback 변환 순서·오류 문구를 회귀로 보존했다.
기본 숫자 경로는 임시 숫자 배열 없이 double 연산과 PyBool singleton을 사용한다.
기존 API가 없었으므로 전후 속도 향상을 주장하지 않는다.

## 최종 재검증

- 기본 206개: 출력 일치 201, 지정 변동 필드 3, native CLI 2.
- 기존 compatibility 파일 68개 전체: 일치 66, SharpPy 실패 2.
- Godot 소스 빌드 경고/오류 0, 기본 17+관련 21개 통과, 경로 검사 5개.
- .NET 경고 fixture는 stderr까지 비교했다. Godot는 경로 표기가 다른 기본
  경고 대신 필터/교체된 stderr를 관찰하는 fixture를 검증했다.

원문은 round111-validation.json에 보존했다. 실패 2개는
set_hash_storage_remaining.py, memoryview_automatic_release.py다.
이번 요청 시작 시 보존 실패 13개 중 11개가 일치로 바뀌었다.

## 작업 상태

1. 부분 해결: frozenset 저장 해시·캐시, 생성자 소비 순서. mutable set 재진입은 남음.
2. 해결: dict cursor/view 및 module mapping 기존 재현 5개.
3. 해결: int bytes/bytearray/index·keyword base 기존 재현.
4. 해결: class closure/qualname 및 kwdefaults 기존 재현 3개.
5. 부분 해결: coroutine throw 경고. memoryview 즉시 자동 해제는 남음.
6. 해결: math.isclose.

별도 잔여: 함수 호출의 105차 대비 약 3.4% 지연 원인 분리, 과거 비교/예외
비용 조사, 큰 정수의 나머지 format/컴파일 literal 제한, 전체 라이브러리 감사.
warnings의 일반 API/stub, 공개 C# Dictionary 직접 변경 등의 한계도 각 보고서에
명시했다. 모든 CPython 기능 및 전체 호스트 API 호환 완료를 의미하지 않는다.
