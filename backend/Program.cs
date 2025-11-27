using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Middleware;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization; // ⬅️ YENİ USING DİREKTİFİ

var builder = WebApplication.CreateBuilder(args);

// 🔥 DbContext Register (ZORUNLU!)
builder.Services.AddDbContext<MudoSoftDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Services
// 🏆 GÜNCELLENDİ: JSON döngüsel referans hatasını engellemek için ayar eklendi.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddSingleton<CommandQueue>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddSingleton<RsaKeyProvider>();
builder.Services.AddSingleton<AesEncryption>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();

// CORS for Vite frontend
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

app.UseCors("AllowMudoSoftFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    // app.UseMiddleware<EncryptedPayloadMiddleware>(); // Yorum satırına alındı (aşağıdaki tek çağrı yeterli)
}

app.UseMiddleware<EncryptedPayloadMiddleware>();
app.MapControllers();
app.Run();