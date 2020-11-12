using System;
using PlasticMetal.MobileSuit.ObjectModel;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit;
using PlasticMetal.MobileSuit.Core;
using PlasticMetal.MobileSuit.Parsing;
using Teleport.Rdt;

namespace Teleport.Lftp
{
    [SuitInfo("LFTP客户端")]
    public class LftpClient : SuitClient, IExitInteractive
    {
        /// <summary>
        /// 连接到服务端控制器的流
        /// </summary>
        private RdtStream? ControllerStream { get; set; }
        /// <summary>
        /// 连接到服务端控制器的流写入器
        /// </summary>
        private StreamWriter? Controller { get; set; }
        /// <summary>
        /// Rdt配置
        /// </summary>
        private RdtConfig Config { get; }
        /// <summary>
        /// 解析IP
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static object ParseIp(string s)
            => IPAddress.Parse(s);

        public LftpClient(RdtConfig config)
        {
            //Ip = endPoint?.Address ?? IPAddress.Loopback;
            Config = config;
        }

        [SuitInfo("连接到服务器: C <IP> <Port>")]
        [SuitAlias("C")]
        public async Task Connect(
            [SuitParser(typeof(LftpClient), nameof(ParseIp))]
            IPAddress ip,
            [SuitParser(typeof(Parsers), nameof(Parsers.ParseInt))]
            int port)
        {
            if (Controller != null)
            {
                await Controller.WriteLineAsync("Exit");

            }

            ControllerStream = await RdtStream.CreateClientAsync(
                Config, new IPEndPoint(ip, port));
            if (ControllerStream == null)
            {
                await IO.WriteLineAsync($"连接服务器失败。");
                return;
            }

            Controller = new StreamWriter(ControllerStream) {AutoFlush = true};
            var cc = new LftpClientController(Config);
            Task.Run(() =>
            {
                Suit.GetBuilder()
                    .ConfigureIn(new StreamReader(ControllerStream))
                    .UseLog(ILogger.OfFile($"ClientController-{cc.ListenerPort}.log"))
                    .Build(cc)
                    .Run();
            }).GetAwaiter();
            await Controller.WriteLineAsync($"Connect {cc.ListenerPort}");

        }

        [SuitInfo("发送文件: Put <源> <目的>")]
        public async Task Put(string src, string des)
        {
            if (Controller == null)
            {
                await IO.WriteLineAsync($"未连接到服务器.");
                return;
            }

            var fi = new FileInfo(src);

            if (!fi.Exists)
            {
                await IO.WriteLineAsync($"未找到本地文件{src}");
                return;
            }


            await Controller.WriteLineAsync($"Put {fi.Length} {src} {des}");

        }

        [SuitInfo("断开连接")]
        public async Task Disconnect()
        {
            if (Controller == null) return;
            await Controller.WriteLineAsync($"Exit");
            Controller = null;
            if (ControllerStream != null)
            {
                await ControllerStream.CloseAsync();
                ControllerStream = null;
            }

        }
        [SuitInfo("收取文件: Get <源> <目的>")]
        public async Task Get(
            string src, string des)
        {
            if (Controller == null)
            {
                await IO.WriteLineAsync($"未连接到服务器.");
                return;
            }

            var fi = new FileInfo(des);
            if (fi.Exists)
            {
                await IO.WriteLineAsync($"{des}文件已存在。");
                return;
            }

            await Controller.WriteLineAsync($"Get {src} {des}");
        }

        [SuitIgnore]
        public void OnExit()
        {
            try
            {
                Disconnect().GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                
            }
            
        }
    }
}
