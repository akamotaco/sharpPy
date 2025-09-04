using System;
using System.Threading;

namespace SharpPy
{
    public class TimeModule : PyModule
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        
        public TimeModule() : base("time")
        {
            // CPython 3.12 호환 time.time() 함수
            SetAttribute("time", new PyFunction("time", args =>
            {
                // Unix timestamp (seconds since epoch)
                var now = DateTime.UtcNow;
                var elapsed = now - UnixEpoch;
                return new PyFloat(elapsed.TotalSeconds);
            }));
            
            // CPython 3.12 호환 time.sleep() 함수  
            SetAttribute("sleep", new PyFunction("sleep", args =>
            {
                if (args.Length != 1)
                    throw PyTypeError.Create("sleep() takes exactly one argument");
                    
                var seconds = args[0];
                double delay;
                
                if (seconds is PyFloat pyFloat)
                    delay = pyFloat.Value;
                else if (seconds is PyInt pyInt)
                    delay = pyInt.Value;
                else
                    throw PyTypeError.Create("sleep() argument must be a number");
                    
                if (delay < 0)
                    throw PyValueError.Create("sleep() argument must be non-negative");
                    
                // Convert to milliseconds for Thread.Sleep
                int delayMs = (int)(delay * 1000);
                if (delayMs > 0)
                    Thread.Sleep(delayMs);
                    
                return PyNone.Instance;
            }));
            
            // CPython 3.12 호환 time.perf_counter() 함수
            SetAttribute("perf_counter", new PyFunction("perf_counter", args =>
            {
                // High-resolution performance counter
                var ticks = System.Diagnostics.Stopwatch.GetTimestamp();
                var seconds = (double)ticks / System.Diagnostics.Stopwatch.Frequency;
                return new PyFloat(seconds);
            }));
            
            // CPython 3.12 호환 time.monotonic() 함수
            SetAttribute("monotonic", new PyFunction("monotonic", args =>
            {
                // Monotonic clock that cannot go backward
                var elapsed = Environment.TickCount64 / 1000.0; // Convert ms to seconds
                return new PyFloat(elapsed);
            }));
            
            // 모듈 문서
            SetAttribute("__doc__", new PyString("Time module - CPython 3.12 compatible implementation"));
        }
    }
}