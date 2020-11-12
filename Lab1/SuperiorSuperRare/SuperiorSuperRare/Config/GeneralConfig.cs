using System.Net;

namespace SuperiorSuperRare.Config
{
    /// <summary>
    ///     全局设置
    /// </summary>
    public class GeneralConfig : BaseConfig
    {
        //public int CacheSizeLimit{ get; set; }
        public bool EnableAuthentication { get; set; }
        public UserConfigCollection Users { get; } = new UserConfigCollection();
        public bool RejectHttpProxy { get; set; }
        public int BufferSize { get; set; } = 8192;

        public (string, int) Server { get; set; } = (IPAddress.Any.ToString(), 8080);
        public int ConnectionLimit { get; set; } = 1000;
    }
}