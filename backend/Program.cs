using MudoSoft.Backend;
using MudoSoft.Backend.Services;
using MudoSoft.Backend.Data;
using MudoSoft.Backend.Crypto;
using MudoSoft.Backend.Middleware;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json.Serialization;

// ❗ YANLIŞ Using Silindi:
// using Npgsql.EntityFrameworkCore.PostgreSQL;  // ❌ BUNU KULLANMA!

// ⭐ TLS Politikası
ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

var builder = WebApplication.CreateBuilder(args);

// ===========================
// 🔥 PostgreSQL DATABASE
// ===========================
builder.Services.AddDbContext<MudoSoftDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// ===========================
// 🔥 CONTROLLERS + JSON OPTIONS
// ===========================
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.Converters.Add(new TimeZoneConverter());
    });

// ===========================
// 🔥 SERVICE DI REGISTER
// ===========================
builder.Services.AddSingleton<CommandQueue>();
builder.Services.AddScoped<IAgentService, AgentService>();
builder.Services.AddSingleton<RsaKeyProvider>();
builder.Services.AddSingleton<AesEncryption>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddHostedService<HeartbeatCheckerWorker>();
builder.Services.AddScoped<IStoreDiscoveryService, StoreDiscoveryService>();
builder.Services.AddScoped<IRemoteSqlService, RemoteSqlService>();

// ===========================
// 🔥 CORS (VITE)
// ===========================
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

// ======================================
// 🔥 MIGRATIONS + STORE CSV AUTO SYNC
// ======================================
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

        // CSV HARDCODE STORE LIST
        string csvContent = @"Mağaza Kodu,Mağaza Adı
5,Nişantaşı Giyim
7,Mersin Forum Giyim
8,Bahariye Giyim
9,Bağdat Giyim
11,Viaport Giyim
16,Ankara Cepa City
17,Ereğli Ereylin Giyim
19,Marmaris Netsel Marina
22,Denizli Forum Giyim
23,Göztepe Optimum Giyim
24,Bodrum Gürece Concept
26,Capitol Giyim
29,Bursa Korupark Giyim
32,Trabzon Forum City
33,Airport Outlet
38,Bodrum Milta Marina
39,Ankara Ankamall Giyim
40,Antalya Agora Giyim
43,Marmaris Solaris City
51,İzmir Agora City
52,Nautilus Home
55,Ankara Arcadium City
56,Bodrum Turgutreis Marina
57,Bodrum Yalıkavak Marina
58,Maltepe Park Giyim
59,Ankara Optimum Outlet
60,Antalya Deepo Giyim
64,Adana M1 City
68,Kayseri Park City
74,Nişantaşı Concept
76,Susurluk Festiva Giyim
80,Eskişehir Vega City
84,Antalya Agore C.Outlet
86,İzmir Forum Giyim
88,İzmir Selway Outlet
89,İzmir Alsancak Concept
91,Samsun Yeşilyurt Giyim
93,Gebze Re:Life
97,Alanya City
98,Ankara Panora Concept
100,Adana Concept
102,Tekirdağ Tekira City
104,Adana 01 Burda Giyim
107,Cevahir Giyim
110,Çeşme Marina
113,Ankara Next Level Concept
114,Antalya Rixos Marina
116,Çorum Ahlpark Giyim
117,212 Outlet
118,Malatya Park Giyim
120,K.maraş Piazza Giyim
121,Ankara Gordion City
122,Pendik Marina Kadın
124,Kozzy City
125,Gebze Center City
129,Mersin Yat Limanı Marina
130,Yalova Setur Marina
132,Edirne Margi City
136,İzmir Arastapark City
137,Akçay Yasa Giyim
138,Akbatı City
139,pendik marina home
140,Bandırma Liman Giyim
141,İzmir Optimum Giyim
142,İzmir Hilltown Giyim
143,Arenapark Giyim
146,Maslak Concept
147,Balıkesir Burda Giyim
148,Capitol Home
151,Aqua Florya Home
152,Buyaka City
153,Antalya Aspendos Concept
155,İskenderun Forbes Marina
157,Bodrum Midtown City
158,Kuşadası Setur Marina
159,Nautilus Giyim
161,Ankara Kentpark Home
163,Emaar City
164,Pendik Marina Erkek
167,Alanya Marina
172,Göcek Marina Kadın
173,Samsun Piazza Giyim
174,Fethiye Marina Yeni
175,Bodrum Avenue Giyim
176,İstinyePark Giyim
177,Ümraniye Meydan Giyim
180,Brandium Re:Life
181,Aqua Florya Giyim
182,Mecidiyeköy Outlet
183,Vadistanbul City
186,İçerenköy City's Home
187,Balat Marina
190,Akmerkez Giyim
191,Akmerkez Home
193,Bodrum Anthaven Marina
195,Akasya Giyim
196,Masko Concept
201,Modoko Concept
202,Mall of İstanbul Concept
205,Palladium Concept
206,Maltepe Piazza Giyim
208,İzmir MaviBahçe Concept
209,İzmit Burda Home
210,Tuzla Viaport Marina
211,Adapazarı Agora Giyim
213,Çanakkale Burda Giyim
214,Çanakkale Burda Home
216,Bodrum Plaza Concept
217,Göcek Marina Erkek
218,Fethiye Marina
219,Mersin Concept
221,Antalya Lara Concept
222,Ayvalık Marina
223,Modoko Concept 2
224,İstmarin City
227,Bursa Anatolium Giyim
235,İzmit Burda Giyim
238,Gaziantep Sanko Giyim
239,Carousel Giyim
243,Urla Marina
245,İçerenköy City's Giyim
246,Büyükada Marina
247,Nişantaşı City's Giyim
248,İzmir İstinyePark Giyim
249,Göztepe Optimum C.Outlet
251,Bursa FSM Concept
252,Bursa Downtown Giyim
254,Tema World Giyim
257,Kaş Marina";

        await discoveryService.SyncStoreDevicesFromCsvAsync(csvContent);
        logger.LogInformation("Mağaza cihazları başarıyla senkronize edildi.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Migration / Store Sync sırasında hata oluştu.");
    }
}

// ===========================
// 🔥 APP MIDDLEWARE PIPELINE
// ===========================
app.UseCors("AllowMudoSoftFrontend");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<EncryptedPayloadMiddleware>();
app.MapControllers();
app.Urls.Add("http://0.0.0.0:5102");

// ===========================
// 🔥 RUN
// ===========================
app.Run();
