using Microsoft.EntityFrameworkCore;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Models;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ================== JWT AUTHENTICATION ==================
var jwtKey = builder.Configuration["Jwt:Key"] ?? "***REMOVED***";
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MudoSoft";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MudoSoftUsers";

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
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero // Token expiry anında geçersiz olsun
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

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("MyCorsPolicy", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // SignalR için ZORUNLU
    });
});

// DB
builder.Services.AddDbContext<MudoSoftDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ================== CUSTOM SERVICES ==================
// IDeviceRepository Scoped, ancak Worker'lar Factory ile çekecek
builder.Services.AddScoped<IRemoteSqlService, RemoteSqlService>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>(); 

// 1. RsaKeyProvider (Gerektiği gibi Scoped)
builder.Services.AddScoped<RsaKeyProvider>(); 

// 2. IAgentService
builder.Services.AddScoped<IAgentService, AgentService>(); 

// 3. FastSqlReachabilityService
builder.Services.AddScoped<FastSqlReachabilityService>();

// 4. CommandQueue
builder.Services.AddSingleton<CommandQueue>();

// 5. AES Encryption (Middleware için kritik)
builder.Services.AddScoped<AesEncryption>();


// 6. Worker'lar (Singleton/HostedService olarak doğru şekilde kaydedildi)
builder.Services.AddHostedService<MudoSoft.Backend.Services.HeartbeatCheckerWorker>();
//builder.Services.AddHostedService<MudoSoft.Backend.Services.DiscoveryWorker>();

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
app.MapControllers();
// Hub Mapping
app.MapHub<MudoSoft.Backend.Hubs.RemoteDesktopHub>("/hubs/desktop");

