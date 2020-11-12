using System.Collections.ObjectModel;

namespace SuperiorSuperRare.Config
{
    /// <summary>
    ///     用户设置
    /// </summary>
    public class UserConfig : BaseConfig
    {
        public UserConfig(string userName, string password)
        {
            UserName = userName;
            Password = password;
        }

        public string UserName { get; }
        public string Password { get; }
    }

    public class UserConfigCollection : KeyedCollection<string, UserConfig>
    {
        protected override string GetKeyForItem(UserConfig item)
        {
            return item.UserName;
        }
    }
}