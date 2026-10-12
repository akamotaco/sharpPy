using System.Runtime.CompilerServices;
using SharpPy;

// Diagnostic only. Forced GC here is not a runtime resource-release strategy.
internal static class MemoryViewGcProbe
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Abandon(PyByteArray owner) => new(owner.GetBuffer(0));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReleaseAndAbandon(PyByteArray owner)
    {
        var view = owner.GetBuffer(0);
        view.Release();
        view.Release();
        return new WeakReference(view);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AbandonChild(PyByteArray owner)
    {
        var parent = owner.GetBuffer(0);
        var child = parent.GetBuffer(0);
        parent.Release();
        return new WeakReference(child);
    }

    private static bool ResizeBlocked(PyByteArray owner)
    {
        try { owner.Append(100); return false; }
        catch (PythonException error) when (error.PyException is PyBufferError) { return true; }
    }

    internal static object Run()
    {
        var abandonedOwner = new PyByteArray(new byte[] { 1, 2, 3 });
        var weak = Abandon(abandonedOwner);
        var releasedOwner = new PyByteArray(new byte[] { 1 });
        var releasedWeak = ReleaseAndAbandon(releasedOwner);
        var childOwner = new PyByteArray(new byte[] { 1 });
        var childWeak = AbandonChild(childOwner);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        bool collected = !weak.IsAlive;
        bool abandonedBlocked = ResizeBlocked(abandonedOwner);
        var newView = releasedOwner.GetBuffer(0);
        bool noDoubleRelease = ResizeBlocked(releasedOwner);
        newView.Release();
        bool childReleased = !ResizeBlocked(childOwner);
        var liveOwner = new PyByteArray(new byte[] { 1, 2, 3 });
        var live = liveOwner.GetBuffer(0);
        bool liveBlocked = ResizeBlocked(liveOwner);
        live.Release();
        bool explicitReleaseAllowsResize = !ResizeBlocked(liveOwner);
        GC.KeepAlive(abandonedOwner);
        if (!collected || releasedWeak.IsAlive || childWeak.IsAlive || !noDoubleRelease || !liveBlocked || !explicitReleaseAllowsResize)
            throw new InvalidOperationException("GC probe preconditions failed");
        return new { view_collected = collected, resize_after_gc_blocked = abandonedBlocked,
            live_view_blocks_resize = liveBlocked, explicit_release_allows_resize = explicitReleaseAllowsResize,
            no_double_release = noDoubleRelease, collected_child_releases_export = childReleased,
            collected_view_releases_export = !abandonedBlocked };
    }
}
