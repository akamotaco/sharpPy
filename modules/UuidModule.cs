using System;
using System.Net.NetworkInformation;

namespace SharpPy.Modules
{
    /// <summary>
    /// Modules/_uuidmodule.c on Windows: UuidCreate is py_UuidCreate, the 16 bytes of
    /// UuidCreateSequential's UUID struct (Data1, Data2, Data3 little-endian, then Data4), which
    /// uuid.py reads with bytes_le; has_uuid_generate_time_safe is 0. The version-1 UUID is made
    /// here as rpcrt4 makes it: 100 ns ticks since 1582-10-15 that never repeat in the process, one
    /// clock sequence, and the node of a network adapter, or a random multicast node without one
    /// (RPC_S_UUID_LOCAL_ONLY, which py_UuidCreate also accepts).
    /// </summary>
    internal static class UuidModule
    {
        // 1582-10-15T00:00:00Z in .NET ticks (100 ns since 0001-01-01).
        private static readonly long GregorianStart = new DateTime(1582, 10, 15, 0, 0, 0, DateTimeKind.Utc).Ticks;
        private static readonly object Gate = new();
        private static long _lastTime;
        private static int _clockSeq = -1;
        private static byte[] _node;

        internal static PyModule Create()
        {
            var module = new PyModule("_uuid");
            module.ModuleDict["UuidCreate"] = PyArgs.NoArgsFunction("_uuid", "UuidCreate", () => PyBytes.From(UuidCreate()));
            module.ModuleDict["has_uuid_generate_time_safe"] = PyInt.From(0);
            return module;
        }

        // py_UuidCreate: UuidCreateSequential
        private static byte[] UuidCreate()
        {
            long time;
            int clockSeq;
            byte[] node;
            lock (Gate)
            {
                time = DateTime.UtcNow.Ticks - GregorianStart;
                if (time <= _lastTime)
                    time = _lastTime + 1;
                _lastTime = time;
                if (_clockSeq < 0)
                    _clockSeq = Random.Shared.Next(0x4000);
                clockSeq = _clockSeq;
                node = _node ??= Node();
            }
            var uuid = new byte[16];
            uint timeLow = (uint)time;
            ushort timeMid = (ushort)(time >> 32);
            ushort timeHiAndVersion = (ushort)(((time >> 48) & 0x0FFF) | 0x1000);
            // Data1, Data2, Data3: little-endian in the struct
            BitConverter.TryWriteBytes(uuid.AsSpan(0, 4), timeLow);
            BitConverter.TryWriteBytes(uuid.AsSpan(4, 2), timeMid);
            BitConverter.TryWriteBytes(uuid.AsSpan(6, 2), timeHiAndVersion);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(uuid, 0, 4);
                Array.Reverse(uuid, 4, 2);
                Array.Reverse(uuid, 6, 2);
            }
            // Data4: the clock sequence with the RFC 4122 variant, then the node
            uuid[8] = (byte)(0x80 | ((clockSeq >> 8) & 0x3F));
            uuid[9] = (byte)clockSeq;
            Array.Copy(node, 0, uuid, 10, 6);
            return uuid;
        }

        // The address of the first adapter that has a 6-byte one, else a random multicast node.
        private static byte[] Node()
        {
            try
            {
                foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (adapter.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;
                    var address = adapter.GetPhysicalAddress().GetAddressBytes();
                    if (address.Length == 6 && Array.Exists(address, part => part != 0))
                        return address;
                }
            }
            catch (Exception error) when (error is NetworkInformationException or PlatformNotSupportedException)
            {
            }
            var random = new byte[6];
            Random.Shared.NextBytes(random);
            random[0] |= 0x01;
            return random;
        }
    }
}
