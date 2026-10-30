using System;
using System.Collections.Generic;

namespace SharpPy;

/// <summary>
/// The release bus: external resources (OS file handles, streams) get closed even when no Python
/// code called close(). POLICY: docs/lifetime_and_gc_policy.md - this is "room C", resource state,
/// where the mechanism may differ from CPython but the outcome must be the same. Object lifetime
/// stays with the CLR collector; that is not negotiable here. What must not happen is a file object
/// Python can no longer reach continuing to lock a file, because that turns a lifetime difference
/// into an observable failure: on Windows os.remove() answers PermissionError where CPython answers
/// success (measured, .artifacts/tmp/lock.py, 4000 abandoned open() calls).
///
/// What CPython does (pinned v3.12.15):
///   Include/object.h:690            Py_DECREF reaching zero hands the object to _Py_Dealloc.
///   Objects/object.c:2623           _Py_Dealloc calls tp_dealloc.
///   Modules/_io/fileio.c:530        fileio_dealloc (bufferedio.c:408, iobase.c:382 likewise) calls
///                                   _PyIOBase_finalize.
///   Modules/_io/iobase.c:349        zombie (refcnt 0) -> PyObject_CallFinalizerFromDealloc, which
///                                   temporarily sets refcnt to 1 ("Temporarily resurrect the
///                                   object", Objects/object.c:374) so that close() can run code.
///   Objects/object.c:357            PyObject_CallFinalizer, "tp_finalize should only be called once".
///   Modules/_io/iobase.c:298        iobase_finalize, installed as Py_tp_finalize (:896): reads the
///                                   Python-visible .closed, sets self._finalizing = True, and calls
///                                   the *Python-level* close() method. Errors are silenced unless
///                                   dev mode - "Silencing I/O errors is bad, but printing spurious
///                                   tracebacks is equally as bad".
///   Modules/_io/fileio.c:90         fileio_dealloc_warn issues the ResourceWarning for a file that
///                                   is being garbage collected while still open.
///   Python/pylifecycle.c:1673       flush_std_files(), reached from the shutdown paths (:1896,
///                                   :1925, :2952).
/// So in CPython the handle is closed at the moment the last reference drops, and it is closed by
/// calling the object's own close().
///
/// What SharpPy does instead, and why each piece is where it is:
///
/// 1. Every file object registers a release action plus a WeakReference to itself. The action
///    captures the Stream and StreamWriter, NOT the file object: an action that captured `this`
///    would keep every open file alive through the registry and nothing would ever be released.
/// 2. Release happens at four points:
///      - close() / `with` exit (deterministic, same as CPython)
///      - registration-driven sweeps (geometric, so a loop of 20000 open() calls does not go
///        quadratic; a forced collection is used only when the CLR has not collected anything
///        since we last looked and the pending list is already large)
///      - gc.collect() (the reconciliation anchor; same instant CPython's own collect would close
///        files - modules/GcModule.cs)
///      - interpreter shutdown, after the __del__ hooks, because a finalizer may still write to an
///        open file (core/PyFinalizer.cs)
/// 3. os.remove/unlink/rmdir/rename retry once after a release pass. This is the part that makes
///    the measured outcome identical: a file dropped mid-run is released at the moment the program
///    needs it back, instead of at the moment the CLR happened to collect it. The cost is zero on
///    the success path and one pass on a path that was already failing.
///
/// Remaining divergence, recorded in docs/known_gaps.md (P0-38): a subclass that overrides close()
/// has that override called by CPython during finalization (iobase_finalize looks the method up on
/// the object); SharpPy's release path runs the native teardown, because by then the object itself
/// has been collected and there is nothing left to dispatch on. Same for ResourceWarning - SharpPy
/// does not warn about a file it closed on the owner's behalf.
/// </summary>
internal static class PyResourceBus
{
    internal sealed class Token
    {
        internal WeakReference<PyObject>? Owner;
        // Null once the resource is gone, whether close() released it or a sweep did. Keeping the
        // action itself rather than the object is what makes the registry non-blocking for the GC.
        internal Action? Release;
    }

    private static readonly List<Token> Tokens = new List<Token>();
    private static int sweepAt = 16;
    private static int lastCollectionCount = -1;
    // Below this many unreleased resources, never force a collection: the ordinary CLR cadence is
    // enough and a forced collect would cost more than the handles it frees are worth.
    private const int ForceFloor = 64;

    /// <summary>
    /// Registers an external resource owned by a Python object. The action must not reference the
    /// owner, directly or through a captured `this`.
    /// </summary>
    internal static Token Register(PyObject owner, Action release)
    {
        var token = new Token { Owner = new WeakReference<PyObject>(owner), Release = release };
        Tokens.Add(token);
        if (Tokens.Count >= sweepAt)
        {
            // Nothing can look dead until the CLR has collected, so a pass is only worth forcing
            // collection when the list is already big and no collection has happened since we looked.
            bool force = Tokens.Count >= ForceFloor && GC.CollectionCount(0) == lastCollectionCount;
            Sweep(force);
            sweepAt = Math.Max(16, Tokens.Count * 2);
        }
        return token;
    }

    /// <summary>
    /// Called by close(): the owner already released the resource, so the entry becomes a tombstone
    /// that the next sweep drops without work.
    /// </summary>
    internal static void MarkReleased(Token? token)
    {
        if (token != null) token.Release = null;
    }

    /// <summary>
    /// Releases every registered resource whose Python owner is unreachable. With forceCollection,
    /// makes one collection pass first if the cheap pass found nothing, so that a caller about to
    /// fail an OS operation gets the same answer CPython would give. Returns the number released.
    /// </summary>
    internal static int ReleaseUnreferenced(bool forceCollection) => Sweep(forceCollection);

    /// <summary>
    /// Interpreter shutdown: release what is still open, in registration order, after the
    /// __del__ hooks had their chance to use them.
    /// </summary>
    internal static void ReleaseAllAtShutdown()
    {
        foreach (var token in Tokens)
            if (token.Release != null)
                Run(token);
        Tokens.Clear();
        sweepAt = 16;
    }

    private static int Sweep(bool forceCollection)
    {
        if (Tokens.Count == 0) return 0;
        int released = SweepPass();
        if (forceCollection && released == 0 && Tokens.Count > 0)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            released = SweepPass();
        }
        lastCollectionCount = GC.CollectionCount(0);
        return released;
    }

    // One pass over the registry. forceCollection adds a single collection when the cheap pass
    // freed nothing, because a WeakReference can only look dead after the CLR has collected.
    private static int SweepPass()
    {
        int released = 0;
        for (int i = Tokens.Count - 1; i >= 0; i--)
        {
            var token = Tokens[i];
            if (token.Release == null)
            {
                Tokens.RemoveAt(i);
                continue;
            }
            if (token.Owner != null && token.Owner.TryGetTarget(out _)) continue;  // Python still holds it
            Run(token);
            token.Release = null;
            Tokens.RemoveAt(i);
            released++;
        }
        return released;
    }

    // A release failure has no owner left to report to. CPython silences the same class of failure
    // in iobase_finalize (Modules/_io/iobase.c:298) for the same reason: an interpreter that spews
    // tracebacks at finalization is worse than one that quietly closes the handle.
    private static void Run(Token token)
    {
        try { token.Release!(); }
        catch (Exception) { }
    }
}
