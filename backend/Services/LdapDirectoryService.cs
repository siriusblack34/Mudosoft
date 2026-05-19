using Novell.Directory.Ldap;
using Novell.Directory.Ldap.Controls;

namespace Orchestra.Backend.Services;

public record AdOuNode(string Dn, string Name, string? ParentDn, int ComputerCount);
public record AdComputer(
    string Dn,
    string Name,
    string? DnsHostName,
    string? OperatingSystem,
    string? OperatingSystemVersion,
    string? Description,
    DateTime? LastLogon,
    bool Enabled,
    string? OuDn);
public record AdGroup(string Dn, string Name, string? Description, int MemberCount);
public record AdUser(
    string Dn,
    string SamAccountName,
    string? DisplayName,
    string? UserPrincipalName,
    string? Email,
    string? Description,
    DateTime? LastLogon,
    bool Enabled,
    string? OuDn);

public interface ILdapDirectoryService
{
    bool IsAvailable { get; }
    Task<List<AdOuNode>> GetOuTreeAsync(CancellationToken ct = default);
    Task<List<AdComputer>> GetComputersInOuAsync(string ouDn, bool recursive, CancellationToken ct = default);
    Task<List<AdGroup>> GetGroupsAsync(CancellationToken ct = default);
    Task<List<AdComputer>> GetComputersInGroupAsync(string groupDn, CancellationToken ct = default);
    Task<List<AdUser>> GetUsersInOuAsync(string ouDn, bool recursive, CancellationToken ct = default);
    Task<List<AdUser>> SearchUsersAsync(string query, int limit, CancellationToken ct = default);
    Task<string?> ResolveHostnameAsync(string hostname, CancellationToken ct = default);
}

