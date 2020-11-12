using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SuperiorSuperRare.Config;

namespace SuperiorSuperRare.Proxy
{
    /// <summary>
    ///     代理服务器
    /// </summary>
    public class ProxyServer : IDisposable

    {
        public delegate Task ConnectionHandler(NetworkStream clientStream, CancellationToken token);

        public ProxyServer(GeneralConfig config, ConnectionHandler handleClient, CancellationToken token)
        {
            //打开套接字
            Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //绑定套接字
            Socket.Bind(config.Server.AsIpEndPoint());
            //Listen
            Socket.Listen(config.ConnectionLimit);
            //开始标准消息循环
            ListenerTask = AcceptClients(Socket, handleClient, token);
        }

        public Task ListenerTask { get; }
        private Socket Socket { get; }

        public void Dispose()
        {
            if (ListenerTask.Status == TaskStatus.Canceled) ListenerTask.Dispose();
            if (Socket.Connected) Socket.Shutdown(SocketShutdown.Both);
            Socket.Close();
            Socket.Dispose();
        }

        private static async Task AcceptClients(Socket listener, ConnectionHandler handleClient,
            CancellationToken token)
        {
            for (; !token.IsCancellationRequested;)
                //对于收到的每个连接请求进行异步处理
                handleClient(new NetworkStream(await listener.AcceptAsync()), token).GetAwaiter();
        }
    }
}