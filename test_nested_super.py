# test_nested_super.py - SharpPy 중첩 함수 내 super() 호출 테스트
#
# 이 테스트는 중첩 함수 내에서 클래스의 super()가 호출될 때
# SharpPy가 제대로 처리하는지 확인합니다.
#
# 문제 상황:
# - 클래스 메서드 내부에 정의된 중첩 함수(def add_to_wardrobe)에서
# - 다른 클래스 인스턴스를 생성하고 instantiate()를 호출하면
# - 해당 클래스의 instantiate()가 super().instantiate()를 호출할 때
# - "super() argument 1 must be type" 오류 발생
#
# 실행: Godot에서 ScriptSystem을 통해 이 파일의 함수 호출

# ========================================
# 실제 코드와 유사한 4단계 상속 구조
# ========================================

class Asset:
    """최상위 베이스 클래스 (assets/base.py의 Asset)"""
    def __init__(self):
        self.instance_id = None
        self._instantiated = False

    def instantiate(self, instance_id, **kwargs):
        self.instance_id = instance_id
        self._instantiated = True
        print(f"[Asset.instantiate] instance_id={instance_id}")


class Item(Asset):
    """아이템 클래스 (assets/base.py의 Item)"""
    name = "Unknown"
    equip_props = {}

    def instantiate(self, instance_id):
        super().instantiate(instance_id)
        print(f"[Item.instantiate] name={self.name}")


class Clothing(Item):
    """의류 기본 클래스 (assets/items/clothes.py의 Clothing)"""
    category = "clothing"

    def instantiate(self, instance_id):
        super().instantiate(instance_id)
        print(f"[Clothing.instantiate] category={self.category}")


class Sundress(Clothing):
    """선드레스 (assets/items/clothes.py의 Sundress)"""
    name = "선드레스"
    equip_props = {"착용:상의": 1, "착용:하의": 1}

    def instantiate(self, instance_id):
        super().instantiate(instance_id)
        print(f"[Sundress.instantiate] done")


# ========================================
# Location 클래스 시뮬레이션
# ========================================

class Location(Asset):
    """Location 클래스 (assets/base.py의 Location)"""
    name = "Unknown Location"

    def instantiate(self, location_id, region_id):
        super().instantiate(location_id)
        self.region_id = region_id
        print(f"[Location.instantiate] location_id={location_id}, region_id={region_id}")

    def add_object(self, obj):
        print(f"[Location.add_object] added {obj}")
        return 100  # fake wardrobe_id


class LinaRoom(Location):
    """리나의 방 - 실제 문제 상황 재현"""
    name = "리나의 방"

    def instantiate(self, location_id, region_id):
        super().instantiate(location_id, region_id)

        wardrobe_id = self.add_object("Wardrobe")

        # 이것이 실제 문제 상황!
        # 클래스 메서드 내부에서 정의된 중첩 함수
        def add_to_wardrobe(item_class):
            item = item_class()
            item_id = 1000 + len(items)  # fake id
            item.instantiate(item_id)  # 여기서 Sundress.instantiate() -> super() 호출
            items.append(item)

        items = []
        add_to_wardrobe(Sundress)
        add_to_wardrobe(Sundress)

        print(f"[LinaRoom.instantiate] {len(items)} items added to wardrobe")


# ========================================
# 테스트 1: 직접 호출 (정상 동작 예상)
# ========================================
def test_direct_call():
    """중첩 함수 없이 직접 호출 - 정상 동작해야 함"""
    print("=== Test 1: Direct Call ===")
    item = Sundress()
    item.instantiate(42)
    print(f"Result: instance_id={item.instance_id}")
    print("=== Test 1 PASSED ===\n")


# ========================================
# 테스트 2: 모듈 레벨 함수 내 중첩 함수
# ========================================
def test_module_level_nested():
    """모듈 레벨 함수 내 중첩 함수"""
    print("=== Test 2: Module Level Nested Function ===")

    items = []

    def add_item(item_class, item_id):
        item = item_class()
        item.instantiate(item_id)
        items.append(item)

    add_item(Sundress, 100)
    add_item(Sundress, 200)

    print(f"Result: {len(items)} items created")
    print("=== Test 2 PASSED ===\n")


# ========================================
# 테스트 3: 클래스 메서드 내 중첩 함수 (실제 문제 상황!)
# ========================================
def test_class_method_nested():
    """클래스 메서드 내 중첩 함수 - 실제 문제 상황"""
    print("=== Test 3: Class Method Nested Function ===")

    room = LinaRoom()
    room.instantiate(1, 0)

    print("=== Test 3 PASSED ===\n")


# ========================================
# 테스트 4: 더 복잡한 클로저 상황
# ========================================
def test_closure_with_outer_variable():
    """클로저가 외부 변수를 캡처하는 상황"""
    print("=== Test 4: Closure with Outer Variable ===")

    wardrobe_id = 999  # 외부 변수

    items = []

    def add_to_wardrobe(item_class):
        # wardrobe_id를 클로저로 캡처
        item = item_class()
        item_id = wardrobe_id + len(items)
        item.instantiate(item_id)
        items.append(item)
        print(f"  Added to wardrobe {wardrobe_id}")

    add_to_wardrobe(Sundress)
    add_to_wardrobe(Sundress)

    print(f"Result: {len(items)} items created")
    print("=== Test 4 PASSED ===\n")


# ========================================
# 모든 테스트 실행
# ========================================
def run_all_tests():
    """모든 테스트 실행"""
    print("\n" + "=" * 50)
    print("SharpPy Nested Function Super() Test")
    print("(Matching real code structure)")
    print("=" * 50 + "\n")

    try:
        test_direct_call()
    except Exception as e:
        print(f"!!! Test 1 FAILED: {e}\n")

    try:
        test_module_level_nested()
    except Exception as e:
        print(f"!!! Test 2 FAILED: {e}\n")

    try:
        test_class_method_nested()
    except Exception as e:
        print(f"!!! Test 3 FAILED: {e}\n")

    try:
        test_closure_with_outer_variable()
    except Exception as e:
        print(f"!!! Test 4 FAILED: {e}\n")

    print("=" * 50)
    print("All tests completed")
    print("=" * 50)


# CPython에서 직접 실행 시
if __name__ == "__main__":
    run_all_tests()
