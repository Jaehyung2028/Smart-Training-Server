using System.Net;
using System.Net.Sockets;

namespace Smart_Training_Server.Helper
{
    public static class NetworkHelper
    {
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
