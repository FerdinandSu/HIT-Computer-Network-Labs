using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit.Core;

namespace Teleport.Rdt
{
    //支持所有协议
    public class RdtStream : Stream
    {
        private long RcvCount { get; set; } = 0;
        private long SndCount { get; set; } = 0;
        /// <summary>
        /// 流的工作模式
        /// </summary>
        public enum RdtStreamMode
        {
            Listener = 0,
            Server = -1,
            Client = 1,
            Closed = -2
        }
        /// <summary>
        /// 刷新时间
        /// </summary>
        private int SleepTime{ get; }
        /// <summary>
        /// 用于Client和Listener的构造函数
        /// </summary>
        /// <param name="config">Rdt配置</param>
        /// <param name="mode">模式</param>
        /// <param name="portNumber">端口号</param>
        private RdtStream(RdtConfig config, RdtStreamMode mode, int portNumber = 0)
        {
            if (!config.Check())
            {
                throw new ArgumentException();
            }

            SleepTime = 1000 / config.RefreshRate;
            Mode = mode;
            Config = config;
            Client = new UdpClient(portNumber);
            ListenerLog = ILogger.OfFile($"{Port}l.log");
            Log = ILogger.OfFile($"{Port}.log");
        }
        /// <summary>
        /// 用于Server的构造函数
        /// </summary>
        /// <param name="upStream">上游Listener</param>
        private RdtStream(RdtStream upStream)
        {
            Mode = RdtStreamMode.Server;
            Client = upStream.Client;
            Config = upStream.Config;
            Log = upStream.Log;
            ListenerLog = upStream.ListenerLog;
            SleepTime = upStream.SleepTime;
        }
        /// <summary>
        /// 监听任务，完成ACK和一般报文的收取
        /// </summary>
        private Task? ListenTask { get; set; }
        /// <summary>
        /// 工作模式
        /// </summary>
        public RdtStreamMode Mode { get; private set; }
        /// <summary>
        /// Listener下游的Server们
        /// </summary>
        private Dictionary<IPEndPoint, RdtStream> DownStreams { get; } = new Dictionary<IPEndPoint, RdtStream>();
        /// <summary>
        /// 流关闭用。任务取消标识符
        /// </summary>
        private CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();
        /// <summary>
        /// 发送队列
        /// </summary>
        private List<RdtDiagram> SendQueue { get; } = new List<RdtDiagram>();
        /// <summary>
        /// Listener的Server接收队列
        /// </summary>
        private ConcurrentQueue<RdtStream> AcceptQueue { get; } = new ConcurrentQueue<RdtStream>();
        /// <summary>
        /// 报文接收队列
        /// </summary>
        private ConcurrentQueue<RdtDiagram> ReceiveQueue { get; } = new ConcurrentQueue<RdtDiagram>();
        /// <summary>
        /// SR用报文缓存接收队列
        /// </summary>
        private Dictionary<uint,RdtDiagram> CacheQueue { get; } = new Dictionary<uint, RdtDiagram>();
        /// <summary>
        /// 远程的地址
        /// </summary>
        public IPEndPoint? RemoteEndPoint { get; private set; }
        /// <summary>
        /// 发送报文的状态字典
        /// </summary>
        private ConcurrentDictionary<uint, DiagramStatus> AckStatus { get; } =
            new ConcurrentDictionary<uint, DiagramStatus>();

        /// <summary>
        ///     发送区间左指针，1~126
        /// </summary>
        private uint SendRangePointer { get; set; } = 1;

        /// <summary>
        ///     发送序号指针，1~126
        /// </summary>
        private uint SendIndexPointer { get; set; } = 1;

