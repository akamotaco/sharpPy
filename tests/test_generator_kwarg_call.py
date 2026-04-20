# romance() 구조 최소 재현 — generator + method + yield from + kwargs
# romance.py:3064 의 `yield from start_romance(player_id, self.instance_id, mode=MODE_CONSENSUAL)` 패턴

def start_romance(player_id, partner_id, mode=None):
    yield f"started mode={mode} player={player_id} partner={partner_id}"
    yield "done"


class Dummy:
    instance_id = 276

    def romance(self):
        player_id = 999
        # 패턴 1: kwarg 하나 — 가장 단순
        yield from start_romance(player_id, self.instance_id, mode="consensual")


print("=== Test 1: generator method + yield from + 3 args + kwarg ===")
d = Dummy()
gen = d.romance()
print(f"type: {type(gen).__name__}")
try:
    for v in gen:
        print(f"  yield: {v}")
    print("OK: no stack empty")
except Exception as e:
    print(f"FAIL: {type(e).__name__}: {e}")


# romance() 와 동일한 분기 구조 (HP 가드, 모드 감지, 최종 yield from)
def can_start_unconscious(a, b): return (False, None)
def can_start_frozen(a, b): return (False, None)

import sys


class DummyFull:
    instance_id = 276

    def romance(self):
        player_id = 999

        # HP 가드 (통과)
        if False:
            yield "blocked"
            return

        # 모드 감지 1
        ok, _ = can_start_unconscious(player_id, self.instance_id)
        if ok:
            yield from start_romance(player_id, self.instance_id, mode="unconscious")
            return

        # 모드 감지 2
        if False:
            yield from start_romance(player_id, self.instance_id, mode="forced")
            return

        # 모드 감지 3
        ok, _ = can_start_frozen(player_id, self.instance_id)
        if ok:
            yield from start_romance(player_id, self.instance_id, mode="frozen")
            return

        # 최종 경로
        yield from start_romance(player_id, self.instance_id, mode="consensual")


print()
print("=== Test 2: romance() 와 동일한 분기 구조 ===")
df = DummyFull()
gen = df.romance()
print(f"type: {type(gen).__name__}")
try:
    count = 0
    for v in gen:
        print(f"  yield: {v}")
        count += 1
    print(f"OK: {count} yields, no stack empty")
except Exception as e:
    print(f"FAIL: {type(e).__name__}: {e}")
