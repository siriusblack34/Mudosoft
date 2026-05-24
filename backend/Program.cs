using Microsoft.EntityFrameworkCore;
using Orchestra.Backend.Data;
using Orchestra.Backend.Models;
using Orchestra.Backend.Services;
using Orchestra.Backend.Crypto;
using Orchestra.Backend.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AspNetCoreRateLimit;
using DotNetEnv;

// 🔧 Load environment variables from .env file
var backendEnvPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
var rootEnvPath = Path.Combine(Directory.GetCurrentDirectory(), "..", ".env");

if (File.Exists(backendEnvPath))
{
    Env.Load(backendEnvPath);
    Console.WriteLine($"✅ Loaded .env from: {backendEnvPath}");
}
else if (File.Exists(rootEnvPath))
{
    Env.Load(rootEnvPath);
    Console.WriteLine($"✅ Loaded .env from: {Path.GetFullPath(rootEnvPath)}");
}
else
{
    Console.WriteLine("⚠️ No .env file found. Using system environment variables.");
}

var builder = WebApplication.CreateBuilder(args);

// 🔧 FIX: Disable EventLog to prevent "Interface unknown" (1717) error on some Windows systems
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// ================== KESTREL LIMITS ==================
// Allow large file uploads (200MB) for agent updates
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 200 * 1024 * 1024; // 200MB
});

// Configure form options for large file uploads
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 200 * 1024 * 1024; // 200MB
});

// ================== RATE LIMITING ==================
// 🔒 SECURITY: API rate limiting to prevent abuse
builder.Services.AddMemoryCache();
builder.Services.Configure<IpRateLimitOptions>(options =>
{
    options.EnableEndpointRateLimiting = true;
    options.StackBlockedRequests = false;
    options.HttpStatusCode = 429;
    options.RealIpHeader = "X-Real-IP";
    options.ClientIdHeader = "X-ClientId";
    options.GeneralRules = new List<RateLimitRule>
    {
        // Genel API limiti: dakikada 400 istek
        new RateLimitRule
        {
            Endpoint = "*",
            Period = "1m",
            Limit = 400
        },
        // Login endpoint: dakikada 10 deneme (brute-force koruması)
        new RateLimitRule
        {
            Endpoint = "*:/api/auth/login",
            Period = "1m",
            Limit = 10
        },
        // Agent auth: dakikada 10 deneme (brute-force koruması)
        new RateLimitRule
        {
            Endpoint = "*:/api/auth/agent-auth",
            Period = "1m",
            Limit = 10
        },
        // SQL query: dakikada 400 istek (çoklu cihaz desteği için)
        new RateLimitRule
        {
            Endpoint = "*:/api/sqlquery/*",
            Period = "1m",
            Limit = 400
        }
    };
});
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();
builder.Services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
builder.Services.AddInMemoryRateLimiting();


// ================== JWT AUTHENTICATION ==================
// 🔒 SECURITY: JWT key MUST be set via environment variable or config
// 🔒 SECURITY: Resolve JWT key — env var takes priority, skip appsettings placeholders
var jwtKeyFromConfig = builder.Configuration["Jwt:Key"];
var jwtKeyFromEnv = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");

// If config value is a placeholder like "${JWT_SECRET_KEY}", ignore it
if (!string.IsNullOrEmpty(jwtKeyFromConfig) && jwtKeyFromConfig.StartsWith("${"))
    jwtKeyFromConfig = null;

