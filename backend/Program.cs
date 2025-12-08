using MudoSoft.Backend;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Middleware;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;

// TLS
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

var builder = WebApplication.CreateBuilder(args);

// ===============================================
// DATABASE
// ===============================================
builder.Services.AddDbContext<MudoSoftDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// ===============================================
// JSON SETTINGS
// ===============================================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new TimeZoneConverter());
    });

// ===============================================
// DEPENDENCY INJECTION
// ===============================================
builder.Services.AddSingleton<CommandQueue>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddSingleton<RsaKeyProvider>();
builder.Services.AddSingleton<AesEncryption>();
builder.Services.AddSingleton<FastSqlReachabilityService>();
builder.Services.AddSingleton<ConnectionDiagnosticService>();   // ⭐ EKLENDİ
builder.Services.AddScoped<IStoreDiscoveryService, StoreDiscoveryService>();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IRemoteSqlService, RemoteSqlService>();
builder.Services.AddHostedService<HeartbeatCheckerWorker>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ===============================================
// CORS (VITE)
// ===============================================
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowMudoSoftFrontend", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// ===============================================
// MIGRATIONS + STORE CSV AUTO SYNC
// ===============================================
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<MudoSoftDbContext>();
    var discoveryService = services.GetRequiredService<IStoreDiscoveryService>();
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        await dbContext.Database.MigrateAsync();
        logger.LogInformation("Database migration'lar başarıyla uygulandı.");

        // HARDCODE STORE CSV (kısalttım)
        string csvContent = @"Mağaza Kodu,Mağaza Adı
5,Nişantaşı Giyim
...
257,Kaş Marina";

        await discoveryService.SyncStoreDevicesFromCsvAsync(csvContent);
        logger.LogInformation("Mağaza cihazları başarıyla senkronize edildi.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Migration / Store Sync sırasında hata oluştu.");
    }
}

// ===============================================
// PIPELINE
// ===============================================
app.UseCors("AllowMudoSoftFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<EncryptedPayloadMiddleware>();
app.MapControllers();
app.Urls.Add("http://0.0.0.0:5102");

// RUN
app.Run();
