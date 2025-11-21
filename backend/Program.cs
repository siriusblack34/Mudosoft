using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Middleware;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 🔥 DbContext Register (ZORUNLU!)
builder.Services.AddDbContext<MudoSoftDbContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// Services
builder.Services.AddControllers();
builder.Services.AddSingleton<CommandQueue>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddSingleton<RsaKeyProvider>();
builder.Services.AddSingleton<AesEncryption>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
    app.UseMiddleware<EncryptedPayloadMiddleware>();
}

app.UseMiddleware<EncryptedPayloadMiddleware>();
app.MapControllers();
app.Run();