// ================== SEED (SADECE DEVELOPMENT) ==================
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<MudoSoftDbContext>();
    db.Database.Migrate();

    // Cihaz yoksa Ekle
    if (!db.StoreDevices.Any())
    {
        var stores = new List<(int Code, string Name)>
        {
            (5,"Nişantaşı Giyim"),(7,"Mersin Forum Giyim"),(8,"Bahariye Giyim"), (9,"Bağdat Giyim"),(11,"Viaport Giyim"),(16,"Ankara Cepa City"), (17,"Ereğli Ereylin Giyim"),(19,"Marmaris Netsel Marina"), (22,"Denizli Forum Giyim"),(23,"Göztepe Optimum Giyim"), (24,"Bodrum Gürece Concept"),(26,"Capitol Giyim"), (29,"Bursa Korupark Giyim"),(32,"Trabzon Forum City"), (33,"Airport Outlet"),(38,"Bodrum Milta Marina"), (39,"Ankara Ankamall Giyim"),(40,"Antalya Agora Giyim"), (43,"Marmaris Solaris City"),(51,"İzmir Agora City"), (52,"Nautilus Home"),(55,"Ankara Arcadium City"), (56,"Bodrum Turgutreis Marina"),(57,"Bodrum Yalıkavak Marina"), (58,"Maltepe Park Giyim"),(59,"Ankara Optimum Outlet"), (60,"Antalya Deepo Giyim"),(64,"Adana M1 City"), (68,"Kayseri Park City"),(74,"Nişantaşı Concept"), (76,"Susurluk Festiva Giyim"),(80,"Eskişehir Vega City"), (86,"İzmir Forum Giyim"),(88,"İzmir Selway Outlet"), (89,"İzmir Alsancak Concept"),(91,"Samsun Yeşilyurt Giyim"), (93,"Gebze Fırsat"),(97,"Alanya City"),(98,"Ankara Panora Concept"), (100,"Adana Concept"),(102,"Tekirdağ Tekira City"), (104,"Adana 01 Burda Giyim"),(107,"Cevahir Giyim"), (110,"Çeşme Marina"),(113,"Ankara Next Level Concept"), (114,"Antalya Rixos Marina"),(116,"Çorum Ahlpark Giyim"), (117,"212 Outlet"),(118,"Malatya Park Giyim"), (120,"K.maraş Piazza Giyim"),(121,"Ankara Gordion City"), (122,"Pendik Marina Kadın"),(124,"Kozzy City"), (125,"Gebze Center City"),(129,"Mersin Yat Limanı Marina"), (130,"Yalova Setur Marina"),(132,"Edirne Margi City"), (136,"İzmir Arastapark City"),(137,"Akçay Yasa Giyim"), (138,"Akbatı City"),(139,"Akbatı City"), (140,"Bandırma Liman Giyim"),(141,"İzmir Optimum Giyim"), (142,"İzmir Hilltown Giyim"),(143,"Arenapark Giyim"), (146,"Maslak Concept"),(147,"Balıkesir Burda Giyim"), (148,"Capitol Home"),(151,"Aqua Florya Home"), (152,"Buyaka City"),(153,"Antalya Aspendos Concept"), (155,"İskenderun Forbes Marina"),(157,"Bodrum Midtown City"), (158,"Kuşadası Setur Marina"),(159,"Nautilus Giyim"), (161,"Ankara Kentpark Home"),(163,"Emaar City"), (164,"Pendik Marina Erkek"),(167,"Alanya Marina"), (172,"Göcek Marina Kadın"),(173,"Samsun Piazza Giyim"), (174,"Fethiye Marina Yeni"),(175,"Bodrum Avenue Giyim"), (176,"İst. İstinyePark Giyim"),(177,"Ümraniye Meydan Giyim"), (180,"Brandium Fırsat Pop Up"),(181,"Aqua Florya Giyim"), (182,"Mecidiyeköy Outlet"),(183,"Vadistanbul City"), (186,"İçerenköy City's Home"),(187,"Balat Marina"), (190,"Akmerkez Giyim"),(191,"Akmerkez Home"), (193,"Bodrum Anthaven Marina"),(195,"Akasya Giyim"), (196,"Masko Concept"),(201,"Modoko Concept"), (202,"Mall of İstanbul Concept"),(205,"Palladium Concept"), (206,"Maltepe Piazza Giyim"),(208,"İzmir MaviBahçe Concept"), (209,"İzmit Burda Home"),(210,"Tuzla Viaport Marina"), (211,"Adapazarı Agora Giyim"),(213,"Çanakkale Burda Giyim"), (214,"Çanakkale Burda Home"),(216,"Bodrum Plaza Concept"), (217,"Göcek Marina Erkek"),(218,"Fethiye Marina"), (219,"Mersin Concept"),(221,"Antalya Lara Concept"), (222,"Ayvalık Marina"),(223,"Modoko Concept 2"), (224,"İstmarin City"),(227,"Bursa Anatolium Giyim"), (235,"İzmit Burda Giyim"),(238,"Gaziantep Sanko Giyim"), (239,"Carousel Giyim"),(243,"Urla Marina"), (245,"İçerenköy City's Giyim"),(246,"Büyükada Marina"), (247,"Nişantaşı City's Giyim"),(248,"İzmir İstinyePark Giyim"), (249,"Göztepe Optimum C.Outlet"),(251,"Bursa FSM Concept"), (252,"Bursa Downtown Giyim"),(254,"Tema World Giyim"), (257,"Kaş Marina")
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

            db.StoreDevices.AddRange(
                new StoreDevice 
                { 
                    DeviceId=$"{s.Code}-PC", 
                    StoreCode=s.Code, 
                    StoreName=s.Name, 
                    DeviceType="PC", 
                    DeviceName=$"{s.Code}-PC", 
                    CalculatedIpAddress=pcIp,
                    DbConnectionString = $"Server={pcIp};Database=Genius3;User Id=GENIUS3;Password=***REMOVED***;TrustServerCertificate=True;Connect Timeout=30;"
                },
                new StoreDevice 
                { 
                    DeviceId=$"{s.Code}-K1", 
                    StoreCode=s.Code, 
                    StoreName=s.Name, 
                    DeviceType="Kasa-1", 
                    DeviceName=$"{s.Code}-Kasa-1", 
                    CalculatedIpAddress=k1,
                    DbConnectionString = $"Server={k1};Database=Genius3;User Id=GENIUS3;Password=***REMOVED***;TrustServerCertificate=True;Connect Timeout=30;"
                },
                new StoreDevice 
                { 
                    DeviceId=$"{s.Code}-K2", 
                    StoreCode=s.Code, 
                    StoreName=s.Name, 
                    DeviceType="Kasa-2", 
                    DeviceName=$"{s.Code}-Kasa-2", 
                    CalculatedIpAddress=k2,
                    DbConnectionString = $"Server={k2};Database=Genius3;User Id=GENIUS3;Password=***REMOVED***;TrustServerCertificate=True;Connect Timeout=30;"
                },
                new StoreDevice 
                { 
                    DeviceId=$"{s.Code}-K3", 
                    StoreCode=s.Code, 
                    StoreName=s.Name, 
                    DeviceType="Kasa-3", 
                    DeviceName=$"{s.Code}-Kasa-3", 
                    CalculatedIpAddress=k3,
                    DbConnectionString = $"Server={k3};Database=Genius3;User Id=GENIUS3;Password=***REMOVED***;TrustServerCertificate=True;Connect Timeout=30;"
                }
            );
        }
        db.SaveChanges();
    }
    
    // FIX AÇIKLAMASI: Var olan kayıtların ConnectionString'i yanlışsa (sa user) veya boşsa düzelt
    var wrongDevices = db.StoreDevices.Where(d => d.DbConnectionString == "" || d.DbConnectionString.Contains("User Id=sa")).ToList();
    if (wrongDevices.Any())
    {
        foreach (var d in wrongDevices)
        {
            d.DbConnectionString = $"Server={d.CalculatedIpAddress};Database=Genius3;User Id=GENIUS3;Password=***REMOVED***;TrustServerCertificate=True;Connect Timeout=30;";
        }
        db.SaveChanges();
    }
}

app.Run();