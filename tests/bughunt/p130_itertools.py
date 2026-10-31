import itertools as i
data = sorted(["a", "bb", "cc", "ddd"], key=len)
print([(k, list(g)) for k, g in i.groupby(data, key=len)])
print({k: list(g) for k, g in i.groupby(data, key=len)})
d = {}
for k, g in i.groupby(data, len):
    d[k] = list(g)
print(d)
print([k for k, g in i.groupby("aabbbcc")], [(k, len(list(g))) for k, g in i.groupby([1, 1, 2, 3, 3])])
gs = [g for k, g in i.groupby("aabb")]
print([list(g) for g in gs])
it = i.groupby("aab")
k, g = next(it); print(k, next(g)); k2, g2 = next(it); print(k2, list(g2), list(g))
print(list(i.groupby([])))
print(list(i.product("ab", repeat=2)), list(i.product([1, 2], "x")), list(i.product(repeat=2)), list(i.product()))
print(list(i.zip_longest("ab", [1], fillvalue="-")), list(i.zip_longest("a", "bc")))
print(list(i.accumulate([1, 2, 3], initial=10)), list(i.accumulate([1, 2, 3], lambda a, b: a * b)), list(i.accumulate([1, 2], func=max)))
print(list(i.islice(i.count(start=5, step=2), 3)), list(i.permutations("ab", r=1)), list(i.combinations("abc", r=2)))
try:
    i.groupby([], bad=1)
except TypeError as e:
    print(type(e).__name__)
