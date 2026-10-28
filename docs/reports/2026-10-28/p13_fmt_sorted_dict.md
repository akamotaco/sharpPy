# p13 format 미니언어·sorted/min/max·dict/set (2026-10-28)

`tests/bughunt/p13_fmt_sorted_dict.py` 추가 → bughunt **20/20 matching**.

## 발견·수정

1. **f-string 중첩 format spec 토크나이저 버그** (`parser/Tokenizer.cs`):
   `f"{3.5:{10}.{3}}"`처럼 spec 안에서 중첩 필드가 끝난 뒤의 텍스트(`.` 등)를
   표현식 코드로 토큰화해 `SyntaxError: f-string: expecting '}', or format specs`
   발생. CPython은 nested field 뒤에도 format spec 상태를 유지한다(PEP 701).
   - `HandleFStringFormatSpec`이 중첩 `{`로 빠져나올 때 `_fstringSpecResume` 설정
   - `HandleFStringExpression`의 중첩 `}` 처리 후 깊이가 기본 표현식 깊이를
     다시 만나면 spec 모드로 재진입
   - `f"{x:{a}.{b}}"`, `f"{1:>{5}}"`, `f"{7:>{2+1}}"`, `f"{3.5:{10}.{3}}"` 일치.
   - 주의: spec-필드 표현식 안의 dict 리터럴 최심부 `}` 직후 잔여 텍스트는
     여전히 단순化处理 가능( 드문 조합, 문서화).
2. **min/max 재작성** (`core/PyBuiltin.cs MinMaxCore`):
   - `key=` 무시되던 것 수정 (`min(["aaa","b"], key=len)` → `'b'`)
   - `default=` 키워드 지원
   - 빈 iterable 메시지 `min() iterable argument is empty`
     (`arg is an empty sequence` 대체), 무인자
     `min expected at least 1 argument, got 0`
   - 동률 시 첫 승자 유지(CPython stable 선택)
3. p13에서 이미 일치했던 것들: format 미니언어 대부분(`>,*,^`, fill, `,`
   천단위, `%`, 중첩 `{:>{w}}`, 인덱스/속성/항목 필드, 에러 문구 incl.
   `Negative zero coercion (z)...`, bytes spec 거부), sorted 안정성/key/reverse
   (str truthy 포함), list.sort, dict pop/setdefault/popitem/fromkeys/update,
   set 연산 가드(`{1} | [2]` TypeError), hash/중복 키 축약(1/1.0/True).

## 검증

- `tools/run_bughunt.py` 20/20, `tools/evaluate.py` 225 + 3 + 2.
- perf probe reps4: 최대 +8.9%(closure_calls, 상시 노이즈 대역).
