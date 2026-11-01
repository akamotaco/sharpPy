"""What Lib/signal.py and unittest need from _signal. Only for running CPython's tests: no signals arrive."""
SIG_DFL = 0
SIG_IGN = 1
SIGINT = 2
SIGILL = 4
SIGFPE = 8
SIGSEGV = 11
SIGTERM = 15
SIGBREAK = 21
SIGABRT = 22
NSIG = 23
CTRL_C_EVENT = 0
CTRL_BREAK_EVENT = 1

_handlers = {}


def default_int_handler(signum, frame):
    raise KeyboardInterrupt


def signal(signum, handler):
    previous = _handlers.get(signum, SIG_DFL)
    _handlers[signum] = handler
    return previous


def getsignal(signum):
    return _handlers.get(signum, default_int_handler if signum == SIGINT else SIG_DFL)


def raise_signal(signum):
    raise NotImplementedError


def strsignal(signum):
    return None


def valid_signals():
    return {SIGINT, SIGILL, SIGFPE, SIGSEGV, SIGTERM, SIGBREAK, SIGABRT}


def set_wakeup_fd(fd, *, warn_on_full_write=True):
    return -1
