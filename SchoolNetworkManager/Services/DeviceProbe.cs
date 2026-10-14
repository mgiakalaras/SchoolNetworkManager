using System.Net;
using System.Net.Sockets;

namespace SchoolNetworkManager.Services;

public class ProbeResult
{
    public string Ip { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string? Mac { get; set; }
    public string? Name { get; set; }
    public string? Vendor { get; set; }
    public List<PortInfo> OpenPorts { get; set; } = new();
    public string? SuggestedType { get; set; }
    public bool HasVeyon { get; set; }
    public bool PortsChecked { get; set; }

    public string OpenPortsText => NetworkTools.FormatPorts(OpenPorts);
}

/// <summary>
/// Συλλέγει αυτόματα στοιχεία για μια συσκευή: online, MAC, όνομα, κατασκευαστή, ports.
/// </summary>
public class DeviceProbe
{
    private readonly OuiLookup _oui;
    private readonly SettingsService _settings;

    public DeviceProbe(OuiLookup oui, SettingsService settings)
    {
        _oui = oui;
        _settings = settings;
    }

    public async Task<ProbeResult> ProbeAsync(string ipAddress, bool includePorts = true)
    {
        var results = await ScanAsync(new[] { ipAddress }, includePorts, onlyAlive: false);

        return results.FirstOrDefault() ?? new ProbeResult { Ip = ipAddress };
    }

    /// <summary>Ελέγχει πολλές IP παράλληλα.</summary>
    public async Task<List<ProbeResult>> ScanAsync(IEnumerable<string> ipAddresses, bool includePorts, bool onlyAlive = true)
    {
        var ips = ipAddresses
            .Select(ip => ip.Trim())
            .Where(IsIPv4)
            .Distinct()
            .ToList();

        var ports = NetworkTools.ParsePorts(_settings.Current.PortsToScan);

        // 1ο βήμα: ping + NetBIOS παράλληλα σε όλες τις IP
        var firstPass = new SemaphoreSlim(64);

        var basicTasks = ips.Select(async ip =>
        {
            await firstPass.WaitAsync();

            try
            {
                var pingTask = NetworkTools.PingAsync(ip, 900);
                var netBiosTask = NetworkTools.NetBiosQueryAsync(ip, 1000);

                await Task.WhenAll(pingTask, netBiosTask);

                return new
                {
                    Ip = ip,
                    Ping = pingTask.Result,
                    NetBiosName = netBiosTask.Result.Name,
                    NetBiosMac = netBiosTask.Result.Mac
                };
            }
            finally
            {
                firstPass.Release();
            }
        });

        var basics = await Task.WhenAll(basicTasks);

        // Ο πίνακας ARP γεμίζει μετά τα παραπάνω πακέτα
        var arp = await NetworkTools.GetArpTableAsync();

        var candidates = basics
            .Select(b => new
            {
                b.Ip,
                b.Ping,
                b.NetBiosName,
                Mac = arp.TryGetValue(b.Ip, out var arpMac) ? arpMac : b.NetBiosMac,
                InArp = arp.ContainsKey(b.Ip)
            })
            .Where(b => !onlyAlive || b.Ping || b.NetBiosName != null || b.InArp)
            .ToList();

        // 2ο βήμα: DNS και ports μόνο για όσες απάντησαν
        var secondPass = new SemaphoreSlim(16);

        var detailTasks = candidates.Select(async c =>
        {
            await secondPass.WaitAsync();

            try
            {
                var dnsTask = c.NetBiosName == null
                    ? NetworkTools.ReverseDnsAsync(c.Ip)
                    : Task.FromResult<string?>(null);

                var portsTask = includePorts
                    ? NetworkTools.ScanPortsAsync(c.Ip, ports)
                    : Task.FromResult(new List<PortInfo>());

                await Task.WhenAll(dnsTask, portsTask);

                var openPorts = portsTask.Result;
                var vendor = _oui.Lookup(c.Mac);

                return new ProbeResult
                {
                    Ip = c.Ip,
                    Online = c.Ping || c.NetBiosName != null || openPorts.Count > 0 || (onlyAlive && c.InArp),
                    Mac = c.Mac,
                    Name = c.NetBiosName ?? dnsTask.Result,
                    Vendor = vendor,
                    OpenPorts = openPorts,
                    PortsChecked = includePorts,
                    HasVeyon = openPorts.Any(p => p.Port == 11100),
                    SuggestedType = NetworkTools.SuggestType(openPorts, vendor)
                };
            }
            finally
            {
                secondPass.Release();
            }
        });

        var details = await Task.WhenAll(detailTasks);

        return details
            .OrderBy(d => NetworkTools.IpToNumber(d.Ip))
            .ToList();
    }

    private static bool IsIPv4(string ip)
    {
        return IPAddress.TryParse(ip, out var address) &&
               address.AddressFamily == AddressFamily.InterNetwork &&
               ip.Count(ch => ch == '.') == 3;
    }
}
