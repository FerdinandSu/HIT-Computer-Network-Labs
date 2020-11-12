using System;
using System.Collections.Generic;
using System.Text;

namespace Teleport.Rdt
{
    public class RdtDiagram
    {


        /// <summary>
        /// 连接用报文
        /// </summary>
        public static readonly RdtDiagram Hello = new RdtDiagram(0, new byte[0]);

        /// <summary>
        /// 断开用报文
        /// </summary>
        public static readonly RdtDiagram Bye = new RdtDiagram((sbyte)-128, new byte[0]);
        /// <summary>
        /// 数据
        /// </summary>
        public byte[] Data { get; set; }
        /// <summary>
        /// 序列号 一般1-126，特殊0,128
        /// </summary>
        public uint Index { get; }
        /// <summary>
        /// 是否为ACK
        /// </summary>
        public bool IsConfirm { get; }
        /// <summary>
        /// 数据长度
        /// </summary>
        public int Length => Data.Length;
        public RdtDiagram(sbyte index, byte[] data)
        {
            IsConfirm = index < 0;
            Index = (uint)Math.Abs((int)index);
            Data = data;
        }
        public RdtDiagram(bool isConfirm, uint index, byte[] data)
        {
            IsConfirm = isConfirm;
            Index = index;
            Data = data;
        }
        public RdtDiagram(bool isConfirm, uint index, ReadOnlyMemory<byte> data)
        {
            IsConfirm = isConfirm;
            Index = index;
            Data = data.ToArray();
        }
        public RdtDiagram(bool isConfirm, uint index, ReadOnlySpan<byte> data)
        {
            IsConfirm = isConfirm;
            Index = index;
            Data = data.ToArray();
        }

        public static RdtDiagram Parse(byte[] diagram)
        {
            return diagram[0] switch
            {
                0 => Hello,
                128 => Bye,
                _ => new RdtDiagram((sbyte)diagram[0], diagram.Length > 1 ? diagram[1..] : new byte[0])
            };
        }
        /// <summary>
        /// 头部字节
        /// </summary>
        public byte Header => (byte) (IsConfirm ? -(int) Index : (sbyte) Index);

        public byte[] ToBytes()
        {
            if (Data.Length == 0) return new[] {Header };
            var r = new byte[Data.Length + 1];
            Data.CopyTo(r, 1);
            r[0] = Header;
            return r;

        }
        public RdtDiagram GetConfirm(bool isSr)
        {
            if (!IsConfirm) return isSr
                ? new RdtDiagram(true, Index, new byte[1]) 
                : new RdtDiagram(true, Index, new byte[0]);
            throw new NotSupportedException();

        }
        public DateTime? TimeStamp { get; set; }
        public override int GetHashCode()
        {
            return HashCode.Combine(Index, IsConfirm, Data);
        }
        public override bool Equals(object? obj)
        {
            if (obj is RdtDiagram o)
            {
                return o.Index == Index &&
                       o.IsConfirm == IsConfirm &&
                       o.Data == Data;
            }

            return false;
        }
    }
}
