using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using PlasticMetal.MobileSuit.Core;
using SuperiorSuperRare.Config;

namespace SuperiorSuperRare.Proxy
{
    /// <summary>
    ///     处理一次代理请求的类
    /// </summary>
    public class ProxyHandler
    {
        public delegate Task<Status> ProxyStep(ProxyContext context);

        /// <summary>
        ///     代理状态
        /// </summary>
        public enum Status
        {
            /// <summary>
            ///     初始态
            /// </summary>
            InitState,

            /// <summary>
            ///     已获得头部
            /// </summary>
            HeaderGot,

            /// <summary>
            ///     需要过滤主机
            /// </summary>
            HostDeciding,

            /// <summary>
            ///     连接主机中
            /// </summary>
            Connecting,

            /// <summary>
            ///     已连接主机
            /// </summary>
            Connected,

            /// <summary>
            ///     代理认证完成
            /// </summary>
            Authenticated,

            /// <summary>
            ///     需要代理
            /// </summary>
            ToHttpProxy,

            /// <summary>
            ///     需要HTTPS通道建立
            /// </summary>
            ToHttpsProxy,

            /// <summary>
            ///     正在退出
            /// </summary>
            Exiting
        }

        public ProxyHandler()
        {
            //设定控制流
            NextStep = new Dictionary<Status, ProxyStep>
            {
                {Status.InitState, GetHeaders},
                {Status.HeaderGot, Authenticate},
                {Status.Authenticated, SelectMethod},
                {Status.ToHttpProxy, HttpProxy},
                {Status.ToHttpsProxy, HttpsProxy},
                {Status.HostDeciding, HostFilter},
                {Status.Connecting, ConnectServer},
                {Status.Connected, SelectMethod}
            };
        }

        /// <summary>
        ///     控制流词典
        /// </summary>
        private Dictionary<Status, ProxyStep> NextStep { get; }

        private Queue<Task> LoggingTasks { get; } = new Queue<Task>();

        /// <summary>
        ///     获取请求头部
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public static async Task<Status> GetHeaders(ProxyContext context)
        {
            context.Header = await ProxyHttpHeaders.Parse(context.Client);

            return context.Header != null ? Status.HeaderGot : Status.Exiting;
        }

        /// <summary>
        ///     身份认证
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<Status> Authenticate(ProxyContext context)
        {
            if (context.Header == null) return Status.Exiting;
            if (!context.Configuration.EnableAuthentication) return Status.Authenticated;


            if (context.Configuration.Users.Contains(context.Header.Authorization?.Item1 ?? "") &&
                context.Configuration.Users[context.Header.Authorization?.Item1 ?? ""].Password ==
                context.Header.Authorization?.Item2
            )
            {
                LoggingTasks.Enqueue(context.Logger.LogDebugAsync($"认证成功：{context.Header.Authorization?.Item1}"));
                return Status.Authenticated;
            }


            await context.Client.WriteBytesAsync((
                    "HTTP/1.1 407 Proxy Authentication Required\r\n" +
                    "Proxy-Authenticate: Basic realm=\"Input Proxy Username & Password.\"\r\n" +
                    "Connection: close\r\n\r\n")
                .ToAsciiBytes(), context.Token);
            LoggingTasks.Enqueue(
                context.Logger.LogExceptionAsync($"407认证失败：{context.Header.Authorization?.Item1 ?? "NO_NAME"}"));
            return Status.Exiting;
        }

        /// <summary>
        ///     分流，对于HTTPs单独处理
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<Status> SelectMethod(ProxyContext context)
        {
            if (context.Header == null) return Status.Exiting;
            var s = context.Header.Verb == "CONNECT"
                ? Status.ToHttpsProxy
                : Status.ToHttpProxy;

            if (s == Status.ToHttpProxy && context.Configuration.RejectHttpProxy)
            {
                await context.Client.WriteBytesAsync((
                        "HTTP/1.1 405 Method Not Allowed\r\n" +
                        "Connection: close\r\n\r\n")
                    .ToAsciiBytes(), context.Token);
                LoggingTasks.Enqueue(context.Logger.LogExceptionAsync($"405错误的方法: {context.Header.Verb}"));
                s = Status.Exiting;
            }

            return s;
        }

        /// <summary>
        ///     连接主机
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<Status> ConnectServer(ProxyContext context)
        {
            if (context.Header == null) return Status.Exiting;
            context.Host?.DisposeAsync();

            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(context.Header.Host);
                context.Host = new NetworkStream(socket);
            }
            catch (SocketException e)
            {
                await Send403(context);
                LoggingTasks.Enqueue(context.Logger.LogExceptionAsync(e));
                return Status.Exiting;
            }


            context.CurrentHostAddress = context.Header.Host;
            LoggingTasks.Enqueue(context.Logger.LogDebugAsync($"连接主机：{context.Header.Host}"));
            return Status.Connected;
        }

