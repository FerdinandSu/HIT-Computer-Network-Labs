using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Teleport.Rdt;

namespace Teleport
{
    public static class ExtensionMethods
    {

        private static Random Random { get; } = new Random();

        public static uint GetLast(this uint @this)
        {
            return (@this + 124) % 126 + 1;
        }
        public static uint GetNext(this uint @this)
        {
            return @this % 126 + 1;
        }
        public static RdtDiagram ToDiagram(this byte[] @this)
        {
            return Rdt.RdtDiagram.Parse(@this);
        }

        public static bool WillLose(this RdtConfig config)
        {
            return Random.Next(100) < config.PackageLoss;
        }
        /// <summary>
        /// this是否在环上的某个区间
        /// </summary>
        /// <param name="this"></param>
        /// <param name="leftEdge"></param>
        /// <param name="rangeLength"></param>
        /// <returns></returns>
        public static bool IsInRange(this uint @this, uint leftEdge, sbyte rangeLength)
        {
            return (@this >= leftEdge && @this < leftEdge + rangeLength)
                   || @this >= 1 && @this + 126 < leftEdge + rangeLength;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="this"></param>
        /// <param name="leftEdge"></param>
        /// <param name="rangeLength"></param>
        /// <returns></returns>
        public static bool IsInLastRange(this uint @this, uint leftEdge, sbyte rangeLength)
        {
            return IsInRange(@this, (leftEdge + 126 - (uint)rangeLength - 1) % 126 + 1, rangeLength);
        }
        /// <summary>
        /// 判断某个数是否在1-126环的全闭区间中
        /// </summary>
        /// <param name="this"></param>
        /// <param name="leftEdge"></param>
        /// <param name="rightEdge"></param>
        /// <returns></returns>
        public static bool IsBetween(this uint @this, uint leftEdge, uint rightEdge)
        {
            return leftEdge <= rightEdge ? @this >= leftEdge && @this <= rightEdge : 
                (@this >= 1 && @this <= rightEdge)|| (@this>=leftEdge && @this<=126);
        }
        /// <summary>
        /// 转换字节数组为ASCII字符串
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string ToAscii(this byte[] bytes)
            => Encoding.ASCII.GetString(bytes);
        /// <summary>
        /// 转换ASCII字符串为字节数组
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static byte[] ToAsciiBytes(this string s)
            => Encoding.ASCII.GetBytes(s);
        /// <summary>
        /// 向流写入字节数组
        /// </summary>
        /// <param name="s"></param>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static Task WriteBytesAsync(this Stream s, byte[] bytes)
            => s.WriteAsync(bytes, 0, bytes.Length);
        /// <summary>
        /// 向流写入字节数组
        /// </summary>
        /// <param name="s"></param>
        /// <param name="bytes"></param>
        /// <param name="t"></param>
        /// <returns></returns>
        public static Task WriteBytesAsync(this Stream s, byte[] bytes, CancellationToken t)
            => s.WriteAsync(bytes, 0, bytes.Length, t);
    }
}
