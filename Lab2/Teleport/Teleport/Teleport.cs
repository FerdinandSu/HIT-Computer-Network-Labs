using Newtonsoft.Json;
using PlasticMetal.MobileSuit;
using PlasticMetal.MobileSuit.Core;
using PlasticMetal.MobileSuit.ObjectModel;
using PlasticMetal.MobileSuit.ObjectModel.Future;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit.Parsing;
using Teleport.Lftp;
using Teleport.Rdt;

namespace Teleport
{
    [SuitInfo("Teleport")]
    internal class Teleport : SuitClient, IStartingInteractive, IExitInteractive
    {
        public const int ParallelCount = 10;
        private RdtConfig RdtConfig { get; set; } = new RdtConfig();
        private LftpServer? Server { get; set; }
        /// <summary>
        /// 修改配置
        /// </summary>
        public void Config()
        {
            var newConfig = new RdtConfig
            {
                PackageLoss = int.Parse(IO.ReadLine("丢包率/百分比[0-100]", RdtConfig.PackageLoss.ToString()) ?? "0"),
                SendRangeLength = sbyte.Parse(IO.ReadLine("发送区间长度[1-125]", RdtConfig.SendRangeLength.ToString()) ?? "60"),
                DiagramMaxLength = ushort.Parse(IO.ReadLine("最大报文长度[1-65534]", RdtConfig.DiagramMaxLength.ToString()) ?? "60000"),
                SendTimeout = int.Parse(IO.ReadLine("发送超时界限[100-1000]", RdtConfig.SendTimeout.ToString()) ?? "200")
            };
            if (newConfig.Check())
            {
                RdtConfig = newConfig;
                IO.WriteLine("配置修改成功，重启后生效。");
            }
            else
            {
                IO.WriteLine("配置修改失败。");
            }

        }

        private static void Main(string[] args)
        {
            Suit.GetBuilder()
                .UsePrompt<PowerLineThemedPromptServer>()
                .Build<Teleport>()
                .Run(args);

        }
        [SuitInfo("启动LFTP客户端")]
        [SuitAlias("Lftp")]
        public void CreateClient()
        {
            Suit.GetBuilder()
                .UsePrompt<PowerLineThemedPromptServer>()
                .Build(new LftpClient(RdtConfig))
                .Run();
        }
        [SuitInfo("启动/重启LFTP服务器")]
        [SuitAlias("Serv")]
        public void StartServer([SuitParser(typeof(Parsers), nameof(Parsers.ParseInt))] int port = 0)
        {
            if (Server != null)
            {
                port = Server.Port;
                StopServer();

            }
            Server = new LftpServer(RdtConfig, port);
            IO.WriteLine($"LFTP服务器启动成功: 127.0.0.1:{Server.Port}");

        }
        [SuitInfo("关闭LFTP服务器")]
        [SuitAlias("Stop")]
        public void StopServer()
        {
            if (Server != null)
            {
                Server.Stop();
                Server = null;
                IO.WriteLine($"LFTP服务器已停止。");
            }
            
        }
        [SuitIgnore]
        public void OnInitialized()
        {
            if (File.Exists("config.json"))
            {
                RdtConfig = JsonConvert.DeserializeObject<RdtConfig>(File.ReadAllText("config.json"));
            }
        }
        [SuitIgnore]
        public void OnExit()
        {
            File.WriteAllText("config.json", JsonConvert.SerializeObject(RdtConfig));
        }
    }
}
