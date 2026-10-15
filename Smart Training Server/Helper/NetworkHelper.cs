using System.Net;
using System.Net.Sockets;

namespace Smart_Training_Server.Helper
{
    public static class NetworkHelper
    {
        public static bool IsWildcardAddress(string value)
        {
            if (!IPAddress.TryParse(value, out var address)) return false;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
        }

        /// <summary>
        /// IPv4 로컬 IP 주소를 반환합니다.
        /// </summary>
        public static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());

            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }

            throw new Exception("IPv4 주소를 찾을 수 없습니다.");
        }
    }
}

