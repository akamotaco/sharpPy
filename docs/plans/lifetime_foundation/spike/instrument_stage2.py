# Run against a scratch git worktree only: logs each Step, and keeps long weak handles on every owner
# so the claim "a long reference keeps the target alive" can be tested on the real VM.
import sys


def patch(path, pairs):
    s = open(path, encoding="utf-8").read()
    for old, new in pairs:
        assert old in s, old
        s = s.replace(old, new)
    open(path, "w", encoding="utf-8", newline="\r\n").write(s)


patch(sys.argv[1] + "/core/PyFinalizer.cs", [
    ("""            var guard = new PyGuard(obj);
            guard.Sequence = Interlocked.Increment(ref _sequence);""",
     """            var guard = new PyGuard(obj);
            if (Environment.GetEnvironmentVariable("SP_LONG") == "1")
                lock (LongProbes) LongProbes.Add(new WeakReference<PyObject>(obj, trackResurrection: true));
            guard.Sequence = Interlocked.Increment(ref _sequence);"""),
    ("""                case GuardState.Armed:
                    guard.State = GuardState.Finalized;""",
     """                case GuardState.Armed:
                    Console.Error.WriteLine("STEP armed->finalized " + ProbeLongAlive());
                    guard.State = GuardState.Finalized;"""),
    ("""                case GuardState.Finalized:
                    guard.State = GuardState.Cleared;""",
     """                case GuardState.Finalized:
                    Console.Error.WriteLine("STEP finalized->cleared " + ProbeLongAlive());
                    guard.State = GuardState.Cleared;"""),
    ("""        private static long _sequence;""",
     """        private static readonly List<WeakReference<PyObject>> LongProbes = new();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ProbeLongAlive()
        {
            int alive = 0, total = 0;
            lock (LongProbes)
            {
                foreach (var w in LongProbes)
                {
                    total++;
                    if (w.TryGetTarget(out _)) alive++;
                }
            }
            return "long alive " + alive + "/" + total;
        }

        internal static string DebugLongAlive() => ProbeLongAlive();

        private static long _sequence;"""),
])
patch(sys.argv[1] + "/modules/GcModule.cs", [
    ("""                PyFinalizer.CollectAndDrain();
""",
     """                PyFinalizer.CollectAndDrain();
                Console.Error.WriteLine("COLLECT done " + PyFinalizer.DebugLongAlive());
"""),
])
