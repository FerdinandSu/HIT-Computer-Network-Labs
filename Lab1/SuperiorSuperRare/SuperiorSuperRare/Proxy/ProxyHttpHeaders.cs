using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace SuperiorSuperRare.Proxy
{
    /// <summary>
    ///     请求头的ADT
    /// </summary>
    public class ProxyHttpHeaders
    {
        private ProxyHttpHeaders()
        {
        }

        public (string, string)? Authorization { get; private set; }
        public DnsEndPoint Host { get; set; } = new DnsEndPoint("localhost", 80);
        public long ContentLength { get; private set; }
        public string Verb { get; private set; } = "";
        public string HeadersRaw { get; private set; }

        public static async Task<ProxyHttpHeaders?> Parse(Stream headerStream)
        {
            var r = new ProxyHttpHeaders();

            var cache = new MemoryStream();
            var buf = new byte[1];
            var status = 0; //Check \r\n\r\n

            for (;;)
            {
                var rdSize = await headerStream.ReadAsync(buf, 0, 1);
                if (rdSize == 0) break;
                await cache.WriteAsync(buf, 0, rdSize);

                status =
                    buf.ToAscii() == ((status & 1) == 0 ? "\r" : "\n") ? status + 1 : 0;
                if (status == 4) break;
            }

            if (cache.Length == 0) return null;

            var headers =
                cache.ToArray().ToAscii()
                    .Split(new[] {"\r\n"}, StringSplitOptions.RemoveEmptyEntries)
                    .ToList();

            var hostExp = headers
                .Single(s => s.StartsWith("host:", StringComparison.OrdinalIgnoreCase))
                [5..]
                .TrimStart()
                .Split(':');
            r.Host = hostExp.Length switch
            {
                1 => new DnsEndPoint(hostExp[0], 80),
                2 => new DnsEndPoint(hostExp[0], ushort.Parse(hostExp[1])),
                _ => throw new FormatException(string.Join(":", hostExp))
            };
            r.ContentLength = long.Parse(headers
                .SingleOrDefault(s => s.StartsWith("content-length:", StringComparison.OrdinalIgnoreCase))
                ?["content-length:".Length..]
                .TrimStart() ?? "0");
            r.Verb = headers[0].Split(' ').First();

            const string authHeaderStart = "Proxy-Authorization: Basic";
            var authExp = Convert.FromBase64String(
                headers
                    .FirstOrDefault(
                        h =>
                            h.StartsWith(authHeaderStart, StringComparison.OrdinalIgnoreCase))
                    ?[authHeaderStart.Length..]
                    .Trim() ?? string.Empty
            ).ToAscii().Split(":");
            r.Authorization = authExp.Length switch
            {
                1 => (authExp[0], ""),
                2 => (authExp[0], authExp[1]),
                _ => null
            };
            var sb = new StringBuilder();

            foreach (var header in headers
                .Where(h => !h.StartsWith("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase)))
                sb.Append(header).Append("\r\n");

            sb.Append("\r\n");
            r.HeadersRaw = sb.ToString();
            return r;
        }
    }
}