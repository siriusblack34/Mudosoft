namespace Mudosoft.Shared.Dtos
{
    public class OsInfoDto
    {
        // Başlangıç değeri atayarak null atanmaz kuralını karşılayın
        public string Name { get; set; } = string.Empty; // ✅ CS8618 Çözümü
        public string Version { get; set; } = string.Empty; // ✅ CS8618 Çözümü
    }
}