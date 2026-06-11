# 토크나이저 ASCII 소문자화 회귀 검증: 대문자 접두사/숫자 리터럴
# (참고: u/U 문자열 접두사, RB"..\n" 류 raw prefix + escape 조합은 기존부터 미지원 — 별개 이슈)
assert 0XFF == 255
assert 0Xff == 255
assert 0O17 == 15
assert 0B101 == 5
assert 1E2 == 100.0
assert 1e2 == 100.0
assert B"abc" == b"abc"
assert RB"abc" == rb"abc"
assert BR"abc" == br"abc"
assert R"abc" == r"abc"
assert F"{1+1}" == "2"
x = 3J
assert x * x == -9 + 0j
print("tokenizer case OK")
