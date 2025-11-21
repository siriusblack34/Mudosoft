namespace MudoSoft.Backend.Models;

public class ExecuteActionRequest
{
    public string DeviceId { get; set; } = default!;
    
    // Çalıştırılacak betik komutu (Örn: Get-Service | ConvertTo-Json)
    public string Script { get; set; } = default!; 
}