var jwtKey = jwtKeyFromEnv ?? jwtKeyFromConfig
    ?? throw new InvalidOperationException("JWT_SECRET_KEY is not configured. Set it in .env or as environment variable.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "Orchestra";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "OrchestraUsers";

// Geçiş dönemi: eski "MudoSoft" issuer/audience ile imzalı agent token'ları (30g ömürlü)
// hâlâ geçerli sayılmalı. Tüm agent'lar yeni token aldıktan sonra eski değerler kaldırılır.
var validIssuers = new[] { jwtIssuer, "MudoSoft", "Orchestra" }.Distinct().ToArray();
var validAudiences = new[] { jwtAudience, "MudoSoftUsers", "MudoSoftAgents", "OrchestraUsers", "OrchestraAgents" }.Distinct().ToArray();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuers = validIssuers,
        ValidAudiences = validAudiences,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };
    
    // SignalR için token desteği
    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

// ================== SERVICES ==================

// 🔒 SECURITY: Strict CORS Policy
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
    ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("MyCorsPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .SetIsOriginAllowed(origin =>
              {
                  // Allow configured origins + local network IPs for agent/admin access
                  if (allowedOrigins.Contains(origin)) return true;
                  if (Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                  {
                      var host = uri.Host;
                      return host == "localhost"
                          || host == "127.0.0.1"
                          || host.StartsWith("10.")
                          || host.StartsWith("192.168.");
                  }
                  return false;
              })
              .WithMethods("GET", "POST", "PUT", "DELETE")
              .WithHeaders("Content-Type", "Authorization", "X-Encrypted", "X-ClientId", "X-Requested-With", "x-signalr-user-agent")
              .AllowCredentials();
    });
});

// DB - Build connection string with environment variable
var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD") 
    ?? throw new InvalidOperationException("DB_PASSWORD environment variable is not set. Please configure it in .env file.");
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?.Replace("${DB_PASSWORD}", dbPassword)
    ?? $"Host=localhost;Port=5432;Database=mudosoft;Username=postgres;Password={dbPassword};";

Console.WriteLine($"🗄️ Database connection configured (password length: {dbPassword.Length} chars)");

builder.Services.AddDbContext<OrchestraDbContext>(options =>
{
    options.UseNpgsql(connectionString);
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ================== CUSTOM SERVICES ==================
// IDeviceRepository Scoped, ancak Worker'lar Factory ile çekecek
builder.Services.AddScoped<IRemoteSqlService, RemoteSqlService>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddSingleton<Orchestra.Backend.Services.EventLogTranslationService>();
builder.Services.AddScoped<EventLogAnalysisService>();
if (OperatingSystem.IsWindows())
    builder.Services.AddScoped<RemoteEventLogPullService>();

// 1. RsaKeyProvider (Gerektiği gibi Scoped)
builder.Services.AddScoped<RsaKeyProvider>(); 

// 2. IAgentService
builder.Services.AddScoped<IAgentService, AgentService>(); 

// 3. FastSqlReachabilityService (Singleton — stateless, worker + controller paylaşır)
builder.Services.AddSingleton<FastSqlReachabilityService>();

// 3.1. Network Diagnostics Service (ag teshis)
builder.Services.AddScoped<NetworkDiagnosticsService>();

// 3.1b. Mobile Line Detection (karasal vs 4.5G tespiti)
builder.Services.AddScoped<MobileLineDetectionService>();

// 3.2. InboxCleanupService
builder.Services.AddScoped<IInboxCleanupService, InboxCleanupService>();

// 3.2.1. StockCleanupService (POS_STOCK_TRANSFER conditional truncate — scheduler için)
builder.Services.AddScoped<IStockCleanupService, StockCleanupService>();

// 3.3. Inventory Import (SDP envanter modulu)
builder.Services.AddScoped<Orchestra.Backend.Services.InventoryImportService>();

// 3.4. Activity Log (audit)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Orchestra.Backend.Services.ActivityLogService>();

// 4. CommandQueue
builder.Services.AddSingleton<CommandQueue>();

// 4.0.1. Batch Execution (acil bat çalıştırma — agent'lı + agent'siz)
builder.Services.AddSingleton<Orchestra.Backend.Services.BatchExecutionService>();

// 4.1. VNC Session Manager (RDP proxy)
builder.Services.AddSingleton<VncSessionManager>();

// 5. AES Encryption (Middleware için kritik)
builder.Services.AddScoped<AesEncryption>();


// 6. Email Service (Alarm bildirim)
builder.Services.AddSingleton<IEmailService, EmailService>();

// 6b. LDAP authentication (opsiyonel, appsettings:Ldap:Enabled ile aç/kapa)
builder.Services.AddSingleton<ILdapAuthService, LdapAuthService>();
builder.Services.AddSingleton<ILdapDirectoryService, LdapDirectoryService>();

// 7. Worker'lar (Singleton/HostedService olarak doğru şekilde kaydedildi)
builder.Services.AddHostedService<Orchestra.Backend.Services.HeartbeatCheckerWorker>();
builder.Services.AddHostedService<Orchestra.Backend.Services.NetworkOutageAlarmWorker>();
builder.Services.AddHostedService<Orchestra.Backend.Services.CriticalServiceMonitorWorker>();
builder.Services.AddHostedService<Orchestra.Backend.Services.SchedulerBackgroundService>();
builder.Services.AddHostedService<Orchestra.Backend.Services.ScheduledTaskSeeder>();
builder.Services.AddHostedService<Orchestra.Backend.Services.DeviceStatusWorker>();
builder.Services.AddHostedService<Orchestra.Backend.Services.RouterLatencyPurgeWorker>();
builder.Services.AddSingleton<Orchestra.Backend.Services.SerialNumberSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Orchestra.Backend.Services.SerialNumberSyncService>());
builder.Services.AddSingleton<Orchestra.Backend.Services.PrinterSerialSyncService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Orchestra.Backend.Services.PrinterSerialSyncService>());
builder.Services.AddHostedService<Orchestra.Backend.Services.TelemetryRetentionWorker>();
if (OperatingSystem.IsWindows())
    builder.Services.AddHostedService<Orchestra.Backend.Services.UserInstallWatcherService>();
