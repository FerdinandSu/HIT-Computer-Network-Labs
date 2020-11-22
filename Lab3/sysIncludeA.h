#include <winsock.h>

#define STUD_FORWARD_TEST_TTLERROR 0
#define STUD_FORWARD_TEST_NOROUTE 0
#define STUD_IP_TEST_TTL_ERROR 0
#define STUD_IP_TEST_DESTINATION_ERROR 0


struct stud_route_msg{
    unsigned int dest;
    unsigned int masklen;
    unsigned int nexthop;
};

