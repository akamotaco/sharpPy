# p30: starred unpacking in list/tuple/set display literals.
def show(label, fn):
    try:
        print(label, repr(fn()))
    except Exception as e:
        print(label, "EXC", type(e).__name__, str(e))


a = [2, 3]
b = [7, 8]
show("star-alone", lambda: [*a])
show("star-lead", lambda: [*a, 4])
show("star-trail", lambda: [1, *a])
show("star-mid", lambda: [1, *a, 4])
show("star-two", lambda: [*a, *a])
show("star-multi", lambda: [*a, *b])
show("star-tuple", lambda: (*a,))
show("star-tuple-mid", lambda: (0, *a, 9))
show("star-set", lambda: sorted({*a}))
show("star-set-dup", lambda: sorted({*a, 3, 9}))
show("star-gen", lambda: [*iter([1, 2])])
show("star-str", lambda: [*"ab"])
show("star-range", lambda: [*range(3)])
show("star-noniter", lambda: [*5])
show("star-empty", lambda: [*[]])
show("star-of-set", lambda: sorted({*{5, 6}}))
show("nested-star-in-star", lambda: [*[*a]])
show("star-dict-vals", lambda: [*{1: 'a', 2: 'b'}.values()])


class K:
    def __iter__(self):
        return iter([10, 11])


show("star-custom", lambda: [*K()])
show("star-in-call", lambda: max([*[3, 9, 1]]))
show("star-return", lambda: [*a, 99])
show("dict-unpack", lambda: {**{}, 1: 2})
show("star-call-args", lambda: sorted(*[[3, 1, 2]]))
