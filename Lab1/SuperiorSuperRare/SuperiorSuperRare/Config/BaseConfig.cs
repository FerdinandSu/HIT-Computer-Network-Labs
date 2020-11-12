using System.Collections.Generic;

namespace SuperiorSuperRare.Config
{
    /// <summary>
    ///     基础配置
    /// </summary>
    public abstract class BaseConfig
    {
        public bool EnableFireWall { get; set; }
        public bool EnableRedirection { get; set; }
        public List<RedirectionRule> RedirectionRules { get; set; } = new List<RedirectionRule>();
        public List<FireWallRule> FireWallRules { get; set; } = new List<FireWallRule>();
    }
}