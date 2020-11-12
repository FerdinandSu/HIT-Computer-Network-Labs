using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit;
using PlasticMetal.MobileSuit.Core;
using PlasticMetal.MobileSuit.ObjectModel;
using PlasticMetal.MobileSuit.Parsing;
using Teleport.Rdt;

namespace Teleport.Lftp
{
    class LftpClientController : SuitClient, IExitInteractive
    {
        private CancellationTokenSource Source { get; } = new CancellationTokenSource();
        private RdtStream? DataStream { get; set; }
        public int ListenerPort => Listener.Port;
        private RdtStream Listener { get; }
        //private IPAddress Ip { get; }
        private RdtConfig Config { get; }
        public LftpClientController(RdtConfig config)
        {
            Listener = RdtStream.CreateListener(config);
            //Ip = endPoint?.Address ?? IPAddress.Loopback;
            Config = config;
        }
        public async Task Busy()
        {

            await IO.WriteLineAsync($"远程服务器正忙，请等待当前传输完成后再试.", OutputType.Error);


        }
        public async Task Conflict(string fn)
        {

            await IO.WriteLineAsync($"{fn}与现有文件冲突，请更名后重试.", OutputType.Error);
        }

        public async Task ConnectionLost()
        {
            await IO.WriteLineAsync($"失去同步.", OutputType.Error);
            Halt();
        }
        public async Task Connect(
            [SuitParser(typeof(Parsers), nameof(Parsers.ParseInt))]
            int port)
        {
            for (; !Source.IsCancellationRequested;)
            {
                var s = await Listener.AcceptAsync();
                if (s?.RemoteEndPoint?.Port != port) continue;
                DataStream = s;
                await IO.WriteLineAsync($"数据流连接成功.", OutputType.CustomInfo);
                break;
            }
        }
        public async Task Put(string src)
        {
            if (DataStream == null)
            {
                await ConnectionLost();
                return;
            }
            var fi = new FileInfo(src);

            var fr = fi.OpenRead();
            var buf = new byte[Config.DiagramMaxLength * Config.Parallel];
            var rc = 0;
            var length = fi.Length;
            Task.Run(async () =>
            {
                var rdsp = 0;
                var clock = new System.Diagnostics.Stopwatch();
                clock.Start();
                for (; rc < length && !Source.IsCancellationRequested;)
                {
                    var rd = await fr.ReadAsync(buf, Source.Token);
                    await DataStream.WriteAsync(buf, 0, rd, Source.Token);

                    rc += rd;
                    rdsp += rd;
                    if ((double)rdsp / length > 0.05)
                    {

                        IO.WriteLineAsync($"Sending {src}[{rc}/{length}]-{(double)rc / length:0%}-{(double)8 * rc / clock.ElapsedMilliseconds / 1000:0.00}Mbps")
                            .GetAwaiter().OnCompleted(() => { rdsp = 0; });
                    }

                }

                clock.Stop();

                fr.Close();
                IO.WriteLineAsync($"{src} Sent - {clock.ElapsedMilliseconds / 1000:0.0}s", OutputType.AllOk).GetAwaiter();
            }).GetAwaiter();

        }
        public async Task NotFound(string fn)
        {

            await IO.WriteLineAsync($"{fn}未找到，请确定后重试.", OutputType.Error);
        }
        private void Halt()
        {
            Source.Cancel();
            DataStream?.Close();
        }
        public async Task Get(
            [SuitParser(typeof(Parsers), nameof(Parsers.ParseLong))]
            long length,
            string des)
        {
            if (DataStream == null)
            {
                await ConnectionLost();
                return;
            }
            var fi = new FileInfo(des);

            var fw = fi.Create();
            var buf = new byte[Config.DiagramMaxLength * Config.Parallel];
            var rc = 0;
            var rdsp = 0;
            Task.Run(async () =>
            {
                var clock = new System.Diagnostics.Stopwatch();
                clock.Start();
                for (; rc < length && !Source.IsCancellationRequested;)
                {
                    var rd = await DataStream.ReadAsync(buf, Source.Token);
                    await fw.WriteAsync(buf, 0, rd, Source.Token);

                    rc += rd;
                    rdsp += rd;
                    if ((double)rdsp / length > 0.05)
                    {

                        IO.WriteLineAsync($"Receiving {des}[{rc}/{length}]-{(double)rc / length:0%}-{(double)8 * rc / clock.ElapsedMilliseconds / 1000:0.00}Mbps")
                            .GetAwaiter().OnCompleted(() => { rdsp = 0; });

                    }

                }

                clock.Stop();
                fw.Close();
                IO.WriteLineAsync($"{des} Received - {clock.ElapsedMilliseconds/1000:0.0}s", OutputType.AllOk).GetAwaiter();
            }).GetAwaiter();
        }
        [SuitIgnore]
        public void OnExit()
        {
            Halt();
            Source.Dispose();
        }
    }
}
