/*
* THIS FILE IS FOR IP TEST
*/
// system support
#include "sysInclude.h"

extern void ip_DiscardPkt(char *pBuffer, int type);

extern void ip_SendtoLower(char *pBuffer, int length);

extern void ip_SendtoUp(char *pBuffer, int length);

extern unsigned int getIpv4Address();

// implemented by students

typedef unsigned char Byte;
typedef uint16_t ushort;
typedef uint32_t uint;
typedef uint64_t ulong;

struct Ipv4Header
{
    Byte HeaderLength : 4;
    Byte Version : 4;
    Byte TOS;
    ushort Length;
    ushort Identification;
    ushort FragmentInfo; //分片信息与本实验无关，不做特殊处理
    Byte TTL;
    Byte Protocol;
    ushort HeaderChecksum;
    uint SrcAddr;
    uint DstAddr;

public:
    /**
     * 构造器
     * 
     * length: 报文总长
     * srcAddr：源地址
     * dstAddr: 目的地址
     * protocol：协议
     * ttl：TTL
     */
    Ipv4Header(ushort length, uint srcAddr, uint dstAddr, Byte protocol, Byte ttl)
    {
        memset(this, 0, sizeof(Ipv4Header));
        Version = 4;
        HeaderLength = 5;
        Length = htons(length);
        TTL = ttl;
        Protocol = protocol;
        SrcAddr = htonl(srcAddr);
        DstAddr = htonl(dstAddr);
        HeaderChecksum = htons(GetHeaderCheckSum());
    }
    /**
     * 检查校验和是否错误
     * 
     * 返回值:
     *      true: 校验和正确
     *      false: 校验和出错
     */
    bool Check()
    {
        return HeaderChecksum == htons(GetHeaderCheckSum());
    }

private:
    /**
     * 构造器
     */
    Ipv4Header()
    {
        memset(this, 0, sizeof(Ipv4Header));
    }
    /**
     * 计算校验和
     * 
     * 返回值:
     *      校验和，已经取反
     */
    ushort GetHeaderCheckSum()
    {
        Byte *h8 = (Byte *)this;
        uint buf = 0;
        for (int i = 0; i < 10; i++)
        {
            if (i != 5) //跳过校验和字段
            {
                buf += h8[i << 1] << 8;
                buf += h8[(i << 1) + 1];
            }
        }
        for (; buf >> 16;) //反码加法
        {
            buf = (buf & 0xffff) + (buf >> 16);
        }

        return ~(ushort)buf;
    }
};
/**
 * 判断目标包是否是发往本机的
 * 
 * 返回值:
 *      true，如果是；反之false
 */
bool IsLocal(uint addr)
{
    uint localAddr = getIpv4Address();
    uint dst = ntohl(addr);
    if (localAddr == dst || dst == ~0u)
        return true;
    for (uint i = 1; i != ~0u; i = (i << 1) + 1)
    {
        if ((localAddr & i) == dst)
            return true;
    }
    return false;
}

int stud_ip_recv(char *pBuffer, unsigned short length)
{
    Ipv4Header *h = (Ipv4Header *)pBuffer;
    if (h->Version != 4)
    {
        ip_DiscardPkt(pBuffer, STUD_IP_TEST_VERSION_ERROR);
        return 1;
    }
    ushort realHeaderLength = ((ushort)h->HeaderLength) << 2;
    if (realHeaderLength < 20 || realHeaderLength > length)
    {
        ip_DiscardPkt(pBuffer, STUD_IP_TEST_HEADLEN_ERROR);
        return 1;
    }
    if (h->TTL == 0)
    {
        ip_DiscardPkt(pBuffer, STUD_IP_TEST_TTL_ERROR);
        return 1;
    }

    if (!IsLocal(h->DstAddr))
    {
        ip_DiscardPkt(pBuffer, STUD_IP_TEST_DESTINATION_ERROR);
        return 1;
    }
    if (!h->Check())
    {
        ip_DiscardPkt(pBuffer, STUD_IP_TEST_CHECKSUM_ERROR);
        return 1;
    }

    ip_SendtoUp(pBuffer, length);
    return 0;
}

int stud_ip_Upsend(char *pBuffer, unsigned short len, unsigned int srcAddr,
                   unsigned int dstAddr, char protocol, char ttl)
{
    char *buf = new char[len + 20];
    Ipv4Header *header = (Ipv4Header *)buf;
    *header = Ipv4Header(len + 20, srcAddr, dstAddr, protocol, ttl);
    memcpy(buf + 20, pBuffer, len);
    ip_SendtoLower(buf, len + 20);
    //内存回收
    delete buf;
    return 0;
}