public class LdapDirectoryService : ILdapDirectoryService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LdapDirectoryService> _logger;

    public LdapDirectoryService(IConfiguration configuration, ILogger<LdapDirectoryService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            if (!_configuration.GetValue<bool>("Ldap:Enabled", false)) return false;
            var server = _configuration["Ldap:Server"];
            var (user, pass) = GetServiceCredentials();
            return !string.IsNullOrWhiteSpace(server)
                && !string.IsNullOrWhiteSpace(user)
                && !string.IsNullOrWhiteSpace(pass);
        }
    }

    private (string user, string pass) GetServiceCredentials()
    {
        // 1) LDAP_BIND_USER + LDAP_BIND_PASSWORD env varları öncelikli (dedicated AD bind hesabı)
        var ldapUser = Environment.GetEnvironmentVariable("LDAP_BIND_USER");
        var ldapPass = Environment.GetEnvironmentVariable("LDAP_BIND_PASSWORD");
        if (!string.IsNullOrWhiteSpace(ldapUser) && !string.IsNullOrWhiteSpace(ldapPass))
            return (ldapUser, ldapPass);

        // 2) Fallback: WMI account'ı reuse et (eğer LDAP bind yetkisi varsa)
        var wmiSection = _configuration.GetSection("MudoSoft:Wmi");
        var wmiDomain = wmiSection["Domain"] ?? _configuration["Ldap:Domain"] ?? "";
        var wmiUser = wmiSection["Username"] ?? "";
        var passwordSecretKey = wmiSection["PasswordSecretKey"];

        string pass = "";
        if (!string.IsNullOrWhiteSpace(passwordSecretKey))
            pass = Environment.GetEnvironmentVariable(passwordSecretKey) ?? "";
        if (string.IsNullOrWhiteSpace(pass))
            pass = Environment.GetEnvironmentVariable("MUDOSOFT_WMI_PASSWORD")
                ?? Environment.GetEnvironmentVariable("WMI_PASSWORD")
                ?? wmiSection["Password"]
                ?? "";

        var upnSuffix = _configuration["Ldap:UpnSuffix"] ?? "";
        string bindUser;
        if (string.IsNullOrEmpty(wmiUser))
            bindUser = "";
        else if (wmiUser.Contains('@') || wmiUser.Contains('\\'))
            bindUser = wmiUser;
        else if (!string.IsNullOrEmpty(upnSuffix))
            bindUser = wmiUser + upnSuffix;
        else if (!string.IsNullOrEmpty(wmiDomain))
            bindUser = wmiDomain + "\\" + wmiUser;
        else
            bindUser = wmiUser;

        return (bindUser, pass);
    }

    private LdapConnection Connect()
    {
        var server = _configuration["Ldap:Server"] ?? throw new InvalidOperationException("Ldap:Server not configured");
        var port = _configuration.GetValue<int>("Ldap:Port", 389);
        var useSsl = _configuration.GetValue<bool>("Ldap:UseSsl", false);
        var timeoutMs = _configuration.GetValue<int>("Ldap:TimeoutMs", 8000);
        var (user, pass) = GetServiceCredentials();
        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
            throw new InvalidOperationException("LDAP service account credentials are not available");

        var conn = new LdapConnection { ConnectionTimeout = timeoutMs };
        if (useSsl) conn.SecureSocketLayer = true;
        conn.Connect(server, port);
        conn.Bind(LdapConnection.LdapV3, user, pass);
        return conn;
    }

    public Task<List<AdOuNode>> GetOuTreeAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var baseDn = _configuration["Ldap:SearchBase"] ?? "";
            using var conn = Connect();
            var ous = new List<AdOuNode>();

            // OU listesi
            var ouResults = SearchAll(conn, baseDn, LdapConnection.ScopeSub, "(objectClass=organizationalUnit)",
                new[] { "distinguishedName", "ou", "name" });
            foreach (var entry in ouResults)
            {
                var dn = entry.Dn;
                var name = GetAttr(entry, "ou") ?? GetAttr(entry, "name") ?? dn;
                var parent = GetParentDn(dn);
                ous.Add(new AdOuNode(dn, name, parent, 0));
            }

            // Computer sayısı — her OU için tek seferde toplu sorgu yapılması daha verimli ama
            // OU sayısı genelde 50-200 arası, ayrı sorgular kabul edilebilir.
            // Toplu yaklaşım: tüm computer'ları tek sorgu ile çek, OU'larına grupla.
            var allComputers = SearchAll(conn, baseDn, LdapConnection.ScopeSub, "(objectCategory=computer)",
                new[] { "distinguishedName" });
            var ouCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in allComputers)
            {
                var parent = GetParentDn(c.Dn);
                if (parent == null) continue;
                ouCounts.TryGetValue(parent, out var cnt);
                ouCounts[parent] = cnt + 1;
            }

            return ous
                .Select(o => new AdOuNode(o.Dn, o.Name, o.ParentDn,
                    ouCounts.TryGetValue(o.Dn, out var c) ? c : 0))
                .OrderBy(o => o.Dn.Length).ThenBy(o => o.Name)
                .ToList();
        }, ct);
    }

    public Task<List<AdComputer>> GetComputersInOuAsync(string ouDn, bool recursive, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = Connect();
            var scope = recursive ? LdapConnection.ScopeSub : LdapConnection.ScopeOne;
            var results = SearchAll(conn, ouDn, scope, "(objectCategory=computer)",
                new[] { "cn", "dNSHostName", "operatingSystem", "operatingSystemVersion",
                    "description", "lastLogonTimestamp", "userAccountControl", "distinguishedName" });
            return results.Select(MapComputer).OrderBy(c => c.Name).ToList();
        }, ct);
    }

    public Task<List<AdGroup>> GetGroupsAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var baseDn = _configuration["Ldap:SearchBase"] ?? "";
            using var conn = Connect();
            var results = SearchAll(conn, baseDn, LdapConnection.ScopeSub, "(objectCategory=group)",
                new[] { "cn", "description", "member" });
            return results.Select(e =>
            {
                var name = GetAttr(e, "cn") ?? e.Dn;
                var desc = GetAttr(e, "description");
                var members = GetAttrArr(e, "member");
                return new AdGroup(e.Dn, name, desc, members.Length);
            })
            .Where(g => g.MemberCount > 0)  // Boş grupları gizle, UI temizliği
            .OrderBy(g => g.Name)
            .ToList();
        }, ct);
    }

    public Task<List<AdComputer>> GetComputersInGroupAsync(string groupDn, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = Connect();
            var groupResult = SearchAll(conn, groupDn, LdapConnection.ScopeBase, "(objectClass=*)",
                new[] { "member" }).FirstOrDefault();
            if (groupResult == null) return new List<AdComputer>();

            var memberDns = GetAttrArr(groupResult, "member");
            var computers = new List<AdComputer>();
            foreach (var memberDn in memberDns)
            {
                try
                {
                    var entry = SearchAll(conn, memberDn, LdapConnection.ScopeBase, "(objectCategory=computer)",
                        new[] { "cn", "dNSHostName", "operatingSystem", "operatingSystemVersion",
                            "description", "lastLogonTimestamp", "userAccountControl", "distinguishedName" })
                        .FirstOrDefault();
                    if (entry != null) computers.Add(MapComputer(entry));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Group member lookup failed: {Dn}", memberDn);
                }
            }
            return computers.OrderBy(c => c.Name).ToList();
        }, ct);
    }

    public Task<List<AdUser>> GetUsersInOuAsync(string ouDn, bool recursive, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            using var conn = Connect();
            var scope = recursive ? LdapConnection.ScopeSub : LdapConnection.ScopeOne;
            // (objectCategory=person)(objectClass=user) — bilgisayar değil, gerçek user'lar
            var results = SearchAll(conn, ouDn, scope, "(&(objectCategory=person)(objectClass=user)(!(objectClass=computer)))",
                new[] { "cn", "sAMAccountName", "displayName", "userPrincipalName", "mail",
                    "description", "lastLogonTimestamp", "userAccountControl", "distinguishedName" });
            return results.Select(MapUser).OrderBy(u => u.DisplayName ?? u.SamAccountName).ToList();
        }, ct);
    }

    public Task<List<AdUser>> SearchUsersAsync(string query, int limit, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var baseDn = _configuration["Ldap:SearchBase"] ?? "";
            using var conn = Connect();
            var q = (query ?? "").Trim().Replace("(", "").Replace(")", "").Replace("*", "");
            if (string.IsNullOrEmpty(q)) return new List<AdUser>();
            var filter = $"(&(objectCategory=person)(objectClass=user)(!(objectClass=computer))" +
                         $"(|(sAMAccountName=*{q}*)(displayName=*{q}*)(userPrincipalName=*{q}*)(mail=*{q}*)))";
            var results = SearchAll(conn, baseDn, LdapConnection.ScopeSub, filter,
                new[] { "cn", "sAMAccountName", "displayName", "userPrincipalName", "mail",
                    "description", "lastLogonTimestamp", "userAccountControl", "distinguishedName" });
            return results.Take(limit).Select(MapUser).OrderBy(u => u.DisplayName ?? u.SamAccountName).ToList();
        }, ct);
    }

    private AdUser MapUser(LdapEntry e)
    {
        var sam = GetAttr(e, "sAMAccountName") ?? GetAttr(e, "cn") ?? e.Dn;
        var displayName = GetAttr(e, "displayName");
        var upn = GetAttr(e, "userPrincipalName");
        var mail = GetAttr(e, "mail");
        var desc = GetAttr(e, "description");
        var lastLogonRaw = GetAttr(e, "lastLogonTimestamp");
        DateTime? lastLogon = null;
        if (long.TryParse(lastLogonRaw, out var ft) && ft > 0)
        {
            try { lastLogon = DateTime.FromFileTimeUtc(ft); } catch { }
        }
        var uacRaw = GetAttr(e, "userAccountControl");
        var enabled = true;
        if (int.TryParse(uacRaw, out var uac))
            enabled = (uac & 0x2) == 0;
        return new AdUser(e.Dn, sam, displayName, upn, mail, desc, lastLogon, enabled, GetParentDn(e.Dn));
    }

    public Task<string?> ResolveHostnameAsync(string hostname, CancellationToken ct = default)
    {
        return Task.Run<string?>(() =>
        {
            try
            {
                var entry = System.Net.Dns.GetHostEntry(hostname);
                var ipv4 = entry.AddressList
                    .FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return ipv4?.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogWarning("DNS resolve failed for {Host}: {Msg}", hostname, ex.Message);
                return null;
            }
        }, ct);
    }

    // Novell library: GetAttribute eksik attribute'ta exception atar (null dönmez)
    private static string? GetAttr(LdapEntry e, string name)
    {
        try { return e.GetAttribute(name)?.StringValue; } catch { return null; }
    }

    private static string[] GetAttrArr(LdapEntry e, string name)
    {
        try { return e.GetAttribute(name)?.StringValueArray ?? Array.Empty<string>(); } catch { return Array.Empty<string>(); }
    }

    private AdComputer MapComputer(LdapEntry e)
    {
        var name = GetAttr(e, "cn") ?? e.Dn;
        var dns = GetAttr(e, "dNSHostName");
        var os = GetAttr(e, "operatingSystem");
        var osVer = GetAttr(e, "operatingSystemVersion");
        var desc = GetAttr(e, "description");
        var lastLogonRaw = GetAttr(e, "lastLogonTimestamp");
        DateTime? lastLogon = null;
        if (long.TryParse(lastLogonRaw, out var ft) && ft > 0)
        {
            try { lastLogon = DateTime.FromFileTimeUtc(ft); } catch { /* aralık dışı */ }
        }
        var uacRaw = GetAttr(e, "userAccountControl");
        var enabled = true;
        if (int.TryParse(uacRaw, out var uac))
            enabled = (uac & 0x2) == 0;  // bit 1 = ACCOUNTDISABLE

        return new AdComputer(e.Dn, name, dns, os, osVer, desc, lastLogon, enabled, GetParentDn(e.Dn));
    }

    private static string? GetParentDn(string dn)
    {
        var idx = dn.IndexOf(',');
        return idx > 0 && idx < dn.Length - 1 ? dn[(idx + 1)..] : null;
    }

    private static List<LdapEntry> SearchAll(LdapConnection conn, string baseDn, int scope, string filter, string[] attrs)
    {
        const int pageSize = 500;
        var results = new List<LdapEntry>();
        var cookie = Array.Empty<byte>();

        do
        {
            var constraints = new LdapSearchConstraints();
            constraints.SetControls(new LdapControl[]
            {
                new SimplePagedResultsControl(pageSize, cookie)
            });

            var queue = conn.Search(baseDn, scope, filter, attrs, false, (LdapSearchQueue?)null, constraints);
            LdapMessage? msg;
            while ((msg = queue.GetResponse()) != null)
            {
                if (msg is LdapSearchResult sr)
                    results.Add(sr.Entry);
                else if (msg is LdapResponse resp)
                {
                    cookie = Array.Empty<byte>();
                    if (resp.Controls != null)
                    {
                        foreach (var ctl in resp.Controls)
                        {
                            if (ctl is SimplePagedResultsControl paged)
                            {
                                cookie = paged.Cookie ?? Array.Empty<byte>();
                            }
                        }
                    }
                }
            }
        } while (cookie.Length > 0);

        return results;
    }
}
