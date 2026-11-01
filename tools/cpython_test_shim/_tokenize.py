"""Lets Lib/tokenize.py import. Only for running CPython's tests; the C tokenizer is not exposed."""


class TokenizerIter:
    def __init__(self, *args, **kwargs):
        raise NotImplementedError("_tokenize is not available in SharpPy")
