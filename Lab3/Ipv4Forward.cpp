/*
* THIS FILE IS FOR IP FORWARD TEST
*/
#include "sysInclude.h"
#include <map>
using namespace std;
// system support
extern void fwd_LocalRcv(char *pBuffer, int length);

extern void fwd_SendtoLower(char *pBuffer, int length, unsigned int nexthop);

extern void fwd_DiscardPkt(char *pBuffer, int type);

extern unsigned int getIpv4Address();

typedef unsigned char Byte;
typedef uint16_t ushort;
typedef uint32_t uint;
typedef uint64_t ulong;

struct RouteInfo
{
	uint DstAddr;
	uint MaskLength;
	uint NextHop;
};

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
		UpdateChecksum();
	}
	/**
     * 更新校验和
     */
	void UpdateChecksum()
	{
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
 * 路由表
 */ 
map<uint, uint> Routes[33];

// implemented by students

void stud_Route_Init()
{
	for (int i = 0; i <= 32;
		 Routes[i++] = map<uint, uint>())
		;
	return;
}
/**
 * 计算目标网络IP
 * 参数: 
 * 	    ipAddr: 目标主机IP
 * 		masklen: 掩码长度
 * 返回值:
 *      true，如果是；反之false
 */
uint mask(uint ipAddr, uint masklen)
{
	uint shiftLen=32-masklen;
	return (ipAddr >>shiftLen) << shiftLen;
}
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
/**
 * 在本地路由表中查询目的地址的路由信息
 * 
 * 参数: 
 * 		ipAddr: 待查询的目的地址
 * 
 * 返回值:
 *      0 ~ UINT_MAX, 查询到的目的地址。
 * 		-1，如果没有查询到。
 */
long route(uint ipAddr)
{
	for (int i = 32; i >= 0; i--)
	{
		map<uint, uint> &myRoute = Routes[i];
		map<uint, uint>::iterator it =
			myRoute.find(mask(ipAddr, i));
		if (it == myRoute.end())
			continue;
		return (long)it->second;
	}
	return -1;
}

int stud_fwd_deal(char *pBuffer, int length)
{
	Ipv4Header *h = (Ipv4Header *)pBuffer;
	if (h->TTL == 0)
	{
		fwd_DiscardPkt(pBuffer, STUD_FORWARD_TEST_TTLERROR);
		return 1;
	}
	uint dst = h->DstAddr;
	if (IsLocal(dst))
	{
		fwd_LocalRcv(pBuffer, length);
		return 0;
	}
	long fwdIp = route(ntohl(dst));
	if (fwdIp == -1)
	{
		fwd_DiscardPkt(pBuffer, STUD_FORWARD_TEST_NOROUTE);
		return 1;
	}
	h->TTL--;
	h->UpdateChecksum();
	fwd_SendtoLower(pBuffer, length, (uint)fwdIp);
	return 0;
}

void stud_route_add(stud_route_msg *proute)
{
	RouteInfo *ri = (RouteInfo *)proute;
	uint dst = ntohl(ri->DstAddr);
	uint msk = ntohl(ri->MaskLength);
	map<uint, uint> &myRoute = Routes[msk];
	if (myRoute.find(dst) != myRoute.end())
		myRoute.erase(myRoute.find(dst));
	myRoute.insert(make_pair(dst, ntohl(ri->NextHop)));

	return;
}