builder.Services.AddHttpClient("internal", c =>
{
    c.Timeout = TimeSpan.FromSeconds(120);
});
//builder.Services.AddHostedService<Orchestra.Backend.Services.DiscoveryWorker>();

// 7. SignalR
builder.Services.AddSignalR(hubOptions =>
{
    hubOptions.MaximumReceiveMessageSize = 5 * 1024 * 1024; // 5MB Limit
    hubOptions.EnableDetailedErrors = true;
}); 

// =====================================================

var app = builder.Build();

// ================== PIPELINE ==================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // 🔒 SECURITY: Force HTTPS in production
    app.UseHttpsRedirection();
    app.UseHsts();
}

// 🔒 SECURITY: Rate limiting (before routing)
app.UseIpRateLimiting();

app.UseRouting();
app.UseCors("MyCorsPolicy");

// Security Headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});

// Şifre çözme işlemi, yetkilendirme ve kontrolörlere ulaşmadan önce burada gerçekleşir
app.UseMiddleware<EncryptedPayloadMiddleware>(); 

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// WebSocket support for VNC proxy
app.UseWebSockets();
app.UseVncWebSocket("/ws/vnc");

app.MapControllers();
// Hub Mapping
app.MapHub<Orchestra.Backend.Hubs.DashboardHub>("/hubs/dashboard");

