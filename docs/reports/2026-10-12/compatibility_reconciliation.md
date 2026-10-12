# 104차: 과거 재현 목록 재분류

2026-10-12, 현재 261bd35 런타임에서 `tests/compatibility/*.py` **67개 전체**를
CPython 3.12.15와 격리 프로세스로 다시 실행했다. 파일당 timeout 15초.
결과는 출력 일치 55, 출력 차이 3, SharpPy 실행 실패 9. reference 실패나
timeout은 없었다. 이는 재현 파일의 결과이며 전체 언어 호환성 비율이 아니다.

`compatibility_inventory.json`에 각 파일·원래 설명·현재 판정·감사 전 기본
회귀 등록 여부를 저장했다. `round104-validation.json`에는 양쪽 raw 출력을
보존했다. `tools/reconcile_compatibility.py`로 같은 목록을 재생성할 수 있다.

일치한 55개 중 기본 회귀에 없던 11개를 승격했다: bound method attributes,
class exceptions/locals, coroutine send, dict subclass copy/same-size iterator/
retained view/live values, memoryview write, module live locals/pop.
11개는 이번 전체 재현 감사에서 각각 검증했다. 승격 후 기본 suite는 186개다.

## 현재 실패 12개 (이 표가 과거 '다음 작업' 표현보다 우선)

| 재현 파일 | 현재 차이 |
|---|---|
| async_generator_kwdefaults.py | function.__kwdefaults__ 속성 노출 누락 |
| class_body_freevar.py | class 내부에서 enclosing 이름 조회 실패 |
| coroutine_throw_legacy_warning.py | 구식 throw 호출 DeprecationWarning 차이 |
| dict_iterator_resize_compaction.py | resize/압축 후 cursor 동작 assertion 실패 |
| dict_view_set_operations.py | dict view 집합 연산 assertion 실패 |
| integer_conversion_remaining.py | bytes/bytearray/index·keyword base 차이 |
| memoryview_automatic_release.py | 자동 해제 뒤에도 export가 남아 BufferError |
| module_mapping_copy_order.py | live mapping 복사 순서 assertion 실패 |
| module_mapping_popitem.py | popitem이 원본 module binding을 삭제하지 않음 |
| module_mapping_retained_views.py | retained view 동기화 assertion 실패 |
| nested_class_qualname_remaining.py | 중첩 클래스 qualname에 부모 누락 |
| set_hash_storage_remaining.py | full hash/frozen hash/callback 차이, 재진입 시 CLR 오류 |

우선순위는 set 재진입 오류, dict cursor/view와 module mapping 관찰 일치,
정수 변환 API, class scope/metadata, warning·자동 수명 순으로 재검토한다.
자동 수명과 경고는 플랫폼·GC·warning filter 조건까지 명시해야 한다.

## 파일 기반 감사 밖의 지속 과제

- 103차 int/float 비용과 역사적 예외 지연 원인은 미확정.
- 초기 참조 CPython patch/variation의 출처 추정은 전체 확정되지 않았다.
- 전체 표준 라이브러리 import/동작, 모든 문법·opcode 조합, 일반 애플리케이션
  성능은 이번 67개 감사의 모집단에 포함되지 않는다.
- 여러 CLR 호스트 API와 32bit 동작은 Windows x64/Godot 소스 빌드 검증과
  구별한다. 사용자 요구에 따라 모바일 실기는 완료 조건으로 추가하지 않는다.
- 과거 보고서는 관측 이력으로 보존하고, 본 목록과 최신 후속 상태를 우선한다.
