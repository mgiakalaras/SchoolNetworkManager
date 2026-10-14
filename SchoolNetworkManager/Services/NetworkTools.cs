using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using SchoolNetworkManager.Models;

namespace SchoolNetworkManager.Services;

public record PortInfo(int Port, string Label);

/// <summary>
/// Βοηθητικά εργαλεία δικτύου: ping, ARP, NetBIOS, reverse DNS, έλεγχος ports, Wake-on-LAN.
/// </summary>
public static class NetworkTools
{
    // ---------------- Ping ----------------

    public static async Task<bool> PingAsync(string? ipAddress, int timeoutMs = 800)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
        {
            return false;
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress.Trim(), timeoutMs);

            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    // ---------------- MAC ----------------

    /// <summary>Μετατρέπει οποιαδήποτε μορφή MAC σε AA:BB:CC:DD:EE:FF.</summary>
    public static string? NormalizeMac(string? mac)
    {
        if (string.IsNullOrWhiteSpace(mac))
        {
            return null;
        }

        var hex = new string(mac.Where(Uri.IsHexDigit).ToArray()).ToUpperInvariant();

        if (hex.Length != 12)
        {
            return mac.Trim();
        }

        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    public static bool IsUsableMac(string? mac)
    {
        return mac != null &&
               mac.Length == 17 &&
               mac != "00:00:00:00:00:00" &&
               mac != "FF:FF:FF:FF:FF:FF";
    }

    /// <summary>Διαβάζει τον πίνακα ARP του συστήματος (IP → MAC).</summary>
    public static async Task<Dictionary<string, string>> GetArpTableAsync()
    {
        var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (OperatingSystem.IsLinux() && File.Exists("/proc/net/arp"))
            {
                var lines = await File.ReadAllLinesAsync("/proc/net/arp");

                foreach (var line in lines.Skip(1))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                    if (parts.Length < 4)
                    {
                        continue;
                    }

                    // flags 0x0 = ημιτελής εγγραφή (η συσκευή δεν απάντησε)
                    if (parts[2] == "0x0")
                    {
                        continue;
                    }

                    var mac = NormalizeMac(parts[3]);

                    if (IsUsableMac(mac))
                    {
                        table[parts[0]] = mac!;
                    }
                }
            }
            else if (OperatingSystem.IsWindows())
            {
                var psi = new ProcessStartInfo("arp", "-a")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);

                if (process != null)
                {
                    var output = await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();

                    var matches = Regex.Matches(
                        output,
                        @"(\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3})\s+([0-9a-fA-F]{2}(?:-[0-9a-fA-F]{2}){5})");

                    foreach (Match match in matches)
                    {
                        var mac = NormalizeMac(match.Groups[2].Value);

                        if (IsUsableMac(mac))
                        {
                            table[match.Groups[1].Value] = mac!;
                        }
                    }
                }
            }
        }
        catch
        {
            // Αν δεν διαβάζεται ο πίνακας ARP, απλά δεν βρίσκουμε MAC.
        }

        return table;
    }

    public static async Task<string?> GetMacFromArpAsync(string ipAddress)
    {
        var table = await GetArpTableAsync();

        return table.TryGetValue(ipAddress.Trim(), out var mac) ? mac : null;
    }

    // ---------------- Όνομα συσκευής ----------------

    /// <summary>
    /// Ερώτημα NetBIOS (UDP 137). Τα Windows απαντούν με το όνομα υπολογιστή και τη MAC,
    /// χωρίς να χρειάζεται κωδικός.
    /// </summary>
    public static async Task<(string? Name, string? Mac)> NetBiosQueryAsync(string ipAddress, int timeoutMs = 1000)
    {
        try
        {
            if (!IPAddress.TryParse(ipAddress.Trim(), out var address) ||
                address.AddressFamily != AddressFamily.InterNetwork)
            {
                return (null, null);
            }

            var request = new byte[50];
            var transactionId = Random.Shared.Next(1, 0xFFFF);

            request[0] = (byte)(transactionId >> 8);
            request[1] = (byte)(transactionId & 0xFF);
            request[5] = 1;      // 1 ερώτηση
            request[12] = 0x20;  // μήκος ονόματος
            request[13] = (byte)'C';
            request[14] = (byte)'K';

            for (var i = 15; i < 45; i++)
            {
                request[i] = (byte)'A';
            }

            request[45] = 0x00;
            request[46] = 0x00;
            request[47] = 0x21;  // NBSTAT
            request[48] = 0x00;
            request[49] = 0x01;  // IN

            using var udp = new UdpClient();
            udp.Connect(address, 137);
            await udp.SendAsync(request, request.Length);

            using var cts = new CancellationTokenSource(timeoutMs);
            var response = await udp.ReceiveAsync(cts.Token);
            var data = response.Buffer;

            var pos = 12;

            if (data.Length <= pos)
            {
                return (null, null);
            }

            // Παράκαμψη ονόματος
            if ((data[pos] & 0xC0) == 0xC0)
            {
                pos += 2;
            }
            else
            {
                while (pos < data.Length && data[pos] != 0)
                {
                    pos += data[pos] + 1;
                }

                pos++;
            }

            pos += 10; // type, class, ttl, rdlength

            if (pos >= data.Length)
            {
                return (null, null);
            }

            int count = data[pos];
            pos++;

            string? name = null;

            for (var i = 0; i < count && pos + 18 <= data.Length; i++)
            {
                var raw = Encoding.ASCII.GetString(data, pos, 15).Trim().TrimEnd('\0').Trim();
                var suffix = data[pos + 15];
                var flags = (data[pos + 16] << 8) | data[pos + 17];
                var isGroup = (flags & 0x8000) != 0;

                if (name == null && suffix == 0x00 && !isGroup && raw.Length > 0)
                {
                    name = raw;
                }

                pos += 18;
            }

            string? mac = null;

            if (pos + 6 <= data.Length)
            {
                mac = NormalizeMac(Convert.ToHexString(data, pos, 6));

                if (!IsUsableMac(mac))
                {
                    mac = null;
                }
            }

            return (name, mac);
        }
        catch
        {
            return (null, null);
        }
    }

    public static async Task<string?> ReverseDnsAsync(string ipAddress, int timeoutMs = 1500)
    {
        try
        {
            if (!IPAddress.TryParse(ipAddress.Trim(), out var address))
            {
                return null;
            }

            var lookup = Dns.GetHostEntryAsync(address);
            var finished = await Task.WhenAny(lookup, Task.Delay(timeoutMs));

            if (finished != lookup)
            {
                return null;
            }

            var host = (await lookup).HostName;

            if (string.IsNullOrWhiteSpace(host) || host == ipAddress)
            {
                return null;
            }

            // Κρατάμε μόνο το πρώτο κομμάτι (π.χ. LAB-PC-01.lan → LAB-PC-01)
            var shortName = host.Split('.')[0];

            return string.IsNullOrWhiteSpace(shortName) ? host : shortName;
        }
        catch
        {
            return null;
        }
    }

    // ---------------- Ports ----------------

    /// <summary>Διαβάζει λίστα της μορφής "22:SSH, 80:Web, 3389".</summary>
    public static List<PortInfo> ParsePorts(string? spec)
    {
        var list = new List<PortInfo>();
        var source = string.IsNullOrWhiteSpace(spec) ? AppSettings.DefaultPorts : spec;

        var items = source.Split(
            new[] { ',', ';', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var item in items)
        {
            var parts = item.Split(':', 2, StringSplitOptions.TrimEntries);

            if (int.TryParse(parts[0], out var port) &&
                port > 0 && port < 65536 &&
                list.All(p => p.Port != port))
            {
                var label = parts.Length > 1 && parts[1].Length > 0 ? parts[1] : port.ToString();
                list.Add(new PortInfo(port, label));
            }
        }

        return list;
    }

    public static async Task<bool> IsPortOpenAsync(string ipAddress, int port, int timeoutMs = 600)
    {
        try
        {
            if (!IPAddress.TryParse(ipAddress.Trim(), out var address))
            {
                return false;
            }

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(timeoutMs);
            await client.ConnectAsync(address, port, cts.Token);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<List<PortInfo>> ScanPortsAsync(string ipAddress, IEnumerable<PortInfo> ports, int timeoutMs = 600)
    {
        var checks = ports.Select(async port => new
        {
            Port = port,
            IsOpen = await IsPortOpenAsync(ipAddress, port.Port, timeoutMs)
        });

        var results = await Task.WhenAll(checks);

        return results
            .Where(r => r.IsOpen)
            .Select(r => r.Port)
            .OrderBy(p => p.Port)
            .ToList();
    }

    public static string FormatPorts(IEnumerable<PortInfo> ports)
    {
        return string.Join(", ", ports.Select(p => $"{p.Port} {p.Label}"));
    }

    /// <summary>Προτείνει τύπο μηχανήματος από τα ανοιχτά ports και τον κατασκευαστή.</summary>
    public static string? SuggestType(IEnumerable<PortInfo> openPorts, string? vendor)
    {
        var ports = openPorts.Select(p => p.Port).ToHashSet();
        var v = vendor ?? string.Empty;

        bool VendorHas(params string[] names) =>
            names.Any(n => v.Contains(n, StringComparison.OrdinalIgnoreCase));

        if (ports.Contains(9100) || ports.Contains(515) || ports.Contains(631) ||
            VendorHas("Epson", "Brother", "Kyocera", "Lexmark", "Ricoh", "Xerox", "Konica", "Canon"))
        {
            return "Εκτυπωτής";
        }

        if (VendorHas("Raspberry"))
        {
            return "Raspberry Pi / SBC";
        }

        if (VendorHas("Synology", "QNAP", "Western Digital"))
        {
            return "NAS / Storage";
        }

        if (VendorHas("Hikvision", "Dahua", "Axis Communications") || ports.Contains(554))
        {
            return "IP Camera / Webcam";
        }

        if (VendorHas("Ubiquiti", "Aruba", "Ruckus"))
        {
            return "Access Point";
        }

        if (VendorHas("Espressif", "Arduino"))
        {
            return "IoT / Arduino";
        }

        if (ports.Contains(3389) || ports.Contains(445) || ports.Contains(139) || ports.Contains(11100))
        {
            return "Υπολογιστής";
        }

        return null;
    }

    // ---------------- Wake-on-LAN ----------------

    public static async Task<bool> SendWakeOnLanAsync(string? mac, string? broadcast)
    {
        var normalized = NormalizeMac(mac);

        if (normalized == null || normalized.Length != 17)
        {
            return false;
        }

        var macBytes = Convert.FromHexString(normalized.Replace(":", string.Empty));
        var packet = new byte[102];

        for (var i = 0; i < 6; i++)
        {
            packet[i] = 0xFF;
        }

        for (var i = 1; i <= 16; i++)
        {
            Buffer.BlockCopy(macBytes, 0, packet, i * 6, 6);
        }

        var targets = new List<IPAddress> { IPAddress.Broadcast };

        if (!string.IsNullOrWhiteSpace(broadcast) &&
            IPAddress.TryParse(broadcast.Trim(), out var directed) &&
            !directed.Equals(IPAddress.Broadcast))
        {
            targets.Add(directed);
        }

        var sent = false;

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        foreach (var target in targets)
        {
            foreach (var port in new[] { 9, 7 })
            {
                try
                {
                    await udp.SendAsync(packet, packet.Length, new IPEndPoint(target, port));
                    sent = true;
                }
                catch
                {
                    // δοκιμάζουμε τον επόμενο προορισμό
                }
            }
        }

        return sent;
    }

    // ---------------- Εύρη IP ----------------

    /// <summary>
    /// Δέχεται: 192.168.2.1-254, 192.168.2.10-192.168.2.50, 192.168.2.0/24
    /// ή πολλά χωρισμένα με κόμμα.
    /// </summary>
    public static List<string> ParseRange(string? range, int max = 1024)
    {
        var result = new List<string>();

        if (string.IsNullOrWhiteSpace(range))
        {
            return result;
        }

        var pieces = range.Split(
            new[] { ',', ';', ' ', '\n', '\r' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var piece in pieces)
        {
            uint start;
            uint end;

            if (piece.Contains('/'))
            {
                var parts = piece.Split('/', 2);

                if (!TryParseIPv4(parts[0], out var baseAddress) ||
                    !int.TryParse(parts[1], out var prefix) ||
                    prefix < 16 || prefix > 32)
                {
                    continue;
                }

                var mask = prefix == 32 ? uint.MaxValue : ~(uint.MaxValue >> prefix);
                var network = baseAddress & mask;
                var broadcast = network | ~mask;

                if (prefix >= 31)
                {
                    start = network;
                    end = broadcast;
                }
                else
                {
                    start = network + 1;
                    end = broadcast - 1;
                }
            }
            else if (piece.Contains('-'))
            {
                var parts = piece.Split('-', 2, StringSplitOptions.TrimEntries);

                if (!TryParseIPv4(parts[0], out start))
                {
                    continue;
                }

                if (TryParseIPv4(parts[1], out var fullEnd))
                {
                    end = fullEnd;
                }
                else if (byte.TryParse(parts[1], out var lastOctet))
                {
                    end = (start & 0xFFFFFF00) | lastOctet;
                }
                else
                {
                    continue;
                }
            }
            else
            {
                if (!TryParseIPv4(piece, out start))
                {
                    continue;
                }

                end = start;
            }

            if (end < start)
            {
                (start, end) = (end, start);
            }

            for (var value = start; value <= end && result.Count < max; value++)
            {
                var ip = FromUInt(value);

                if (!result.Contains(ip))
                {
                    result.Add(ip);
                }

                if (value == uint.MaxValue)
                {
                    break;
                }
            }
        }

        return result;
    }

    private static bool TryParseIPv4(string text, out uint value)
    {
        value = 0;

        if (!IPAddress.TryParse(text.Trim(), out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            text.Count(ch => ch == '.') != 3)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];

        return true;
    }

    private static string FromUInt(uint value)
    {
        return $"{value >> 24}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}";
    }

    public static long IpToNumber(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            return long.MaxValue;
        }

        var parts = ip.Trim().Split('.');

        if (parts.Length != 4 ||
            !byte.TryParse(parts[0], out var a) ||
            !byte.TryParse(parts[1], out var b) ||
            !byte.TryParse(parts[2], out var c) ||
            !byte.TryParse(parts[3], out var d))
        {
            return long.MaxValue;
        }

        return ((long)a << 24) + ((long)b << 16) + ((long)c << 8) + d;
    }
}
