using System.Text.Json;
using System.Text.Json.Serialization;

namespace MudoSoft.Backend;

public class TimeZoneConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // JSON'dan okurken, zamanı doğrudan UTC kabul et.
        if (reader.TokenType == JsonTokenType.String)
        {
            if (DateTime.TryParse(reader.GetString(), out DateTime value))
            {
                // Okunan zamanı açıkça UTC olarak işaretle
                return DateTime.SpecifyKind(value, DateTimeKind.Utc);
            }
        }
        return default; 
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Veritabanında UTC saklıyoruz. Serileştirme sırasında
        // zamanı ISO 8601 formatında ve UTC son eki ('Z') ile yaz.
        if (value.Kind == DateTimeKind.Unspecified)
        {
             // Eğer tip belirtilmemişse, varsayılanı UTC yap
             value = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }

        // Değeri UTC'ye çevir ve 'Z' (Zulu Time/UTC) ile bitirerek gönder
        writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"));
    }
}