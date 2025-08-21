def complex_function(
    data: list[int] | dict[str, float] | None,
    optional: str | None = None
) -> tuple[bool, str | int]:
    if data is None:
        return (False, "No data")
    elif isinstance(data, list):
        return (True, len(data))
    else:
        return (True, "dict")

# 테스트
print(complex_function(None))           # OK: (False, "No data")
print(complex_function([1, 2, 3]))      # OK: (True, 3)
print(complex_function({"a": 1.0}))     # OK: (True, "dict")
print(complex_function("wrong"))        # Error: 타입 불일치