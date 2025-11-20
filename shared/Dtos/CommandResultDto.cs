public class CommandResultDto
{
    public Guid CommandId { get; set; }   // 🔥 string DEĞİL → Guid
    public string DeviceId { get; set; } = "";
    public bool Success { get; set; }
    public string Output { get; set; } = "";
}
