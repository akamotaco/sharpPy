"""Pending: BlockingIOError type selection and characters_written semantics."""
for cls in (OSError, BlockingIOError):
    for third in (0, 3, -1, None, 'file', 1.5):
        try:
            error = cls(11, 'would block', third)
            print(cls.__name__, third, type(error).__name__, error.args, error.filename)
            try: print('written', error.characters_written)
            except AttributeError as missing: print('missing', str(missing))
        except Exception as error:
            print('error', cls.__name__, third, type(error).__name__, str(error))
