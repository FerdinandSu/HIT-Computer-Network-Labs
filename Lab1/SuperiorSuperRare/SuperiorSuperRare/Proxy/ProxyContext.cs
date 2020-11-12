#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using PlasticMetal.MobileSuit.Core;
using SuperiorSuperRare.Config;

namespace SuperiorSuperRare.Proxy
{
    /// <summary>
    ///     储存代理上下文的结构
    /// </summary>
    public class ProxyContext : IDisposable
    {
        public ProxyContext(NetworkStream client, GeneralConfig configuration, ILogger logger, CancellationToken token)
        {
            Configuration = configuration;
            Client = client;
            Logger = logger;
            Token = token;
        }

        /// <summary>
        ///     用于取消
        /// </summary>
        public CancellationToken Token { get; }

        /// <summary>
        ///     日志
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        ///     配置
        /// </summary>
        public GeneralConfig Configuration { get; }

        /// <summary>
        ///     用户配置
        /// </summary>
        public UserConfig? UserConfiguration
            => Configuration.Users.Contains(Header?.Authorization?.Item1 ?? "")
                ? Configuration.Users[Header?.Authorization?.Item1 ?? ""]
                : null;

        /// <summary>
        ///     重定向信息
        /// </summary>
        public (string, string)? Redirection { get; set; }

        /// <summary>
        ///     HTTP头
        /// </summary>
        public ProxyHttpHeaders? Header { get; set; }

        /// <summary>
        ///     当前主机地址
        /// </summary>
        public DnsEndPoint? CurrentHostAddress { get; set; }

        /// <summary>
        ///     客户端套接字的流
        /// </summary>
        public NetworkStream Client { get; }

        /// <summary>
        ///     主机套接字的流
        /// </summary>
        public NetworkStream? Host { get; set; }

        public void Dispose()
        {
            Client.Dispose();
            Host?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}