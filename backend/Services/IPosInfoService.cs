public interface IPosInfoService
{
    Task<string?> GetPosVersion(string ip);
}