        /// <summary>
        ///     防护墙和重定向处理
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<Status> HostFilter(ProxyContext context)
        {
            if (context.Header == null) return Status.Exiting;
            var hostName = context.Header.Host.Host;

            if (context.Configuration.EnableRedirection)
                foreach (var rule in context.Configuration.RedirectionRules)
                    rule.Apply(ref hostName);

            if (context.UserConfiguration?.EnableRedirection == true)
                foreach (var rule in context.UserConfiguration.RedirectionRules)
                    rule.Apply(ref hostName);

            if (hostName != context.Header.Host.Host)
            {
                LoggingTasks.Enqueue(context.Logger.LogDebugAsync($"重定向：{context.Header.Host.Host}->{hostName}"));
                context.Redirection = (context.Header.Host.Host, hostName);
                context.Header.Host = new DnsEndPoint(hostName, context.Header.Host.Port);
            }
            else
            {
                context.Redirection = null;
            }

            if (context.Configuration.EnableFireWall
                && context.Configuration.FireWallRules.Any(r => !r.Allows(hostName)) ||
                context.UserConfiguration != null
                && context.UserConfiguration.EnableFireWall &&
                context.UserConfiguration.FireWallRules.Any(r => !r.Allows(hostName)))
            {
                await Send403(context);
                LoggingTasks.Enqueue(context.Logger.LogDebugAsync($"防火墙拒绝访问：{hostName}"));
                return Status.Exiting;
            }


            return Status.Connecting;
        }

        ///
        public async Task Send404(ProxyContext context)
        {
            await context.Client.WriteBytesAsync((
                    "HTTP/1.1 404 Not Found\r\n" +
                    "Connection: close\r\n\r\n")
                .ToAsciiBytes(), context.Token);
            LoggingTasks.Enqueue(context.Logger.LogExceptionAsync($"404未找到:{context.Header?.Host.Host ?? ""}"));
        }

        public async Task Send403(ProxyContext context)
        {
            await context.Client.WriteBytesAsync((
                    "HTTP/1.1 403 Forbidden\r\n" +
                    "Connection: close\r\n\r\n")
                .ToAsciiBytes(), context.Token);
            LoggingTasks.Enqueue(context.Logger.LogExceptionAsync($"403禁止:{context.Header?.Host.Host ?? ""}"));
        }

        /// <summary>
        ///     HTTP代理
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<Status> HttpProxy(ProxyContext context)
        {
            if (context.Header == null) return Status.Exiting;
            if (context.CurrentHostAddress == null || !Equals(context.Header.Host, context.CurrentHostAddress))
                return Status.HostDeciding;

            if (context.Host == null)
            {
                await Send404(context);
                return Status.Exiting;
            }

            //异步转发主机收到的数据到客户端
            Forward(context.Host, context.Client, context).GetAwaiter();


            var buffer = new byte[context.Configuration.BufferSize];
            int bytesRead;

            //转发客户端的数据到主机
            do
            {
                context.Header ??= await ProxyHttpHeaders.Parse(context.Client); //刷新请求头
                if (context.Header == null) return Status.Exiting;
                //主机更换，需要重新连接
                if (context.CurrentHostAddress == null || !Equals(context.Header.Host, context.CurrentHostAddress))
                    return Status.HostDeciding;

                var header = (context.Redirection == null
                        ? context.Header.HeadersRaw
                        : context.Header.HeadersRaw.Replace(context.Redirection?.Item1 ?? "!",
                            context.Redirection?.Item2))
                    .ToAsciiBytes();

                //转发请求头
                await context.Host?.WriteBytesAsync
                    (header, context.Token)!;
                //转发请求体 && 其它请求头等等
                bytesRead = header.Length;
                if (context.Header.ContentLength > 0)
                {
                    var cl = context.Header.ContentLength;
                    do
                    {
                        bytesRead = await context.Client.ReadAsync(buffer, 0,
                            (int) Math.Min(cl, context.Configuration.BufferSize), context.Token);
                        await context.Host.WriteAsync(buffer, 0, bytesRead, context.Token);
                        cl -= bytesRead;
                    } while (bytesRead > 0 && cl > 0);
                }

                context.Header = null;
            } while (bytesRead > 0);

            return Status.Exiting;
        }

        /// <summary>
        ///     连接HTTPS
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task<Status> HttpsProxy(ProxyContext context)
        {
            if (context.Header == null) return Status.Exiting;
            if (context.CurrentHostAddress == null || !Equals(context.Header.Host, context.CurrentHostAddress))
                return Status.HostDeciding;

            if (context.Host == null)
            {
                await Send404(context);
                return Status.Exiting;
            }

            //双向转发
            var task = Task.WhenAny(
                Forward(context.Client, context.Host, context),
                Forward(context.Host, context.Client, context));
            await context.Client.WriteBytesAsync("HTTP/1.1 200 Connection established\r\n\r\n"
                .ToAsciiBytes(), context.Token);
            await task;

            return Status.Exiting;
        }

        private static async Task Forward(Stream source, Stream destination, ProxyContext context)
        {
            var buffer = new byte[context.Configuration.BufferSize];

            try
            {
                int bytesRead;
                do
                {
                    bytesRead = await source.ReadAsync(buffer, 0, context.Configuration.BufferSize, context.Token);
                    if (!destination.CanWrite) break;
                    await destination.WriteAsync(buffer, 0, bytesRead, context.Token);
                } while (bytesRead > 0 && source.CanRead && !context.Token.IsCancellationRequested);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        //启动
        public async Task Run(NetworkStream client, GeneralConfig configuration, ILogger logger,
            CancellationToken token)
        {
            var result = Status.InitState;

            using var context = new ProxyContext(client, configuration, logger, token);
            do
            {
                try
                {
                    result = await NextStep[result](context);
                }
                catch (SocketException)
                {
                    result = Status.Exiting;
                }
            } while (result != Status.Exiting);

            while (LoggingTasks.Count > 0) await LoggingTasks.Dequeue();
        }
    }
}