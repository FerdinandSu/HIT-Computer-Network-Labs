using System.Text.RegularExpressions;

namespace SuperiorSuperRare.Config
{
    /// <summary>
    ///     重定向规则
    /// </summary>
    public class RedirectionRule
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";

        /// <summary>
        ///     应用重定向。注意，此方法修改传入的参数！
        /// </summary>
        /// <param name="input">需要被重定向的url，会被修改。</param>
        public void Apply(ref string input)
        {
            input = Regex.Replace(input, Key, Value);
        }
    }
}