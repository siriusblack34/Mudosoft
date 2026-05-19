using Novell.Directory.Ldap;

namespace Orchestra.Backend.Services;

public record LdapAuthResult(bool Success, string? DisplayName, string? Email, string? FailureReason);

public interface ILdapAuthService
{
    bool IsEnabled { get; }
    Task<LdapAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default);
}

public class LdapAuthService : ILdapAuthService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<LdapAuthService> _logger;

    public LdapAuthService(IConfiguration configuration, ILogger<LdapAuthService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public bool IsEnabled => _configuration.GetValue<bool>("Ldap:Enabled", false);

    public Task<LdapAuthResult> AuthenticateAsync(string username, string password, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return Task.FromResult(new LdapAuthResult(false, null, null, "LDAP is disabled"));

        var server = Environment.GetEnvironmentVariable("LDAP_SERVER") ?? _configuration["Ldap:Server"];
        var port = _configuration.GetValue<int>("Ldap:Port", 389);
        var useSsl = _configuration.GetValue<bool>("Ldap:UseSsl", false);
        var upnSuffix = _configuration["Ldap:UpnSuffix"] ?? "";
        var domain = _configuration["Ldap:Domain"] ?? "";
        var searchBase = _configuration["Ldap:SearchBase"] ?? "";
        var timeoutMs = _configuration.GetValue<int>("Ldap:TimeoutMs", 5000);

        if (string.IsNullOrWhiteSpace(server))
        {
            _logger.LogWarning("LDAP server not configured");
            return Task.FromResult(new LdapAuthResult(false, null, null, "LDAP server not configured"));
        }

        // Kullanıcı UPN veya NT-style bind name'i belirle
        // Kullanıcı zaten @ veya \ içeriyorsa olduğu gibi bırak; yoksa UPN ekle (yoksa domain\user dene)
        string bindUser;
        if (username.Contains('@') || username.Contains('\\'))
            bindUser = username;
        else if (!string.IsNullOrEmpty(upnSuffix))
            bindUser = username + upnSuffix;
        else if (!string.IsNullOrEmpty(domain))
            bindUser = domain + "\\" + username;
        else
            bindUser = username;

        return Task.Run(() =>
        {
            using var conn = new LdapConnection { ConnectionTimeout = timeoutMs };
            try
            {
                if (useSsl)
                    conn.SecureSocketLayer = true;

                conn.Connect(server, port);
                conn.Bind(LdapConnection.LdapV3, bindUser, password);

                if (!conn.Bound)
                {
                    _logger.LogInformation("LDAP bind failed (not bound): {User}", username);
                    return new LdapAuthResult(false, null, null, "Bind failed");
                }

                string? displayName = null;
                string? email = null;

                if (!string.IsNullOrEmpty(searchBase))
                {
                    try
                    {
                        var sAccountName = username.Contains('@') ? username.Split('@')[0]
                            : username.Contains('\\') ? username.Split('\\')[1]
                            : username;

                        var filter = $"(&(objectClass=user)(sAMAccountName={EscapeFilter(sAccountName)}))";
                        var attrs = new[] { "displayName", "mail", "cn" };
                        var results = conn.Search(searchBase, LdapConnection.ScopeSub, filter, attrs, false);

                        if (results.HasMore())
                        {
                            var entry = results.Next();
                            displayName = entry.GetAttribute("displayName")?.StringValue
                                ?? entry.GetAttribute("cn")?.StringValue;
                            email = entry.GetAttribute("mail")?.StringValue;
                        }
                    }
                    catch (Exception searchEx)
                    {
                        _logger.LogWarning(searchEx, "LDAP search failed for {User} (auth still succeeded)", username);
                    }
                }

                return new LdapAuthResult(true, displayName, email, null);
            }
            catch (LdapException ex)
            {
                // 49 = InvalidCredentials — beklenen başarısızlık, info seviyesinde logla
                if (ex.ResultCode == LdapException.InvalidCredentials)
                {
                    _logger.LogInformation("LDAP invalid credentials: {User}", username);
                    return new LdapAuthResult(false, null, null, "Invalid credentials");
                }
                _logger.LogWarning(ex, "LDAP error for {User}: code={Code}", username, ex.ResultCode);
                return new LdapAuthResult(false, null, null, $"LDAP error: {ex.ResultCode}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LDAP connection error for {User}", username);
                return new LdapAuthResult(false, null, null, "Connection error");
            }
        }, ct);
    }

    private static string EscapeFilter(string input)
    {
        return input
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}
