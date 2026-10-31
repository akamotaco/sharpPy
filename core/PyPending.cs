namespace SharpPy
{
    /// <summary>
    /// The one read the interpreter hot path makes about object lifetime.
    ///
    /// CPython's equivalent is tstate-&gt;interp-&gt;ceval.eval_breaker, tested in RESUME
    /// (Python/bytecodes.c:145-146 "eval_breaker &amp;&amp; oparg &lt; 2") and at the back edge of every
    /// loop. A CLR finalizer cannot run Python - it runs on another thread where a Python
    /// exception would take the process down - so it only sets this flag and queues the work.
    /// The interpreter thread picks it up at the next safe point (PyFinalizer.Drain).
    ///
    /// POLICY: docs/lifetime_and_gc_policy.md. This is not an attempt to reproduce the *moment*
    /// of unreachability, only to stop losing the calls that are owed.
    /// </summary>
    /// <remarks>
    /// Deliberately no field initializers and no static constructor: the VM reads Flag on every
    /// RESUME and every JUMP_BACKWARD, and a static-init check would put a branch on that path
    /// for a class that owns nothing but the flag.
    /// </remarks>
    internal static class PyPending
    {
        internal static volatile bool Flag;
    }
}