        /// <summary>
        ///     接收指针，1~126
        /// </summary>
        private uint ReceivePointer { get; set; } = 1;
        /// <summary>
        /// 可靠数据传输配置
        /// </summary>
        private RdtConfig Config { get; }
        /// <summary>
        /// 端口上的Udp客户端
        /// </summary>
        private UdpClient Client { get; }
        /// <summary>
        /// 流的工作端口(收听和发送)
        /// </summary>
        public int Port => ((IPEndPoint)Client.Client.LocalEndPoint).Port;
        /// <summary>
        /// 写超时标记
        /// </summary>
        private bool WriteTimeoutFlag { get; set; }
        /// <summary>
        /// 流是否会超时
        /// </summary>
        public override bool CanTimeout => true;
        /// <summary>
        /// 读超时
        /// </summary>
        public override int ReadTimeout
        {
            get => Config.ReadTimeout;
            set => Config.ReadTimeout = value;
        }
        /// <summary>
        /// 写超时
        /// </summary>
        public override int WriteTimeout
        {
            get => Config.WriteTimeout;
            set => Config.WriteTimeout = value;
        }
        /// <summary>
        /// 流是否可读
        /// </summary>
        public override bool CanRead => (Mode == RdtStreamMode.Client || Mode == RdtStreamMode.Server) &&
                                        Config.Type != RdtPointType.Sender;
        /// <summary>
        /// 流是否可查找
        /// </summary>
        public override bool CanSeek => false;
        /// <summary>
        /// 流是否可写
        /// </summary>
        public override bool CanWrite => (Mode == RdtStreamMode.Client || Mode == RdtStreamMode.Server) &&
                                         Config.Type != RdtPointType.Receiver;
        /// <summary>
        /// 不支持。流的长度。
        /// </summary>
        public override long Length => throw new NotSupportedException();
        /// <summary>
        /// 不支持。设定流的位置。
        /// </summary>
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        /// <summary>
        /// 创建一个Listener
        /// </summary>
        /// <param name="config"></param>
        /// <param name="portNumber"></param>
        /// <returns></returns>
        public static RdtStream CreateListener(RdtConfig config, int portNumber = 0)
        {
            var l = new RdtStream(config, RdtStreamMode.Listener, portNumber);
            l.ListenTask = l.Listen();
            return l;
        }
        /// <summary>
        /// 只是发送报文到RemoteEndPoint
        /// </summary>
        /// <param name="diagram"></param>
        /// <returns></returns>
        private Task SendRawDiagramAsync(RdtDiagram diagram)
        {
            return SendRawDiagramAsync(diagram, RemoteEndPoint);
        }
        /// <summary>
        /// 只是发送报文到指定端口
        /// </summary>
        /// <param name="diagram"></param>
        /// <param name="ep"></param>
        /// <returns></returns>
        private async Task SendRawDiagramAsync(RdtDiagram diagram, IPEndPoint? ep)
        {
            
            if (ep == null) return;
            var lose = Config.WillLose();
            if (!lose)
                await Client.SendAsync(diagram.ToBytes(), 1 + diagram.Length, ep);
            await Log.LogDebugAsync($"[{Port}]:{(lose ? "[LOST]" : "")}Sent:{diagram.Index}[{(diagram.IsConfirm ? "C" : "0")}] to {ep}");
        }
        /// <summary>
        /// 发送报文
        /// </summary>
        /// <param name="diagram"></param>
        /// <returns></returns>
        private async Task SendDiagramAsync(RdtDiagram diagram)
        {
            diagram.TimeStamp = DateTime.Now;
            SendQueue.Add(diagram/*(diagram,
                new Timer(
                    state =>
                    {
                        if (!AckStatus.TryUpdate(diagram.Index,
                            DiagramStatus.ActionRequired, DiagramStatus.Sent))
                        {
                            Console.WriteLine(AckStatus.TryGetValue(diagram.Index, out var v));
                            Console.WriteLine(v);
                            Console.WriteLine(diagram.Index);
                        }
                            
                        
                    },
                    AckStatus, Config.SendTimeout, Timeout.Infinite))*/
                );
            await SendRawDiagramAsync(diagram);
            if (!AckStatus.TryAdd(diagram.Index, DiagramStatus.Sent))
                await Log.LogExceptionAsync("Status Add Failed");
        }
        /// <summary>
        /// 发送报文
        /// </summary>
        /// <param name="diagram"></param>
        private void SendDiagram(RdtDiagram diagram)
        {
            SendDiagramAsync(diagram).GetAwaiter().GetResult();
        }
        /// <summary>
        /// 发送和事件日志
        /// </summary>
        private ILogger Log { get; }
        /// <summary>
        /// 监听日志
        /// </summary>
        private ILogger ListenerLog { get; }
        /// <summary>
        /// 关闭流
        /// </summary>
        public override void Close()
        {
            CloseAsync().GetAwaiter().GetResult();
        }
        /// <summary>
        /// 关闭流
        /// </summary>
        /// <returns></returns>
        public async Task CloseAsync()
        {
            switch (Mode)
            {
                case RdtStreamMode.Listener:
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();
                    foreach (var value in DownStreams.Values) await value.CloseAsync();
                    Mode = RdtStreamMode.Closed;
                    Client.Close();
                    if (ListenTask != null)
                        await ListenTask;
                    ListenTask?.Dispose();
                    Client.Dispose();
                    Log.Dispose();
                    ListenerLog.Dispose();
                    break;
                case RdtStreamMode.Server:
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();
                    await SendRawDiagramAsync(RdtDiagram.Bye);
                    Mode = RdtStreamMode.Closed;

                    break;
                case RdtStreamMode.Client:
                    if (!CancellationTokenSource.IsCancellationRequested)
                        CancellationTokenSource.Cancel();
                    await SendRawDiagramAsync(RdtDiagram.Bye);
                    Mode = RdtStreamMode.Closed;
                    Client.Close();
                    if (ListenTask != null)
                        await ListenTask;
                    ListenTask?.Dispose();
                    Client.Dispose();
                    Log.Dispose();
                    ListenerLog.Dispose();
                    break;
                case RdtStreamMode.Closed:
                    return;
                default:
                    throw new ArgumentOutOfRangeException();
            }


        }
        /// <summary>
        /// 创建一个Client
        /// </summary>
        /// <param name="config"></param>
        /// <param name="remote"></param>
        /// <param name="portNumber"></param>
        /// <returns></returns>
        public static RdtStream? CreateClient(RdtConfig config, IPEndPoint remote, int portNumber = 0)
        {
            return CreateClientAsync(config, remote, portNumber).GetAwaiter().GetResult();
        }
        /// <summary>
        /// 创建一个Client
        /// </summary>
        /// <param name="config"></param>
        /// <param name="remote"></param>
        /// <param name="portNumber"></param>
        /// <returns></returns>
        public static async Task<RdtStream?> CreateClientAsync(RdtConfig config, IPEndPoint remote, int portNumber = 0)
        {
            var c = new RdtStream(config, RdtStreamMode.Client, portNumber)
            {
                RemoteEndPoint = remote
            };
            var wtFlag = false;
            await using var tWriteTimeout = new Timer(o => { wtFlag = true; }, null, config.WriteTimeout, -1
            );
            var connectTask = ConnectListener(c, remote);
            for (; !wtFlag;)
            {
                await c.SendRawDiagramAsync(RdtDiagram.Hello, remote);
                var flag = false;
                await using var t = new Timer(
                    o => { flag = true; }, null, config.SendTimeout, -1
                );

                for (; !flag;)
                {
                    if (c.ListenTask != null)
                    {
                        await connectTask;
                        return c;
                    }

                    await Task.Delay(10);
                }

            }

            return null;
        }
        /// <summary>
        /// 连接到Listener
        /// </summary>
        /// <param name="c"></param>
        /// <param name="remote"></param>
        /// <returns></returns>
        private static async Task ConnectListener(RdtStream c, IPEndPoint remote)
        {
            for (; ; )
            {
                var rcv = await c.Client.ReceiveAsync();
                //建立连接
                if (Equals(rcv.RemoteEndPoint, remote) && Equals(rcv.Buffer.ToDiagram(), RdtDiagram.Hello))
                {
                    c.ListenTask = c.Listen();
                    c.RemoteEndPoint = remote;
                    return;
                }
            }

        }
        /// <summary>
        /// 处理收到的报文
        /// </summary>
        /// <param name="diagram"></param>
        /// <returns></returns>
        private async Task HandleDiagram(RdtDiagram diagram)
        {
            if (diagram.IsConfirm)
            {
                if (diagram.Index.IsInRange(SendRangePointer, Config.SendRangeLength) &&
                    AckStatus.ContainsKey(diagram.Index))
                {
                    if (diagram.Data.Length == 0)
                    {
                        if (!SendRangePointer.IsBetween(SendRangePointer, diagram.Index))
                        {
                            
                        }
                        for (var j = SendRangePointer;
                            j.IsBetween(SendRangePointer, diagram.Index);
                            j = j.GetNext())
                        {
                            if (AckStatus.TryUpdate(j, DiagramStatus.Confirmed, DiagramStatus.Sent))
                            {
                                //await ListenerLog.LogCommandAsync($"[{SendRangePointer},{diagram.Index}]{j} Confirmed");
                            }

                        }

                    }

                    else //SR协议的ACK
                    {
                        AckStatus.TryUpdate(diagram.Index, DiagramStatus.Confirmed, DiagramStatus.Sent);
                        //await ListenerLog.LogExceptionAsync($"{diagram.Index} Receive failed");
                    }

                }
                /*else if(AckStatus.ContainsKey(diagram.Index))
                    await ListenerLog.LogExceptionAsync($"[{SendRangePointer},{Config.SendRangeLength}]{diagram.Index} Out of Range");*/
                return;
            }

            if (!CanRead) return;

            if (diagram.Index.IsInRange(ReceivePointer,Config.ReceiveRangeLength))
            {
                //Log.LogCommand($"ACKSend{diagram.Index}[{ReceivePointer},{Config.ReceiveRangeLength}]");
                await SendRawDiagramAsync(diagram.GetConfirm(Config.ReceiveRangeLength>1));
                //如果区间左端点来了，则把缓存队列的内容写入接收队列
                if (diagram.Index == ReceivePointer)
                {
                    ReceiveQueue.Enqueue(diagram);
                    //await Log.LogCommandAsync($"Receive: {RcvCount += diagram.Length}");
                    ReceivePointer = ReceivePointer.GetNext();
                    for (; CacheQueue.ContainsKey(ReceivePointer); ReceivePointer = ReceivePointer.GetNext())
                    {
                        CacheQueue.Remove(ReceivePointer, out var d);
                        if (d == null) continue;
                        ReceiveQueue.Enqueue(d);
                        //await Log.LogCommandAsync($"Receive: {RcvCount += d.Length}");
                    }
                }
                else 
                {
                    //如果包已经收到则丢弃
                    if(!CacheQueue.ContainsKey(diagram.Index))
                        CacheQueue.Add(diagram.Index, diagram);
                }



            }
            else if (Config.ReceiveRangeLength == 1)
            {
                await SendRawDiagramAsync(new RdtDiagram(true, ReceivePointer.GetLast(), new byte[0]));
            }
            else if (diagram.Index.IsInLastRange(ReceivePointer, Config.ReceiveRangeLength))
            {
                await SendRawDiagramAsync(new RdtDiagram(true, diagram.Index, new byte[1]));
            }
        }
        /// <summary>
        /// 监听任务
        /// </summary>
        /// <returns></returns>
        private async Task Listen()
        {
            for (; !CancellationTokenSource.IsCancellationRequested;)
            {
                try
                {
                    var rcv = await Client.ReceiveAsync();
                    if (rcv.Buffer.Length == 0) continue;
                    var diagram = RdtDiagram.Parse(rcv.Buffer);
                    await ListenerLog.LogDebugAsync(
                        $"[{Port}]:Accepted: {diagram.Index}[{(diagram.IsConfirm ? "C" : "0")}] from {rcv.RemoteEndPoint}");
                    switch (diagram.Index)
                    {
                        case 0:
                            //连接请求, 仅Listener可能收到
                            if (!DownStreams.ContainsKey(rcv.RemoteEndPoint))
                            {
                                var server = new RdtStream(this)
                                {
                                    RemoteEndPoint = rcv.RemoteEndPoint
                                };
                                DownStreams.Add(rcv.RemoteEndPoint, server);
                                AcceptQueue.Enqueue(server);
                                await SendRawDiagramAsync(RdtDiagram.Hello, rcv.RemoteEndPoint);
                            }
                            else
                            {
                                await SendRawDiagramAsync(RdtDiagram.Hello, rcv.RemoteEndPoint);
                            }

                            break;
                        case 128:
                            //断开请求
                            if (Mode == RdtStreamMode.Client)
                            {
                                //客户端
                                Mode = RdtStreamMode.Closed;
                                Client.Close();
                                if (!CancellationTokenSource.IsCancellationRequested)
                                    CancellationTokenSource.Cancel();
                            }
                            else if (Mode == RdtStreamMode.Listener)
                            {
                                //服务端
                                if (DownStreams.ContainsKey(rcv.RemoteEndPoint))
                                {
                                    var downStream = DownStreams[rcv.RemoteEndPoint];
                                    downStream.CancellationTokenSource.Cancel();
                                    downStream.Mode = RdtStreamMode.Closed;
                                    DownStreams.Remove(rcv.RemoteEndPoint);
                                    await SendRawDiagramAsync(RdtDiagram.Bye, rcv.RemoteEndPoint);
                                }
                            }

                            break;
                        default:
                            if (Mode == RdtStreamMode.Client)
                                //客户端
                                await HandleDiagram(diagram);
                            else if (Mode == RdtStreamMode.Listener)
                                //服务端
                                if (DownStreams.ContainsKey(rcv.RemoteEndPoint))
                                    await DownStreams[rcv.RemoteEndPoint].HandleDiagram(diagram);

                            break;
                    }
                }
                catch (SocketException)
                {
                    continue;
                }

                
            }
        }
        /// <summary>
        /// 缓冲流，直到缓冲区（发送窗口）中的报文数达到目标
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <param name="targetCount">目标报文数</param>
        /// <returns></returns>
        protected async Task FlushCore(CancellationToken cancellationToken, int targetCount)
        {
            for (; !cancellationToken.IsCancellationRequested && SendQueue.Count > targetCount;)
            {
                if (WriteTimeoutFlag) throw new TimeoutException("Write Timeout.");
                var i = 0;
                var flag = 0;
                //await Log.LogDebugAsync($"Flush[{SendRangePointer},{SendIndexPointer}]:{SendQueue.Count}>>{targetCount}");
                foreach (var queuedDiagram in SendQueue)
                {

                    if (AckStatus.TryGetValue(queuedDiagram.Index, out var status))
                    {
                        switch (status)
                        {

                            case DiagramStatus.Sent:
                                if ((DateTime.Now - queuedDiagram.TimeStamp)?.Milliseconds > Config.SendTimeout)
                                {
                                    await SendRawDiagramAsync(queuedDiagram);
                                    queuedDiagram.TimeStamp = DateTime.Now;
                                }
                                break;
                            case DiagramStatus.Confirmed:
                                //await timer.DisposeAsync();
                                if (i == flag) flag++;
                               
                                break;
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    else
                        await Log.LogExceptionAsync("Status Get Failed");

                    i++;
                }

                for (var j = 0; j < flag; j++)
                {
                    if (!AckStatus.TryRemove(SendRangePointer, out var d))
                        await Log.LogExceptionAsync("Status Remove Failed");

                    //await Log.LogCommandAsync($"Sent: {SndCount+=SendQueue[0].Length}");
                    SendQueue.RemoveAt(0);
                    SendRangePointer = SendRangePointer.GetNext();
                }

                await Task.Delay(SleepTime, cancellationToken);
            }
        }
        /// <summary>
        /// 缓冲流，直到缓冲区（发送窗口）中的报文数达到目标
        /// </summary>
        /// <param name="targetCount">目标报文数</param>
        public void Flush(int targetCount)
        {
            FlushCore(CancellationTokenSource.Token, targetCount).GetAwaiter().GetResult();
        }
        /// <inheritdoc/>
        public override void Flush()
        {
            Flush(0);
        }
        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Mode == RdtStreamMode.Closed || Mode == RdtStreamMode.Listener) throw new NotSupportedException();
            using var twr = new Timer(o => WriteTimeoutFlag = true, null, WriteTimeout, -1);

            for (; !CancellationTokenSource.IsCancellationRequested;)
            {
                SendDiagram(new RdtDiagram(false, SendIndexPointer,
                    buffer.Length > Config.DiagramMaxLength ? buffer.Slice(0, Config.DiagramMaxLength) : buffer));
                SendIndexPointer = SendIndexPointer.GetNext();
                if (SendQueue.Count == Config.SendRangeLength) Flush(Config.SendRangeLength - 1);

                if (buffer.Length <= Config.DiagramMaxLength) break;
                buffer = buffer.Slice(Config.DiagramMaxLength);
            }

            Flush();
            twr.Dispose();
        }
        /// <inheritdoc/>
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken);
        }
        /// <inheritdoc/>
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Mode == RdtStreamMode.Closed || Mode == RdtStreamMode.Listener) throw new NotSupportedException();
            if (cancellationToken == default) cancellationToken = CancellationTokenSource.Token;
            await using var twr = new Timer(o => WriteTimeoutFlag = true, null, WriteTimeout, -1);

            for (; !cancellationToken.IsCancellationRequested;)
            {
                await SendDiagramAsync(new RdtDiagram(false, SendIndexPointer,
                    buffer.Length > Config.DiagramMaxLength ? buffer.Slice(0, Config.DiagramMaxLength) : buffer));

                SendIndexPointer = SendIndexPointer.GetNext();
                if (SendQueue.Count == Config.SendRangeLength)
                    await FlushCore(cancellationToken, Config.SendRangeLength - 1);

                if (buffer.Length <= Config.DiagramMaxLength) break;
                buffer = buffer.Slice(Config.DiagramMaxLength);
            }

            await FlushCore(cancellationToken, 0);
            await twr.DisposeAsync();
        }
        /// <inheritdoc/>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (Mode == RdtStreamMode.Listener) throw new NotSupportedException();
            if (Mode == RdtStreamMode.Closed) return 0;
            if (cancellationToken == default) cancellationToken = CancellationTokenSource.Token;

            var timeoutFlag = false;
            var cr = 0;
            await using var t = new Timer(o => timeoutFlag = true, null, ReadTimeout, -1);
            for (; !cancellationToken.IsCancellationRequested;)
            {
                for (; ReceiveQueue.IsEmpty;)
                {
                    if (timeoutFlag) throw new TimeoutException();
                    await Task.Delay(SleepTime, cancellationToken);
                }

                if (!ReceiveQueue.TryPeek(out var diagram)) continue;
                if (diagram.Data.Length > buffer.Length)
                {
                    diagram.Data[..buffer.Length].CopyTo(buffer);
                    diagram.Data = diagram.Data[buffer.Length..];
                    cr += buffer.Length;
                    break;
                }

                diagram.Data.CopyTo(buffer);
                cr += diagram.Data.Length;
                ReceiveQueue.TryDequeue(out _);
                if (buffer.Length == diagram.Data.Length) break;
                if (cr > 0 && ReceiveQueue.IsEmpty) break;
                buffer = buffer.Slice(diagram.Data.Length);
            }

            return cr;
        }
        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            switch (Mode)
            {
                case RdtStreamMode.Listener:
                    throw new NotSupportedException();
                case RdtStreamMode.Closed:
                    return 0;
            }

            var timeoutFlag = false;
            var cr = 0;
            using var t = new Timer(o => timeoutFlag = true, null, ReadTimeout, -1);
            for (; ; )
            {
                for (; ReceiveQueue.IsEmpty;)
                {
                    if (timeoutFlag) throw new TimeoutException();
                    Thread.Sleep(10);
                }

                if (!ReceiveQueue.TryPeek(out var diagram)) continue;
                if (diagram.Data.Length > buffer.Length)
                {
                    diagram.Data[..buffer.Length].CopyTo(buffer);
                    diagram.Data = diagram.Data[buffer.Length..];
                    cr += buffer.Length;
                    break;
                }

                diagram.Data.CopyTo(buffer);
                cr += diagram.Data.Length;
                ReceiveQueue.TryDequeue(out _);
                if (buffer.Length == diagram.Data.Length) break;
                buffer = buffer.Slice(diagram.Data.Length);
            }

            return cr;
        }
        /// <inheritdoc/>
        public override void WriteByte(byte value)
        {
            if (Mode == RdtStreamMode.Closed || Mode == RdtStreamMode.Listener) throw new NotSupportedException();
            using var twr = new Timer(o => WriteTimeoutFlag = true, null, WriteTimeout, -1);

            SendDiagram(new RdtDiagram(false, SendIndexPointer,
                new[] { value }));
            SendIndexPointer = SendIndexPointer.GetNext();

            Flush();
            twr.Dispose();
        }
        /// <inheritdoc/>
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken)
        {
            return await ReadAsync(new Memory<byte>(buffer, offset, count), cancellationToken);
        }
        /// <summary>
        /// 从Listener中收取一个Server
        /// </summary>
        /// <returns>收到的Server, null如果任务取消</returns>
        public RdtStream? Accept()
        {
            return AcceptAsync().GetAwaiter().GetResult();
        }
        /// <summary>
        /// 从Listener中收取一个Server
        /// </summary>
        /// <param name="cancellationToken">取消标识符</param>
        /// <returns>收到的Server, null如果任务取消</returns>
        public async Task<RdtStream?> AcceptAsync(CancellationToken cancellationToken = default)
        {
            if (Mode != RdtStreamMode.Listener)
                throw new NotSupportedException();
            if (cancellationToken == default) cancellationToken = CancellationTokenSource.Token;
            for (; !cancellationToken.IsCancellationRequested;)
            {
                for (; AcceptQueue.IsEmpty;) await Task.Delay(100, cancellationToken);

                if (AcceptQueue.TryDequeue(out var r)) return r;
            }

            return null;
        }
        /// <inheritdoc/>
        public override int ReadByte()
        {
            switch (Mode)
            {
                case RdtStreamMode.Listener:
                    throw new NotSupportedException();
                case RdtStreamMode.Closed:
                    return -1;
            }

            var timeoutFlag = false;
            using var t = new Timer(o => timeoutFlag = true, null, ReadTimeout, -1);

            for (; ReceiveQueue.IsEmpty;)
            {
                if (timeoutFlag) throw new TimeoutException();
                Thread.Sleep(10);
            }

            if (!ReceiveQueue.TryPeek(out var diagram) || diagram.Data.Length == 0) return -1;
            var r = diagram.Data[0];
            if (diagram.Data.Length > 1)
            {
                diagram.Data = diagram.Data[1..];
                return r;
            }

            ReceiveQueue.TryDequeue(out _);
            return r;
        }
        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return ReadAsync(buffer, offset, count).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException)
            {
                return 0;
            }

        }
        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }
        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            Write(new ReadOnlySpan<byte>(buffer, offset, count));
        }
        /// <summary>
        /// 销毁流
        /// </summary>
        public override async ValueTask DisposeAsync()
        {
            await CloseAsync();
        }
        /// <summary>
        /// 报文状态
        /// </summary>
        private enum DiagramStatus
        {
            /// <summary>
            /// 已发送
            /// </summary>
            Sent = 0,
            /// <summary>
            /// 已确认
            /// </summary>
            Confirmed = 1
        }
    }
}