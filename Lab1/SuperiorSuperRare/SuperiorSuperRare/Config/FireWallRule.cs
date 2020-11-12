using System.Text.RegularExpressions;

namespace SuperiorSuperRare.Config
{
    /// <summary>
    ///     防火墙规则
    /// </summary>
    public class FireWallRule
    {
        public bool IsAllow { get; set; } = true;
        public string Rule { get; set; } = "";

        public bool Allows(string url)
        {
            return !Regex.IsMatch(url, Rule) ^ IsAllow;
        }
    }
}