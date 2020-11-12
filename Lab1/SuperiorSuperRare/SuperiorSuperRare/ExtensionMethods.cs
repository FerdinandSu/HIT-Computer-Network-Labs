using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SuperiorSuperRare
{
    public static class ExtensionMethods
    {
        /// <summary>
        ///     转换元组为IPEndPoint
        /// </summary>
        /// <param name="tuple"></param>
        /// <returns></returns>
        public static IPEndPoint AsIpEndPoint(this (string, int) tuple)
        {
            return new IPEndPoint(IPAddress.Parse(tuple.Item1 ?? string.Empty), tuple.Item2);
        }

        /// <summary>
        ///     转换字节数组为ASCII字符串
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string ToAscii(this byte[] bytes)
        {
            return Encoding.ASCII.GetString(bytes);
        }

        /// <summary>
        ///     转换ASCII字符串为字节数组
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static byte[] ToAsciiBytes(this string s)
        {
            return Encoding.ASCII.GetBytes(s);
        }

        /// <summary>
        ///     向流写入字节数组
        /// </summary>
        /// <param name="s"></param>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static Task WriteBytesAsync(this Stream s, byte[] bytes)
        {
            return s.WriteAsync(bytes, 0, bytes.Length);
        }

        /// <summary>
        ///     向流写入字节数组
        /// </summary>
        /// <param name="s"></param>
        /// <param name="bytes"></param>
        /// <param name="t"></param>
        /// <returns></returns>
        public static Task WriteBytesAsync(this Stream s, byte[] bytes, CancellationToken t)
        {
            return s.WriteAsync(bytes, 0, bytes.Length, t);
        }
    }
}