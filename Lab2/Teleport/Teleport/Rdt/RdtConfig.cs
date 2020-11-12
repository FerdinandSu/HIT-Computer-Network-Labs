using Newtonsoft.Json;
using PlasticMetal.MobileSuit.Core;

namespace Teleport.Rdt
{
    /// <summary>
    /// 节点类型，决定单向还是双向
    /// </summary>
    public enum RdtPointType
    {
        Receiver = -1,
        Both = 0,
        Sender = 1
    }
    public class RdtConfig
    {
        /// <summary>
        /// 传输并行数
        /// </summary>
        public int Parallel { get; set; } = 60;
        /// <summary>
        /// 刷新频率, 1-1000
        /// </summary>
        public int RefreshRate { get; set; } = 1000;

        /// <summary>
        /// 丢包率1-99，百分比
        /// </summary>
        public int PackageLoss { get; set; } = 0;
        /// <summary>
        /// 节点类型，决定单向还是双向
        /// </summary>
        public RdtPointType Type { get; set; } = RdtPointType.Both;
        /// <summary>
        /// 发送区间长度1-125，为1则为停等协议
        /// </summary>
        public sbyte SendRangeLength { get; set; } = 1;
        /// <summary>
        /// 发送区间长度1-62，不为1则为SR协议
        /// </summary>
        public sbyte ReceiveRangeLength { get; set; } = 1;
        /// <summary>
        /// 最大报文长度，1-65534
        /// </summary>
        public ushort DiagramMaxLength { get; set; } = 1000;
        /// <summary>
        /// 写入超时，为负数则永不超时
        /// </summary>
        public int WriteTimeout { get; set; } = -1;
        /// <summary>
        /// 读取超时，为负数则永不超时
        /// </summary>
        public int ReadTimeout { get; set; } = -1;

        public int SendTimeout { get; set; } = 100;

        public bool Check()
        {
            return SendRangeLength > 0  && SendRangeLength < 126
                && ReceiveRangeLength > 0 && ReceiveRangeLength < 63
                && RefreshRate>=1 &&RefreshRate<=1000;
        }

    }
}
