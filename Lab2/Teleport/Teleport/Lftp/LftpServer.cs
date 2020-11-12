using PlasticMetal.MobileSuit;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit.Core;
using Teleport.Rdt;

namespace Teleport.Lftp
{
    /// <summary>
    /// 服务端
    /// </summary>
    public class LftpServer
    {
        /// <summary>
        /// 停止服务器
        /// </summary>
        public void Stop()
        {
            Source.Cancel();
            Listener.Close();
            Thread.Sleep(100);
            Source.Dispose();
        }
        /// <summary>
        /// 取消标识符
        /// </summary>
        private CancellationTokenSource Source { get; } = new CancellationTokenSource();
        /// <summary>
        /// 配置
        /// </summary>
        private RdtConfig Config{ get; }
        /// <summary>
        /// 端口
        /// </summary>
        public int Port => Listener.Port;
        /// <summary>
        /// Listener流
        /// </summary>
        protected RdtStream Listener { get; set; }
        public LftpServer(RdtConfig config, int port = 0)
        {
            Listener= RdtStream.CreateListener(config, port);
            Config = config;
            Run().GetAwaiter();
        }
        /// <summary>
        /// 服务器运行线程
        /// </summary>
        /// <returns></returns>
        private async Task Run()
        {
            for (;!Source.IsCancellationRequested;)
            {
                var server= await Listener.AcceptAsync();
                if (server == null) continue;
                Task.Run(() =>
                {
                    Suit.GetBuilder()
                        .ConfigureIn(new StreamReader(server))
                        .UseLog(ILogger.OfFile($"ServerController-{Listener.Port}.log"))
                        .ConfigureOut(new StreamWriter(server) { AutoFlush = true })
                        .Build(new LftpServerController(server.RemoteEndPoint,Config))
                        .Run();
                    server.Close();
                }).GetAwaiter();

            }
        }
    }
}
