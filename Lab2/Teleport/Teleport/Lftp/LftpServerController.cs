using PlasticMetal.MobileSuit.ObjectModel;
using PlasticMetal.MobileSuit.Parsing;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit.Core;
using Teleport.Rdt;
using PlasticMetal.MobileSuit;

namespace Teleport.Lftp
{
    /// <summary>
    /// MobileSuit驱动的控制器
    /// </summary>
    public class LftpServerController : SuitClient, IStartingInteractive, IExitInteractive
    {
        /// <summary>
        /// 取消标识符
        /// </summary>
        private CancellationTokenSource Source { get; } = new CancellationTokenSource();
        /// <summary>
        /// 数据流
        /// </summary>
        private RdtStream? DataStream { get; set; }

        /// <summary>
        /// 传输锁
        /// </summary>
        private bool TransportLock { get; set; }
        /// <summary>
        /// 远程Ip
        /// </summary>
        private IPAddress Ip { get; }
        /// <summary>
        /// Rdt配置
        /// </summary>
        private RdtConfig Config { get; }
        public LftpServerController(IPEndPoint? endPoint, RdtConfig config)
        {
            Ip = endPoint?.Address ?? IPAddress.Loopback;
            Config = config;
        }
        /// <summary>
        /// 进行数据连接
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        public async Task Connect(
            [SuitParser(typeof(Parsers), nameof(Parsers.ParseInt))]
            int port)
        {
            DataStream = await RdtStream.CreateClientAsync(
                Config, new IPEndPoint(Ip, port));
            await IO.WriteLineAsync($"Connect {DataStream?.Port ?? -1}");

        }
        /// <summary>
        /// 发送文件
        /// </summary>
        /// <param name="length">长度</param>
        /// <param name="src">源</param>
        /// <param name="des">目标</param>
        /// <returns></returns>
        public async Task Put(
            [SuitParser(typeof(Parsers), nameof(Parsers.ParseLong))]
            long length, string src, string des)
        {
            if (TransportLock)
            {
                await IO.WriteLineAsync($"Busy");

                return;
            }

            var fi = new FileInfo(des);
            if (fi.Exists)
            {
                await IO.WriteLineAsync($"Conflict {des}");

                return;
            }

            if (DataStream == null)
            {
                await IO.WriteLineAsync($"ConnectionLost");

                return;
            }
            await IO.WriteLineAsync($"Put {src}");

            var fw = fi.Create();
            var buf = new byte[Config.DiagramMaxLength * Config.Parallel];
            var rcv = 0;
            TransportLock = true;
            Task.Run(async () =>
            {
                for (; rcv < length && !Source.IsCancellationRequested;)
                {
                    var cr = await DataStream.ReadAsync(buf, Source.Token);
                    await fw.WriteAsync(buf, 0, cr, Source.Token);
                    rcv += cr;
                    await Log.LogDebugAsync($"nowthat:{rcv}/{length}");
                }
                await Log.LogDebugAsync("Unlocking...");
                TransportLock = false;
                await Log.LogDebugAsync($"Unlocked");
                fw.Close();
            }).GetAwaiter();

        }
        /// <summary>
        /// 停机
        /// </summary>
        /// <returns></returns>
        public async Task Halt()
        {
            await IO.WriteLineAsync($"Exit");

            Source.Cancel();
        }
        /// <summary>
        /// 获取文件
        /// </summary>
        /// <param name="src">源</param>
        /// <param name="des">目标</param>
        /// <returns></returns>
        public async Task Get(
            string src, string des)
        {
            if (TransportLock)
            {
                await IO.WriteLineAsync($"Busy");

                return;
            }

            var fi = new FileInfo(src);
            if (!File.Exists(src))
            {
                await IO.WriteLineAsync($"NotFound {src}");

                return;
            }

            if (DataStream == null)
            {
                await IO.WriteLineAsync($"ConnectionLost");

                return;
            }
            await IO.WriteLineAsync($"Get {fi.Length} {des}");

            TransportLock = true;
            var fr = fi.OpenRead();
            var buf = new byte[Config.DiagramMaxLength * Config.Parallel];
            var length = fi.Length;
            var snd = 0;
            Task.Run(async () =>
                {
                    /*for (; snd < length && !Source.IsCancellationRequested;)
                    {
                        var cr = await DataStream.ReadAsync(buf, Source.Token);
                        await fw.WriteAsync(buf, 0, cr, Source.Token);
                        rcv += cr;
                    }*/
                    await fr.CopyToAsync(DataStream, Config.DiagramMaxLength * Config.Parallel, Source.Token);
                    await Log.LogDebugAsync("Unlocking...");
                    TransportLock = false;
                    await Log.LogDebugAsync("Unlocked");
                    fr.Close();
                }).GetAwaiter();
        }
        [SuitIgnore]
        public void OnExit()
        {
            Halt().GetAwaiter().GetResult();
            DataStream?.Close();
            Source.Dispose();
        }
        [SuitIgnore]
        public void OnInitialized()
        {
            IO.DisableTimeMark = true;
        }
    }
}
