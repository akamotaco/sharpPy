using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SharpPy.Modules
{
    /// <summary>
    /// What Modules/socketmodule.c gets from Winsock for addresses: inet_pton/inet_ntop/inet_addr
    /// (InetPtonA, RtlIpv6AddressToString), getaddrinfo/getnameinfo and the netdb files that
    /// getservbyname/getservbyport/getprotobyname read. .NET has the name resolver
    /// (System.Net.Dns, which calls the system's getaddrinfo) but neither Winsock's text forms of
    /// addresses nor the services table, so those are written here after the Windows behaviour,
    /// measured on the reference CPython 3.12.15 (Windows 11).
    /// </summary>
    internal static class SocketAddresses
    {
        internal const int AF_UNSPEC = 0, AF_INET = 2, AF_INET6 = 23;
        internal const int SOCK_STREAM = 1, SOCK_DGRAM = 2;
        internal const int AI_PASSIVE = 1, AI_CANONNAME = 2, AI_NUMERICHOST = 4, AI_NUMERICSERV = 8, AI_FQDN = 0x20000;
        internal const int NI_NOFQDN = 1, NI_NUMERICHOST = 2, NI_NAMEREQD = 4, NI_NUMERICSERV = 8, NI_DGRAM = 16;
        // ws2tcpip.h: the EAI_* codes are WSA errors on Windows.
        internal const int EAI_AGAIN = 11002, EAI_BADFLAGS = 10022, EAI_FAIL = 11003, EAI_FAMILY = 10047,
            EAI_NONAME = 11001, EAI_SERVICE = 10109, EAI_SOCKTYPE = 10044;

        /// <summary>One addrinfo of the result list.</summary>
        internal sealed class AddrInfo
        {
            internal int Family, SockType, Protocol;
            internal string CanonName;
            internal IPAddress Address;
            internal int Port;
        }

        /// <summary>A getaddrinfo failure: its EAI_* code (set_gaierror).</summary>
        internal sealed class GaiException : Exception
        {
            internal readonly int Code;
            internal GaiException(int code) : base("getaddrinfo failed") => Code = code;
        }

        // ---- inet_pton (InetPtonA) ----

        /// <summary>inet_pton(AF_INET): exactly four decimal parts 0-255, no leading zero, nothing else.</summary>
        internal static byte[] ParseIPv4Strict(string text)
        {
            var parts = text.Split('.');
            if (parts.Length != 4)
                return null;
            var result = new byte[4];
            for (int i = 0; i < 4; i++)
            {
                var part = parts[i];
                if (part.Length == 0 || part.Length > 3 || (part.Length > 1 && part[0] == '0'))
                    return null;
                int value = 0;
                foreach (char c in part)
                {
                    if (c < '0' || c > '9')
                        return null;
                    value = value * 10 + (c - '0');
                }
                if (value > 255)
                    return null;
                result[i] = (byte)value;
            }
            return result;
        }

        /// <summary>inet_pton(AF_INET6): eight groups of 1-4 hex digits, one "::", a dotted IPv4 tail.</summary>
        internal static byte[] ParseIPv6Strict(string text)
        {
            var words = new List<int>();
            int compress = -1;
            int i = 0, n = text.Length;
            if (n == 0)
                return null;
            if (text.StartsWith("::", StringComparison.Ordinal))
            {
                compress = 0;
                i = 2;
                if (i == n)
                    return new byte[16];
            }
            else if (text[0] == ':')
                return null;
            byte[] v4 = null;
            while (i < n)
            {
                int start = i;
                int value = 0, digits = 0;
                while (i < n && Uri.IsHexDigit(text[i]))
                {
                    value = value * 16 + Convert.ToInt32(text[i].ToString(), 16);
                    digits++;
                    i++;
                }
                if (i < n && text[i] == '.')
                {
                    // The IPv4 tail: the rest of the text.
                    v4 = ParseIPv4Strict(text.Substring(start));
                    if (v4 == null)
                        return null;
                    i = n;
                    break;
                }
                if (digits == 0 || digits > 4)
                    return null;
                words.Add(value);
                if (i == n)
                    break;
                if (text[i] != ':')
                    return null;
                i++;
                if (i < n && text[i] == ':')
                {
                    if (compress >= 0)
                        return null;
                    compress = words.Count;
                    i++;
                    if (i == n)
                        break;
                }
                else if (i == n)
                    return null;
            }
            int total = words.Count + (v4 != null ? 2 : 0);
            if (compress >= 0 ? total > 7 : total != 8)
                return null;
            var result = new byte[16];
            int pos = 0;
            for (int w = 0; w < words.Count; w++)
            {
                if (w == compress)
                    pos += (8 - total) * 2;
                result[pos++] = (byte)(words[w] >> 8);
                result[pos++] = (byte)words[w];
            }
            if (compress == words.Count)
                pos += (8 - total) * 2;
            if (v4 != null)
                Array.Copy(v4, 0, result, pos, 4);
            return result;
        }

        /// <summary>inet_addr: one to four parts, each decimal, octal (0) or hex (0x), the last filling the rest.</summary>
        internal static byte[] InetAddr(string text)
        {
            var values = new List<ulong>();
            int i = 0, n = text.Length;
            while (true)
            {
                if (i >= n || !char.IsAsciiDigit(text[i]))
                    return null;
                ulong value = 0;
                int radix = 10;
                if (text[i] == '0')
                {
                    radix = 8;
                    i++;
                    if (i < n && (text[i] == 'x' || text[i] == 'X'))
                    {
                        radix = 16;
                        i++;
                        if (i >= n || !Uri.IsHexDigit(text[i]))
                            return null;
                    }
                }
                while (i < n)
                {
                    char c = text[i];
                    int digit;
                    if (char.IsAsciiDigit(c))
                        digit = c - '0';
                    else if (radix == 16 && Uri.IsHexDigit(c))
                        digit = Convert.ToInt32(c.ToString(), 16);
                    else
                        break;
                    if (digit >= radix)
                        return null;
                    value = value * (ulong)radix + (ulong)digit;
                    if (value > uint.MaxValue)
                        return null;
                    i++;
                }
                values.Add(value);
                if (i < n && text[i] == '.')
                {
                    if (values.Count == 4)
                        return null;
                    i++;
                    continue;
                }
                break;
            }
            // What follows the address must be a space (inet_addr stops there).
            if (i < n && !char.IsWhiteSpace(text[i]))
                return null;
            ulong result;
            switch (values.Count)
            {
                case 1:
                    // A single number is the whole address; INADDR_NONE is the error value.
                    if (values[0] >= uint.MaxValue) return null;
                    result = values[0];
                    break;
                case 2:
                    if (values[0] > 0xff || values[1] > 0xffffff) return null;
                    result = values[0] << 24 | values[1];
                    break;
                case 3:
                    if (values[0] > 0xff || values[1] > 0xff || values[2] > 0xffff) return null;
                    result = values[0] << 24 | values[1] << 16 | values[2];
                    break;
                default:
                    if (values[0] > 0xff || values[1] > 0xff || values[2] > 0xff || values[3] > 0xff) return null;
                    result = values[0] << 24 | values[1] << 16 | values[2] << 8 | values[3];
                    break;
            }
            return new[] { (byte)(result >> 24), (byte)(result >> 16), (byte)(result >> 8), (byte)result };
        }

        // ---- inet_ntop ----

        internal static string IPv4ToString(byte[] a, int offset = 0) =>
            $"{a[offset]}.{a[offset + 1]}.{a[offset + 2]}.{a[offset + 3]}";

        /// <summary>
        /// RtlIpv6AddressToString: the longest run of two or more zero groups (the first of equal
        /// ones) becomes "::"; an IPv4-compatible, -mapped or -translated address, and an ISATAP
        /// one, ends in dotted IPv4.
        /// </summary>
        internal static string IPv6ToString(byte[] a)
        {
            var w = new int[8];
            for (int i = 0; i < 8; i++)
                w[i] = a[2 * i] << 8 | a[2 * i + 1];
            if (w[0] == 0 && w[1] == 0 && w[2] == 0 && w[3] == 0 && w[6] != 0)
            {
                if (w[4] == 0 && w[5] == 0)
                    return "::" + IPv4ToString(a, 12);
                if (w[4] == 0 && w[5] == 0xffff)
                    return "::ffff:" + IPv4ToString(a, 12);
                if (w[4] == 0xffff && w[5] == 0)
                    return "::ffff:0:" + IPv4ToString(a, 12);
            }
            if ((w[4] == 0 || w[4] == 0x200) && w[5] == 0x5efe)
                return Groups(w, 6) + ":" + IPv4ToString(a, 12);
            return Groups(w, 8);
        }

        private static string Groups(int[] w, int count)
        {
            int bestStart = -1, bestLength = 1;
            for (int i = 0; i < count;)
            {
                if (w[i] != 0) { i++; continue; }
                int j = i;
                while (j < count && w[j] == 0) j++;
                if (j - i > bestLength) { bestStart = i; bestLength = j - i; }
                i = j;
            }
            var text = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i == bestStart)
                {
                    text.Append("::");
                    i += bestLength - 1;
                    continue;
                }
                if (text.Length > 0 && text[^1] != ':')
                    text.Append(':');
                text.Append(w[i].ToString("x"));
            }
            return text.ToString();
        }

        /// <summary>The text of an address as inet_ntop gives it (no zone).</summary>
        internal static string AddressText(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4 ? IPv4ToString(bytes) : IPv6ToString(bytes);
        }

        // ---- getaddrinfo ----

        /// <summary>A numeric host as getaddrinfo reads it: an IPv4 dotted quad, or IPv6 with an optional %scope.</summary>
        internal static IPAddress NumericHost(string host)
        {
            var v4 = ParseIPv4Strict(host);
            if (v4 != null)
                return new IPAddress(v4);
            string zone = null;
            int percent = host.IndexOf('%');
            if (percent >= 0)
            {
                zone = host.Substring(percent + 1);
                host = host.Substring(0, percent);
            }
            var v6 = ParseIPv6Strict(host);
            if (v6 == null)
                return null;
            long scope = 0;
            if (zone != null && (zone.Length == 0 || !long.TryParse(zone, System.Globalization.NumberStyles.None, null, out scope) || scope > uint.MaxValue))
                return null;
            return new IPAddress(v6, scope);
        }

        // The service as getaddrinfo reads it: digits (strtoul, the low 16 bits), or a name of the services file.
        private static bool NumericService(string service, out int port)
        {
            port = 0;
            int i = 0;
            while (i < service.Length && char.IsWhiteSpace(service[i])) i++;
            bool negative = false;
            if (i < service.Length && (service[i] == '-' || service[i] == '+'))
            {
                negative = service[i] == '-';
                i++;
            }
            if (i == service.Length)
                return service.Length == 0;
            ulong value = 0;
            for (; i < service.Length; i++)
            {
                char c = service[i];
                if (c < '0' || c > '9')
                    return false;
                value = unchecked(value * 10 + (ulong)(c - '0'));
            }
            if (negative)
                value = unchecked(0 - value);
            port = (int)(value & 0xffff);
            return true;
        }

        /// <summary>
        /// getaddrinfo (ws2_32) for hosts and services: hints checked first, then the service, then
        /// the host. A host that is not numeric goes to Dns.GetHostAddresses (the system resolver);
        /// a text .NET would read as an address but getaddrinfo does not ("127.1") is EAI_NONAME
        /// without asking it.
        /// </summary>
        internal static List<AddrInfo> GetAddrInfo(string host, string service, int family, int socktype, int protocol, int flags)
        {
            if ((flags & AI_CANONNAME) != 0 && (host == null || (flags & AI_FQDN) != 0))
                throw new GaiException(EAI_BADFLAGS);
            if (family != AF_UNSPEC && family != AF_INET && family != AF_INET6)
                throw new GaiException(EAI_FAMILY);
            if (socktype < 0 || socktype > 5)
                throw new GaiException(EAI_SOCKTYPE);
            if (host == null && service == null)
                throw new GaiException(EAI_NONAME);
            // The service.
            int port = 0;
            int resultType = socktype;
            if (service != null && !NumericService(service, out port))
            {
                if ((flags & AI_NUMERICSERV) != 0)
                    throw new GaiException(EAI_NONAME);
                int tcp = socktype is 0 or SOCK_STREAM ? ServicePort(service, "tcp") : -1;
                int udp = socktype is 0 or SOCK_DGRAM ? ServicePort(service, "udp") : -1;
                if (tcp < 0 && udp < 0)
                    throw new GaiException(EAI_SERVICE);
                port = tcp >= 0 ? tcp : udp;
                if (socktype == 0)
                    resultType = tcp >= 0 && udp >= 0 ? 0 : tcp >= 0 ? SOCK_STREAM : SOCK_DGRAM;
            }
            // The host.
            var addresses = new List<IPAddress>();
            string canon = null;
            if (host == null)
            {
                bool passive = (flags & AI_PASSIVE) != 0;
                if (family != AF_INET)
                    addresses.Add(passive ? IPAddress.IPv6Any : IPAddress.IPv6Loopback);
                if (family != AF_INET6)
                    addresses.Add(passive ? IPAddress.Any : IPAddress.Loopback);
            }
            else if (NumericHost(host) is { } numeric)
            {
                if (family == AF_INET && numeric.AddressFamily != AddressFamily.InterNetwork
                    || family == AF_INET6 && numeric.AddressFamily != AddressFamily.InterNetworkV6)
                    throw new GaiException(EAI_NONAME);
                addresses.Add(numeric);
                if ((flags & AI_CANONNAME) != 0)
                    canon = AddressToString(numeric, port);
            }
            else if ((flags & AI_NUMERICHOST) != 0 || IPAddress.TryParse(host, out _))
            {
                throw new GaiException(EAI_NONAME);
            }
            else
            {
                var hint = family == AF_INET ? AddressFamily.InterNetwork : family == AF_INET6 ? AddressFamily.InterNetworkV6 : AddressFamily.Unspecified;
                try
                {
                    if ((flags & AI_CANONNAME) != 0)
                    {
                        var entry = Dns.GetHostEntry(host, hint);
                        canon = entry.HostName;
                        addresses.AddRange(entry.AddressList);
                    }
                    else
                    {
                        addresses.AddRange(Dns.GetHostAddresses(host, hint));
                    }
                }
                catch (SocketException error)
                {
                    throw new GaiException(SocketModule.ErrorCodeOf(error));
                }
                catch (ArgumentException)
                {
                    throw new GaiException(EAI_NONAME);
                }
                if (addresses.Count == 0)
                    throw new GaiException(EAI_NONAME);
            }
            var result = new List<AddrInfo>();
            foreach (var address in addresses)
            {
                result.Add(new AddrInfo
                {
                    Family = address.AddressFamily == AddressFamily.InterNetwork ? AF_INET : AF_INET6,
                    SockType = resultType,
                    Protocol = protocol,
                    CanonName = result.Count == 0 ? canon : null,
                    Address = address,
                    Port = port,
                });
            }
            return result;
        }

        /// <summary>WSAAddressToString: "a.b.c.d[:port]", "[addr%scope]:port" or "addr%scope".</summary>
        private static string AddressToString(IPAddress address, int port)
        {
            if (address.AddressFamily == AddressFamily.InterNetwork)
                return port == 0 ? AddressText(address) : $"{AddressText(address)}:{port}";
            var text = AddressText(address) + (address.ScopeId != 0 ? "%" + address.ScopeId : "");
            return port == 0 ? text : $"[{text}]:{port}";
        }

        /// <summary>getnameinfo for an address getaddrinfo(AI_NUMERICHOST) has read.</summary>
        internal static (string Host, string Service) GetNameInfo(IPAddress address, int port, int flags)
        {
            string host = null;
            if ((flags & NI_NUMERICHOST) == 0)
            {
                try
                {
                    host = Dns.GetHostEntry(address).HostName;
                }
                catch (SocketException)
                {
                    host = null;
                }
                catch (ArgumentException)
                {
                    host = null;
                }
                if (host != null && (flags & NI_NOFQDN) != 0)
                {
                    int dot = host.IndexOf('.');
                    if (dot > 0)
                        host = host.Substring(0, dot);
                }
            }
            if (host == null)
            {
                if ((flags & NI_NAMEREQD) != 0)
                    throw new GaiException(EAI_NONAME);
                host = AddressText(address) + (address.AddressFamily == AddressFamily.InterNetworkV6 && address.ScopeId != 0 ? "%" + address.ScopeId : "");
            }
            string service = null;
            if ((flags & NI_NUMERICSERV) == 0)
                service = ServiceName(port, (flags & NI_DGRAM) != 0 ? "udp" : "tcp");
            return (host, service ?? port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        // ---- the netdb files ----

        private static List<(string Name, string[] Aliases, int Port, string Proto)> _services;
        private static List<(string Name, string[] Aliases, int Number)> _protocols;

        /// <summary>
        /// %SystemRoot%\System32\drivers\etc\services, which getservbyname and getservbyport read on
        /// Windows (/etc/services elsewhere). .NET has no services API. Without the file there is no
        /// service: the lookups fail as for an unknown name.
        /// </summary>
        private static List<(string Name, string[] Aliases, int Port, string Proto)> Services()
        {
            if (_services != null)
                return _services;
            var list = new List<(string, string[], int, string)>();
            foreach (var fields in NetdbLines("services"))
            {
                if (fields.Length < 2)
                    continue;
                var slash = fields[1].IndexOf('/');
                if (slash <= 0 || !int.TryParse(fields[1].AsSpan(0, slash), out int port))
                    continue;
                list.Add((fields[0], fields[2..], port, fields[1].Substring(slash + 1)));
            }
            return _services = list;
        }

        private static List<(string Name, string[] Aliases, int Number)> Protocols()
        {
            if (_protocols != null)
                return _protocols;
            var list = new List<(string, string[], int)>();
            foreach (var fields in NetdbLines(OperatingSystem.IsWindows() ? "protocol" : "protocols"))
            {
                if (fields.Length < 2 || !int.TryParse(fields[1], out int number))
                    continue;
                list.Add((fields[0], fields[2..], number));
            }
            return _protocols = list;
        }

        private static IEnumerable<string[]> NetdbLines(string name)
        {
            string path = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", name)
                : Path.Combine("/etc", name);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
            {
                yield break;
            }
            foreach (var raw in lines)
            {
                var line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0)
                    line = line.Substring(0, hash);
                var fields = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length > 0)
                    yield return fields;
            }
        }

        private static bool NameMatches(string name, string[] aliases, string wanted)
        {
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var alias in aliases)
                if (string.Equals(alias, wanted, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>getservbyname: the port, or -1.</summary>
        internal static int ServicePort(string name, string proto)
        {
            foreach (var entry in Services())
                if (NameMatches(entry.Name, entry.Aliases, name) && (proto == null || string.Equals(entry.Proto, proto, StringComparison.OrdinalIgnoreCase)))
                    return entry.Port;
            return -1;
        }

        /// <summary>getservbyport: the name, or null.</summary>
        internal static string ServiceName(int port, string proto)
        {
            foreach (var entry in Services())
                if (entry.Port == port && (proto == null || string.Equals(entry.Proto, proto, StringComparison.OrdinalIgnoreCase)))
                    return entry.Name;
            return null;
        }

        /// <summary>getprotobyname: the number, or -1.</summary>
        internal static int ProtocolNumber(string name)
        {
            foreach (var entry in Protocols())
                if (NameMatches(entry.Name, entry.Aliases, name))
                    return entry.Number;
            return -1;
        }
    }
}
