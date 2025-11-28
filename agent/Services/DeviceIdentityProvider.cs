using Microsoft.Extensions.Logging;
using Mudosoft.Agent.Interfaces;

namespace Mudosoft.Agent.Services;

public sealed class DeviceIdentityProvider : IDeviceIdentityProvider
{
    // ID'yi depolayacağımız dosya adı
    private const string DeviceIdFilePath = "device_id.txt";
    private readonly ILogger<DeviceIdentityProvider> _logger;
    private readonly string _deviceId;

    public DeviceIdentityProvider(ILogger<DeviceIdentityProvider> logger)
    {
        _logger = logger;
        _deviceId = GetOrCreateDeviceId();
    }

    // Arayüz gereksinimi: Kalıcı ID'yi döndürür
    public string GetDeviceId()
    {
        return _deviceId;
    }

    private string GetOrCreateDeviceId()
    {
        try
        {
            // 1. ID dosyasını kontrol et ve oku
            if (File.Exists(DeviceIdFilePath))
            {
                string existingId = File.ReadAllText(DeviceIdFilePath).Trim();
                if (!string.IsNullOrEmpty(existingId))
                {
                    _logger.LogInformation("Kalıcı cihaz ID'si bulundu: {DeviceId}", existingId);
                    return existingId;
                }
            }

            // 2. Bulunamazsa yeni bir GUID oluştur
            // 'N' formatı tireleri kaldırır ve SQL'deki NVARCHAR(450) ile uyumludur.
            string newId = Guid.NewGuid().ToString("N"); 
            
            // 3. Yeni ID'yi diske kaydet
            File.WriteAllText(DeviceIdFilePath, newId);
            _logger.LogWarning("Yeni kalıcı cihaz ID'si oluşturuldu ve diske kaydedildi: {DeviceId}", newId);
            return newId;
        }
        catch (Exception ex)
        {
            // Kritik hata durumunda geçici bir GUID kullan (sadece hata anında)
            _logger.LogError(ex, "Cihaz ID'si okunamadı veya oluşturulamadı. Geçici ID kullanılıyor.");
            return Guid.NewGuid().ToString("N");
        }
    }
}