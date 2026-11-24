using System;
using System.Collections.Generic;

namespace SharpPy.Modules
{
    /// <summary>
    /// The allocator behind the big buffers of the compression libraries (liblzma's match finder
    /// window, hash and son arrays and decoder dictionary; libbzip2's block, sort and output
    /// arrays). The C libraries give them back with free() when lzma_end() / BZ2_bz*End() runs;
    /// the CLR collects them only at a full collection, so a program that makes many coders one
    /// after another (zipfile makes one per member) would hold several hundred MB of dead buffers.
    /// The modules end a stream as soon as it cannot be used again (after flush(), at eof), its
    /// buffers come back here, and the next coder asking for the same size takes them. Buffers
    /// of coders that were dropped without ending are counted while they are alive: past
    /// LIVE_LIMIT a full collection frees the dead ones first, as their free() would have.
    /// </summary>
    internal static class CodecBufferPool
    {
        private const long MIN_POOLED = 1 << 20;
        private const long MAX_POOLED_TOTAL = 256L << 20;
        private const long LIVE_LIMIT = 768L << 20;

        private static readonly List<Array> pool = new();
        private static long pooled;
        private static readonly List<WeakReference<Array>> live = new();

        private static long Bytes(Array a) => Buffer.ByteLength(a);

        private static T[] Take<T>(int length) where T : unmanaged
        {
            lock (pool)
            {
                for (int i = pool.Count - 1; i >= 0; i--)
                {
                    if (pool[i] is T[] a && a.Length == length)
                    {
                        pool.RemoveAt(i);
                        pooled -= Bytes(a);
                        return a;
                    }
                }
            }
            return null;
        }

        private static long LiveBytes()
        {
            long total = 0;
            for (int i = live.Count - 1; i >= 0; i--)
            {
                if (live[i].TryGetTarget(out var a))
                    total += Bytes(a);
                else
                    live.RemoveAt(i);
            }
            return total;
        }

        private static T[] Allocate<T>(int length) where T : unmanaged
        {
            long size = (long)length * (typeof(T) == typeof(byte) ? 1 : typeof(T) == typeof(ushort) ? 2 : typeof(T) == typeof(ulong) ? 8 : 4);
            if (size >= MIN_POOLED)
            {
                lock (pool)
                {
                    if (LiveBytes() - pooled + size > LIVE_LIMIT)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        LiveBytes();
                    }
                }
            }
            var array = GC.AllocateUninitializedArray<T>(length);
            if (size >= MIN_POOLED)
            {
                lock (pool)
                    live.Add(new WeakReference<Array>(array));
            }
            return array;
        }

        /// <summary>An array of length elements; its contents are undefined.</summary>
        internal static T[] Rent<T>(int length) where T : unmanaged => Take<T>(length) ?? Allocate<T>(length);

        /// <summary>An array of length bytes; its contents are undefined.</summary>
        internal static byte[] RentBytes(int length) => Rent<byte>(length);

        /// <summary>An array of length uints; its contents are undefined.</summary>
        internal static uint[] RentUInts(int length) => Rent<uint>(length);

        /// <summary>Gives a buffer back for another coder; small ones are left to the collector.</summary>
        internal static void Return(Array a)
        {
            if (a == null || Bytes(a) < MIN_POOLED)
                return;
            lock (pool)
            {
                long size = Bytes(a);
                // Make room by dropping the oldest buffers.
                while (pool.Count > 0 && pooled + size > MAX_POOLED_TOTAL)
                {
                    pooled -= Bytes(pool[0]);
                    pool.RemoveAt(0);
                }
                if (size <= MAX_POOLED_TOTAL)
                {
                    pool.Add(a);
                    pooled += size;
                }
            }
        }
    }
}
