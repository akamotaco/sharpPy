import hashlib
import random

names = ["md5", "sha1", "sha224", "sha256", "sha384", "sha512"]
for name in names:
    for size in (0, 1, 3, 55, 56, 57, 63, 64, 65, 111, 112, 113, 119, 120, 127, 128, 129, 1000):
        data = bytes(i % 251 for i in range(size))
        print(name, size, getattr(hashlib, name)(data).hexdigest())
    h = hashlib.new(name)
    print(h.name, h.digest_size, h.block_size)
    whole = getattr(hashlib, name)()
    for start in range(0, 500, 37):
        whole.update(bytes(range(start % 200, start % 200 + 37)))
        snapshot = whole.copy()
        snapshot.update(b"x")
    print(whole.hexdigest(), whole.digest() == bytes.fromhex(whole.hexdigest()), snapshot.hexdigest())
print(sorted(hashlib.algorithms_guaranteed & set(names)), hashlib.sha256(bytearray(b"a")).hexdigest())
for call in (lambda: hashlib.sha1("text"), lambda: hashlib.md5(1), lambda: hashlib.new("nope")):
    try:
        call()
    except (TypeError, ValueError) as e:
        print(type(e).__name__)
random.seed("a string seed")
print(random.random(), random.randint(1, 100), random.choice("abc"))
random.seed(b"bytes seed")
print(random.random())
