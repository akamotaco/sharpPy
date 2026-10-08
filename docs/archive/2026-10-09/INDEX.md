# 기존 문서 보관

2026-10-09 기존 문서를 원문 그대로 이동했다. 아래 내용은 과거 구현 의도와 조사 결과를 확인하기 위한 자료이며, 현재 PC 경로·구현 수준·측정 결과의 근거로 그대로 사용하지 않는다.

새 작업 기준은 [평가 및 성능 개선 계획](../../evaluation_and_performance_plan.md)이다.

| 보관 위치 | 내용 |
|---|---|
| [readme.md](readme.md) | 기존 프로젝트 설명 |
| [claude.md](claude.md) | 기존 개발 메모와 이전 PC 환경 |
| [토큰 생성기 README](SharpPy.Tokens/README.md) | 기존 생성 도구 설명 |
| [FAQ](docs/faq.md) | 기존 개발 정책과 질문 |
| [리팩터링 계획](docs/refactoring_plan.md) | 기존 작업 계획 |
| [문법 재작성 계획](docs/python_cs_gram_rewrite_plan.md) | 파서 문법 작업 계획 |
| [성능 개선 계획](docs/performance_optimization_plan.md) | 과거 성능 분석과 후보 |
| [오프셋 분석](docs/offset_vs_index_analysis.md) | 내부 인덱스와 외부 오프셋 |
| [C → C# 패턴](docs/c_to_csharp_patterns.md) | 이식 패턴 |
| [CPython 구현 가이드](docs/cpython_to_sharppy_implementation.md) | CPython과 SharpPy 구조 비교 |
| [액션 코드 분석](docs/action_code_analysis.md) | 파서 액션 코드 조사 |

보관본 내부의 이전 루트 기준 경로와 링크도 원문대로 유지했다. 현재 문서에서 보관본을 인용할 때는 이 보관 위치로 연결한다. 기존 README가 가리키던 `parser_implementation_analysis.md`는 이동 전에도 존재하지 않았다.

Git에는 대소문자만 다른 `README.md`와 `readme.md`가 함께 등록되어 있다. Windows에서는 같은 경로로 취급되므로, 이동한 작업 폴더의 `readme.md`와 별도로 Git 원본 두 개를 [대문자 원본](README.uppercase-original.md), [소문자 원본](readme.lowercase-original.md)에 각각 보존했다. Git의 대소문자 중복 등록 정리는 이번 문서 이동에서 수행하지 않았다.
