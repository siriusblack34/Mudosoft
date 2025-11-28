namespace Mudosoft.Agent.Interfaces
{
    public interface IDeviceIdentityProvider
    {
        /// <summary>
        /// Cihazın kalıcı ve benzersiz ID'sini döndürür.
        /// </summary>
        string GetDeviceId();
    }
}