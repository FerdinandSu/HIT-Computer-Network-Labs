using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json;
using PlasticMetal.MobileSuit;
using PlasticMetal.MobileSuit.Core;
using PlasticMetal.MobileSuit.ObjectModel;
using PlasticMetal.MobileSuit.ObjectModel.Future;
using SuperiorSuperRare.Config;
using SuperiorSuperRare.Proxy;

namespace SuperiorSuperRare
{
    /// <summary>
    ///     UI，基于MobileSuit
    /// </summary>
    [SuitInfo("SuperiorSuperRare")]
    internal class SuperiorSuperRare : SuitClient
    {
        public SuperiorSuperRare()
        {
            //加载配置
            Config = JsonConvert.DeserializeObject<GeneralConfig>(File.ReadAllText("config.json"));
            Token = new CancellationTokenSource();
        }

        private ProxyServer? Server { get; set; }
        private GeneralConfig Config { get; set; }
        private CancellationTokenSource Token { get; set; }

        private bool ServerRunning => Server != null;

        [SuitAlias("stop")]
        [SuitInfo("停止服务")]
        public void StopServer()
        {
            if (!ServerRunning) return;
            Token.Cancel();
            Thread.Sleep(100);
            Token = new CancellationTokenSource();
            Server?.Dispose();
            Server = null;
            IO.WriteLine("服务已停止。");
            Log.LogDebug("服务停止.");
        }

        [SuitAlias("rst")]
        [SuitInfo("重启服务")]
        public void RestartServer()
        {
            if (!ServerRunning) return;
            StopServer();
            StartServer();
        }

        [SuitAlias("strt")]
        [SuitInfo("启动服务")]
        public void StartServer()
        {
            if (ServerRunning) return;
            Server = new ProxyServer(Config, async (client, token)
                => await new ProxyHandler().Run(client, Config, Log, Token.Token), Token.Token);
            IO.WriteLine($"服务已启动: {Config.Server.AsIpEndPoint()}。");
            Log.LogDebug($"服务启动: {Config.Server.AsIpEndPoint()}。");
        }

        [SuitAlias("rc")]
        [SuitInfo("重新载入配置文件")]
        public void ReloadConfig()
        {
            var running = ServerRunning;
            StopServer();
            Config = JsonConvert.DeserializeObject<GeneralConfig>(File.ReadAllText("config.json"));
            Log.LogDebug("配置重载.");
            if (running) StartServer();
        }

        [SuitAlias("ec")]
        [SuitInfo("编辑配置文件")]
        public void EditConfig()
        {
            Process.Start(new ProcessStartInfo("config.json") {UseShellExecute = true});
        }

        [SuitAlias("log")]
        [SuitInfo("查看日志")]
        public void ViewLog()
        {
            Process.Start(new ProcessStartInfo("proxy.log") {UseShellExecute = true});
        }

        private static void Main()
        {
            var suit = Suit.GetBuilder()
                .UseBuildInCommand<BuildInCommandServer>()
                .UseLog(ILogger.OfFile("proxy.log"))
                .UsePrompt<PowerLineThemedPromptServer>()
                .Build<SuperiorSuperRare>();
            suit.RunCommand("strt");
            suit.Run();
        }
    }
}