// ================== SEED (SADECE DEVELOPMENT) ==================
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<OrchestraDbContext>();
    db.Database.Migrate();

    // 🔒 SECURITY: Get DB credentials from environment
    var geniusDbUser = Environment.GetEnvironmentVariable("GENIUS_DB_USER") ?? "GENIUS3";
    var geniusDbPass = Environment.GetEnvironmentVariable("GENIUS_DB_PASSWORD");
    
    if (string.IsNullOrEmpty(geniusDbPass))
    {
        Console.WriteLine("⚠️ WARNING: GENIUS_DB_PASSWORD environment variable is not set. Skipping device seeding.");
    }
    else
    {
        // Helper function to build connection string securely
        string BuildConnectionString(string ip) => 
            $"Server={ip};Database=Genius3;User Id={geniusDbUser};Password={geniusDbPass};TrustServerCertificate=True;Connect Timeout=30;";

        // Cihaz yoksa Ekle
        if (!db.StoreDevices.Any())
        {
            var stores = new List<(int Code, string Name)>
            {
                (5,"Nişantaşı Giyim"),(7,"Mersin Forum Giyim"),(8,"Bahariye Giyim"), (9,"Bağdat Giyim"),(11,"Viaport Giyim"),(16,"Ankara Cepa City"), (17,"Ereğli Ereylin Giyim"),(19,"Marmaris Netsel Marina"), (22,"Denizli Forum Giyim"),(23,"Göztepe Optimum Giyim"), (24,"Bodrum Gürece Concept"),(26,"Capitol Giyim"), (29,"Bursa Korupark Giyim"),(32,"Trabzon Forum City"), (33,"Airport Outlet"),(38,"Bodrum Milta Marina"), (39,"Ankara Ankamall Giyim"),(40,"Antalya Agora Giyim"), (43,"Marmaris Solaris City"),(51,"İzmir Agora City"), (52,"Nautilus Home"),(55,"Ankara Arcadium City"), (56,"Bodrum Turgutreis Marina"),(57,"Bodrum Yalıkavak Marina"), (58,"Maltepe Park Giyim"),(59,"Ankara Optimum Outlet"), (60,"Antalya Deepo Giyim"),(64,"Adana M1 City"), (68,"Kayseri Park City"),(74,"Nişantaşı Concept"), (76,"Susurluk Festiva Giyim"),(80,"Eskişehir Vega City"), (86,"İzmir Forum Giyim"),(88,"İzmir Selway Outlet"), (89,"İzmir Alsancak Concept"),(91,"Samsun Yeşilyurt Giyim"), (93,"Gebze Fırsat"),(97,"Alanya City"),(98,"Ankara Panora Concept"), (100,"Adana Concept"),(102,"Tekirdağ Tekira City"), (104,"Adana 01 Burda Giyim"),(107,"Cevahir Giyim"), (110,"Çeşme Marina"),(113,"Ankara Next Level Concept"), (114,"Antalya Rixos Marina"),(116,"Çorum Ahlpark Giyim"), (117,"212 Outlet"),(118,"Malatya Park Giyim"), (120,"K.maraş Piazza Giyim"),(121,"Ankara Gordion City"), (122,"Pendik Marina Kadın"),(124,"Kozzy City"), (125,"Gebze Center City"),(129,"Mersin Yat Limanı Marina"), (130,"Yalova Setur Marina"),(132,"Edirne Margi City"), (136,"İzmir Arastapark City"),(137,"Akçay Yasa Giyim"), (138,"Akbatı City"),(139,"Pendik Marina Home"), (140,"Bandırma Liman Giyim"),(141,"İzmir Optimum Giyim"), (142,"İzmir Hilltown Giyim"),(143,"Arenapark Giyim"), (146,"Maslak Concept"),(147,"Balıkesir Burda Giyim"), (148,"Capitol Home"),(151,"Aqua Florya Home"), (152,"Buyaka City"),(153,"Antalya Aspendos Concept"), (155,"İskenderun Forbes Marina"),(157,"Bodrum Midtown City"), (158,"Kuşadası Setur Marina"),(159,"Nautilus Giyim"), (161,"Ankara Kentpark Home"),(163,"Emaar City"), (164,"Pendik Marina Erkek"),(167,"Alanya Marina"), (172,"Göcek Marina Kadın"),(173,"Samsun Piazza Giyim"), (174,"Fethiye Marina Yeni"),(175,"Bodrum Avenue Giyim"), (176,"İst. İstinyePark Giyim"),(177,"Ümraniye Meydan Giyim"), (180,"Brandium Fırsat Pop Up"),(181,"Aqua Florya Giyim"), (182,"Mecidiyeköy Outlet"),(183,"Vadistanbul City"), (186,"İçerenköy City's Home"),(187,"Balat Marina"), (190,"Akmerkez Giyim"),(191,"Akmerkez Home"), (193,"Bodrum Anthaven Marina"),(195,"Akasya Giyim"), (196,"Masko Concept"),(201,"Modoko Concept"), (202,"Mall of İstanbul Concept"),(205,"Palladium Concept"), (206,"Maltepe Piazza Giyim"),(208,"İzmir MaviBahçe Concept"), (209,"İzmit Burda Home"),(210,"Tuzla Viaport Marina"), (211,"Adapazarı Agora Giyim"),(213,"Çanakkale Burda Giyim"), (214,"Çanakkale Burda Home"),(216,"Bodrum Plaza Concept"), (217,"Göcek Marina Erkek"),(218,"Fethiye Marina"), (219,"Mersin Concept"),(221,"Antalya Lara Concept"), (222,"Ayvalık Marina"),(223,"Modoko Concept 2"), (224,"İstmarin City"),(227,"Bursa Anatolium Giyim"), (235,"İzmit Burda Giyim"),(238,"Gaziantep Sanko Giyim"), (239,"Carousel Giyim"),(243,"Urla Marina"), (245,"İçerenköy City's Giyim"),(246,"Büyükada Marina"), (247,"Nişantaşı City's Giyim"),(248,"İzmir İstinyePark Giyim"), (249,"Göztepe Optimum C.Outlet"),(251,"Bursa FSM Concept"), (252,"Bursa Downtown Giyim"),(254,"Tema World Giyim"), (257,"Kaş Marina"),(258,"Datça Marina")
            };

            foreach (var s in stores)
            {
                string pcIp = $"192.168.{s.Code}.2";
                string k1 = $"192.168.{s.Code}.31";
                string k2 = $"192.168.{s.Code}.32";
                string k3 = $"192.168.{s.Code}.33";

                if (s.Code == 93)
                {
                    pcIp = "192.168.125.237";
                    k1 = "192.168.125.235";
                }

                if (s.Code == 257)
                {
                    pcIp = "192.168.50.2";
                    k1 = "192.168.50.31";
                    k2 = "192.168.50.32";
                }

                if (s.Code == 258)
                {
                    pcIp = "192.168.240.2";
                    k1 = "192.168.240.31";
                    k2 = "192.168.240.32";
                    k3 = "192.168.240.33";
                }

                db.StoreDevices.AddRange(
                    new StoreDevice 
                    { 
                        DeviceId=$"{s.Code}-PC", 
                        StoreCode=s.Code, 
                        StoreName=s.Name, 
                        DeviceType="PC", 
                        DeviceName=$"{s.Code}-PC", 
                        CalculatedIpAddress=pcIp,
                        DbConnectionString = BuildConnectionString(pcIp)
                    },
                    new StoreDevice 
                    { 
                        DeviceId=$"{s.Code}-K1", 
                        StoreCode=s.Code, 
                        StoreName=s.Name, 
                        DeviceType="Kasa-1", 
                        DeviceName=$"{s.Code}-Kasa-1", 
                        CalculatedIpAddress=k1,
                        DbConnectionString = BuildConnectionString(k1)
                    },
                    new StoreDevice 
                    { 
                        DeviceId=$"{s.Code}-K2", 
                        StoreCode=s.Code, 
                        StoreName=s.Name, 
                        DeviceType="Kasa-2", 
                        DeviceName=$"{s.Code}-Kasa-2", 
                        CalculatedIpAddress=k2,
                        DbConnectionString = BuildConnectionString(k2)
                    },
                    new StoreDevice 
                    { 
                        DeviceId=$"{s.Code}-K3", 
                        StoreCode=s.Code, 
                        StoreName=s.Name, 
                        DeviceType="Kasa-3", 
                        DeviceName=$"{s.Code}-Kasa-3", 
                        CalculatedIpAddress=k3,
                        DbConnectionString = BuildConnectionString(k3)
                    }
                );
            }
            db.SaveChanges();
        }
        
        // FIX: Var olan kayıtların ConnectionString'i yanlışsa (sa user) veya boşsa düzelt
        var wrongDevices = db.StoreDevices.Where(d => d.DbConnectionString == "" || d.DbConnectionString.Contains("User Id=sa")).ToList();
        if (wrongDevices.Any())
        {
            foreach (var d in wrongDevices)
            {
                d.DbConnectionString = BuildConnectionString(d.CalculatedIpAddress);
            }
            db.SaveChanges();
        }

        // =====================================================
        // GEÇİCİ PC EKLEME
        // =====================================================
        var temporaryDevices = new List<(string Name, string Ip, int StoreCode, string StoreName)>
        {
            ("GE-PC-1", "10.0.102.40", 102, "Tekirdağ Tekira City"),
            ("GE-PC-2", "10.0.102.45", 102, "Tekirdağ Tekira City"),
            ("GE-PC-3", "10.0.210.131", 210, "Tuzla Viaport Marina"),
            ("GE-PC-4", "10.0.210.132", 210, "Tuzla Viaport Marina"),
            ("GE-PC-5", "10.0.210.133", 210, "Tuzla Viaport Marina"),
            ("GE-PC-6", "10.0.102.35", 102, "Tekirdağ Tekira City"),
            ("GE-PC-7", "10.0.102.50", 102, "Tekirdağ Tekira City")
        };

        foreach (var td in temporaryDevices)
        {
            var expectedId = $"GECICI-{td.Ip}";
            var existing = db.StoreDevices.FirstOrDefault(d => d.DeviceId == expectedId);
            if (existing == null)
            {
                // Aynı IP ile farklı ID'li kayıt varsa önce sil
                var byIp = db.StoreDevices.FirstOrDefault(d => d.CalculatedIpAddress == td.Ip);
                if (byIp != null)
                    db.StoreDevices.Remove(byIp);

                db.StoreDevices.Add(new StoreDevice
                {
                    DeviceId = expectedId,
                    StoreCode = td.StoreCode,
                    StoreName = td.StoreName,
                    DeviceType = "GECICI",
                    DeviceName = td.Name,
                    CalculatedIpAddress = td.Ip,
                    DbConnectionString = BuildConnectionString(td.Ip)
                });
            }
            else
            {
                // Mevcut kaydı güncelle
                existing.DeviceType = "GECICI";
                existing.DeviceName = td.Name;
                existing.DbConnectionString = BuildConnectionString(td.Ip);
            }
        }
        db.SaveChanges();

        // =====================================================
        // KAŞ MARINA KASA-2 EKLEMESİ
        // =====================================================
        if (!db.StoreDevices.Any(d => d.DeviceId == "257-K2"))
        {
            db.StoreDevices.Add(new StoreDevice
            {
                DeviceId = "257-K2",
                StoreCode = 257,
                StoreName = "Kaş Marina",
                DeviceType = "Kasa-2",
                DeviceName = "257-Kasa-2",
                CalculatedIpAddress = "192.168.50.32",
                DbConnectionString = BuildConnectionString("192.168.50.32")
            });
            db.SaveChanges();
        }

        // =====================================================
        // MERSİN YAT LİMANI KASA-2 EKLEMESİ
        // =====================================================
        if (!db.StoreDevices.Any(d => d.DeviceId == "129-K2"))
        {
            db.StoreDevices.Add(new StoreDevice
            {
                DeviceId = "129-K2",
                StoreCode = 129,
                StoreName = "Mersin Yat Limanı Marina",
                DeviceType = "Kasa-2",
                DeviceName = "129-Kasa-2",
                CalculatedIpAddress = "192.168.129.32",
                DbConnectionString = BuildConnectionString("192.168.129.32")
            });
            db.SaveChanges();
        }

        // =====================================================
        // MAĞAZA 139 ve 191 KASA-2 EKLEMESİ
        // =====================================================
        // 139-K2 kaldırıldı - Mağaza 139 (Pendik Marina Home) sadece Kasa-1 kullanıyor

        if (!db.StoreDevices.Any(d => d.DeviceId == "191-K2"))
        {
            db.StoreDevices.Add(new StoreDevice
            {
                DeviceId = "191-K2",
                StoreCode = 191,
                StoreName = "Mağaza 191",
                DeviceType = "Kasa-2",
                DeviceName = "191-Kasa-2",
                CalculatedIpAddress = "192.168.191.32",
                DbConnectionString = BuildConnectionString("192.168.191.32")
            });
            db.SaveChanges();
        }
        // =====================================================
        // DATÇA MARİNA (258) EKLEMESİ — IP bloğu: 192.168.240.x
        // =====================================================
        if (!db.StoreDevices.Any(d => d.DeviceId == "258-PC"))
        {
            db.StoreDevices.AddRange(
                new StoreDevice { DeviceId = "258-PC", StoreCode = 258, StoreName = "Datça Marina", DeviceType = "PC", DeviceName = "258-PC", CalculatedIpAddress = "192.168.240.2", DbConnectionString = BuildConnectionString("192.168.240.2") },
                new StoreDevice { DeviceId = "258-K1", StoreCode = 258, StoreName = "Datça Marina", DeviceType = "Kasa-1", DeviceName = "258-Kasa-1", CalculatedIpAddress = "192.168.240.31", DbConnectionString = BuildConnectionString("192.168.240.31") },
                new StoreDevice { DeviceId = "258-K2", StoreCode = 258, StoreName = "Datça Marina", DeviceType = "Kasa-2", DeviceName = "258-Kasa-2", CalculatedIpAddress = "192.168.240.32", DbConnectionString = BuildConnectionString("192.168.240.32") },
                new StoreDevice { DeviceId = "258-K3", StoreCode = 258, StoreName = "Datça Marina", DeviceType = "Kasa-3", DeviceName = "258-Kasa-3", CalculatedIpAddress = "192.168.240.33", DbConnectionString = BuildConnectionString("192.168.240.33") }
            );
            db.SaveChanges();
            Console.WriteLine("✅ Store 258 (Datça Marina) eklendi — IP: 192.168.240.x");
        }
        // Router kaydı (ayrı ekleme — migration'dan sonra gelen mağazalar için)
        if (!db.StoreDevices.Any(d => d.DeviceId == "258-Router"))
        {
            db.StoreDevices.Add(new StoreDevice { DeviceId = "258-Router", StoreCode = 258, StoreName = "Datça Marina", DeviceType = "Router", DeviceName = "258-Router", CalculatedIpAddress = "192.168.240.1", DbConnectionString = "" });
            db.SaveChanges();
        }

        // =====================================================
        // MAĞAZA 196 IP GÜNCELLEMESİ (192.168.196.2 -> 192.168.196.5)
        // =====================================================
        var store196Pc = db.StoreDevices.FirstOrDefault(d => d.StoreCode == 196 && d.DeviceType == "PC");
        if (store196Pc != null && store196Pc.CalculatedIpAddress != "192.168.196.5")
        {
            store196Pc.CalculatedIpAddress = "192.168.196.5";
            store196Pc.DbConnectionString = BuildConnectionString("192.168.196.5");
            db.SaveChanges();
            Console.WriteLine("✅ Store 196 IP updated to 192.168.196.5");
        }

        db.SaveChanges();
    }

    // =====================================================
    // STORE NETWORK INFO SEED — Turkcell karasal hat Mbps degerleri
    // Eksik olanlar eklenir, mevcutlar kullanici manuel duzenlemis olabilir diye dokunulmaz.
    // =====================================================
    var mbpsData = new (int StoreCode, int Mbps)[]
    {
        (5, 8), (76, 8), (102, 8), (120, 8), (176, 8), (245, 8), (253, 8),
        (205, 10), (254, 10),
        (89, 15), (202, 15), (251, 15), (252, 15),
        (3, 16), (7, 16), (8, 16), (11, 16), (17, 16), (19, 16), (22, 16), (23, 16),
        (24, 16), (26, 16), (29, 16), (32, 16), (33, 16), (38, 16), (39, 16), (43, 16),
        (52, 16), (56, 16), (57, 16), (58, 16), (59, 16), (64, 16), (68, 16), (74, 16),
        (86, 16), (88, 16), (97, 16), (98, 16), (107, 16), (110, 16), (117, 16), (118, 16),
        (121, 16), (124, 16), (125, 16), (129, 16), (130, 16), (132, 16), (136, 16),
        (137, 16), (139, 16), (140, 16), (141, 16), (143, 16), (148, 16), (151, 16),
        (157, 16), (158, 16), (159, 16), (162, 16), (167, 16), (172, 16), (175, 16),
        (177, 16), (190, 16), (191, 16), (196, 16), (201, 16), (213, 16), (214, 16),
        (216, 16), (217, 16), (218, 16), (221, 16), (222, 16), (227, 16), (238, 16),
        (239, 16), (243, 16), (246, 16), (249, 16),
        (40, 24), (60, 24), (112, 24), (114, 24), (116, 24), (146, 24), (155, 24),
        (182, 24), (208, 24),
        (9, 35), (16, 35), (51, 35), (55, 35), (80, 35), (91, 35), (100, 35), (104, 35),
        (113, 35), (138, 35), (142, 35), (147, 35), (152, 35), (163, 35), (164, 35),
        (173, 35), (174, 35), (181, 35), (183, 35), (193, 35), (195, 35), (206, 35),
        (209, 35), (210, 35), (211, 35), (219, 35), (224, 35), (235, 35), (248, 35),
        (122, 35), (161, 35), (187, 35), (223, 35), (257, 35)
    };

    var existingCodes = db.StoreNetworkInfos.Select(s => s.StoreCode).ToHashSet();
    var nowUtc = DateTime.UtcNow;
    var toAdd = new List<StoreNetworkInfo>();
    foreach (var (code, mbps) in mbpsData)
    {
        if (existingCodes.Contains(code)) continue;
        toAdd.Add(new StoreNetworkInfo
        {
            StoreCode = code,
            TerrestrialMbps = mbps,
            UpdatedAt = nowUtc
        });
    }
    if (toAdd.Count > 0)
    {
        db.StoreNetworkInfos.AddRange(toAdd);
        db.SaveChanges();
        Console.WriteLine($"[SEED] StoreNetworkInfos: {toAdd.Count} magaza Mbps kaydi eklendi");
    }

    // Seed default Store Opening checklist template
    await Orchestra.Backend.Services.StoreOpeningTemplateSeeder.SeedAsync(db);

    // Seed default admin user if no users exist
    if (!db.Users.Any())
    {
        var adminUsername = Environment.GetEnvironmentVariable("ADMIN_USERNAME") ?? "admin";
        var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "admin";
        if (adminUsername.StartsWith("${")) adminUsername = "admin";
        if (adminPassword.StartsWith("${")) adminPassword = "admin";

        db.Users.Add(new Orchestra.Backend.Models.User
        {
            Username = adminUsername.ToLower(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            Role = "Admin",
            FullName = "Administrator",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        db.SaveChanges();
        Console.WriteLine($"✅ Default admin user created: {adminUsername}");
    }
}

app.Run();
