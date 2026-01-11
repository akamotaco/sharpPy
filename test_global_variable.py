# test_global_variable.py - SharpPy global 변수 테스트
#
# 문제 상황:
# - 모듈 레벨에서 global 변수 선언 (예: _current_chapter = None)
# - 함수 내에서 global 키워드로 선언 후 사용
# - "local variable referenced before assignment" 에러 발생
#
# 실행: Godot에서 ScriptSystem을 통해 run_all_tests() 호출

# ========================================
# 모듈 레벨 전역 변수
# ========================================
_current_chapter = None
_counter = 0


# ========================================
# 테스트 1: global 읽기만 (정상 동작 예상)
# ========================================
def test_global_read_only():
    """global 변수 읽기만 - 정상 동작해야 함"""
    print("=== Test 1: Global Read Only ===")

    # global 선언 없이 읽기만
    if _current_chapter is None:
        print("  _current_chapter is None (correct)")
    else:
        print(f"  _current_chapter = {_current_chapter}")

    print("=== Test 1 PASSED ===\n")


# ========================================
# 테스트 2: global 읽기 + 쓰기 (문제 발생 가능)
# ========================================
def test_global_read_write():
    """global 변수 읽기 후 쓰기 - SharpPy에서 에러 가능"""
    print("=== Test 2: Global Read Then Write ===")

    global _current_chapter

    # 읽기 먼저
    print(f"  Before: _current_chapter = {_current_chapter}")

    # 조건문에서 읽기
    if _current_chapter is None:
        print("  _current_chapter is None, setting to 'chapter_0'")
        _current_chapter = "chapter_0"

    print(f"  After: _current_chapter = {_current_chapter}")
    print("=== Test 2 PASSED ===\n")


# ========================================
# 테스트 3: 실제 chapters/__init__.py 패턴 재현
# ========================================
def test_load_chapter_pattern():
    """실제 load_chapter() 함수 패턴 재현"""
    print("=== Test 3: Load Chapter Pattern ===")

    global _current_chapter

    chapter_name = "chapter_1"
    print(f"  Loading chapter: {chapter_name}")

    # 1. 기존 데이터 저장 (첫 로드가 아니고 preserve_player=True면)
    if _current_chapter is not None:
        print(f"  Saving player data before chapter transition...")

    # 2. 기존 데이터 초기화 (첫 로드가 아니면)
    if _current_chapter is not None:
        print(f"  Clearing previous chapter: {_current_chapter}")

    # 3. 현재 챕터 기록
    _current_chapter = chapter_name

    print(f"  Chapter '{chapter_name}' loaded successfully.")
    print("=== Test 3 PASSED ===\n")


# ========================================
# 테스트 4: global 카운터 증가
# ========================================
def test_global_counter():
    """global 카운터 증가"""
    print("=== Test 4: Global Counter ===")

    global _counter

    print(f"  Before: _counter = {_counter}")
    _counter += 1
    print(f"  After increment: _counter = {_counter}")
    _counter += 1
    print(f"  After increment: _counter = {_counter}")
    print("=== Test 4 PASSED ===\n")


# ========================================
# 테스트 5: 여러 함수에서 global 공유
# ========================================
_shared_state = {"value": 0}

def increment_shared():
    global _shared_state
    _shared_state["value"] += 1

def read_shared():
    return _shared_state["value"]

def test_shared_global():
    """여러 함수에서 global 공유"""
    print("=== Test 5: Shared Global State ===")

    print(f"  Initial: {read_shared()}")
    increment_shared()
    print(f"  After increment: {read_shared()}")
    increment_shared()
    print(f"  After increment: {read_shared()}")
    print("=== Test 5 PASSED ===\n")


# ========================================
# 테스트 6: global 선언 위치 테스트
# ========================================
_position_test = "initial"

def test_global_declaration_position():
    """global 선언이 함수 시작에 없는 경우"""
    print("=== Test 6: Global Declaration Position ===")

    # 먼저 다른 작업
    x = 10
    y = 20
    print(f"  Local vars: x={x}, y={y}")

    # 나중에 global 선언
    global _position_test

    print(f"  _position_test = {_position_test}")
    _position_test = "modified"
    print(f"  _position_test = {_position_test}")
    print("=== Test 6 PASSED ===\n")


# ========================================
# 모든 테스트 실행
# ========================================
def run_all_tests():
    """모든 테스트 실행"""
    print("\n" + "=" * 50)
    print("SharpPy Global Variable Test")
    print("=" * 50 + "\n")

    try:
        test_global_read_only()
    except Exception as e:
        print(f"!!! Test 1 FAILED: {e}\n")

    try:
        test_global_read_write()
    except Exception as e:
        print(f"!!! Test 2 FAILED: {e}\n")

    try:
        test_load_chapter_pattern()
    except Exception as e:
        print(f"!!! Test 3 FAILED: {e}\n")

    try:
        test_global_counter()
    except Exception as e:
        print(f"!!! Test 4 FAILED: {e}\n")

    try:
        test_shared_global()
    except Exception as e:
        print(f"!!! Test 5 FAILED: {e}\n")

    try:
        test_global_declaration_position()
    except Exception as e:
        print(f"!!! Test 6 FAILED: {e}\n")

    print("=" * 50)
    print("All tests completed")
    print("=" * 50)


# CPython에서 직접 실행 시
if __name__ == "__main__":
    run_all_tests()
