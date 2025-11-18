using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Services
builder.Services.AddControllers();
builder.Services.AddSingleton<CommandQueue>();
builder.Services.AddSingleton<IAgentService, AgentService>();
builder.Services.AddSingleton<RsaKeyProvider>();

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
}
app.UseMiddleware<EncryptedPayloadMiddleware>();
app.MapControllers();
app.Run();

