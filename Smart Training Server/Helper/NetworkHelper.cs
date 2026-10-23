using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

// 클라이언트에게 안내할 IP를 선택하고 API 시작 시 접속 주소를 출력합니다. 수신 주소와 안내 주소는 서로 다를 수 있습니다.
namespace Smart_Training_Server.Helper
{
    public static class NetworkHelper
    {
        // 0.0.0.0·::처럼 수신 전체 주소를 뜻하는 값인지 확인합니다. 클라이언트에게 안내할 접속 주소로는 사용할 수 없습니다.
        public static bool IsWildcardAddress(string value)
        {
            if (!IPAddress.TryParse(value, out var address))
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
        }

        /// <summary>
        /// 게이트웨이가 있는 LAN IPv4를 우선하며, 사용할 주소가 없으면 루프백을 반환합니다.
        /// </summary>
        public static string GetLocalIPAddress()
        {
            return GetLocalIPv4Addresses().FirstOrDefault() ?? IPAddress.Loopback.ToString();
        }

        // 실행 중인 네트워크 어댑터의 IPv4를 수집합니다. 루프백·터널·자동 할당 주소 등을 제외하고 게이트웨이·물리 연결을 우선합니다.
        public static string[] GetLocalIPv4Addresses()
        {
            var candidates = new List<(string Address, bool Gateway, bool Physical)>();
            NetworkInterface[] adapters;
            try
            {
                adapters = NetworkInterface.GetAllNetworkInterfaces();
            }
            catch (NetworkInformationException)
            {
                return Array.Empty<string>();
            }

            foreach (var adapter in adapters)
            {
                if (adapter.OperationalStatus != OperationalStatus.Up ||
                    adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                try
                {
                    var properties = adapter.GetIPProperties();
                    bool gateway = properties.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !x.Address.Equals(IPAddress.Any) &&
                        !IPAddress.IsLoopback(x.Address));
                    bool physical = adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211;
                    foreach (var unicast in properties.UnicastAddresses)
                    {
                        var ip = unicast.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip))
                        {
                            continue;
                        }

                        var bytes = ip.GetAddressBytes();
                        if (bytes[0] == 0 || bytes[0] >= 224 || (bytes[0] == 169 && bytes[1] == 254))
                        {
                            continue;
                        }

                        candidates.Add((ip.ToString(), gateway, physical));
                    }
                }
                catch (NetworkInformationException)
                { /* 다른 네트워크 장치에서 사용할 주소를 계속 찾습니다. */
                }
            }

            return candidates.OrderByDescending(x => x.Gateway).ThenByDescending(x => x.Physical).ThenBy(x => x.Address, StringComparer.Ordinal).Select(x => x.Address).Distinct().ToArray();
        }

        // 같은 PC용 API 주소와 다른 PC용 주소 후보, Unity 방 안내 주소를 콘솔에 출력합니다.
        public static void WriteConnectionInfo(int apiPort, string advertisedAddress)
        {
            var addresses = GetLocalIPv4Addresses();
            Console.WriteLine($"[접속] 같은 PC의 API 주소: http://localhost:{apiPort}");
            foreach (var address in addresses)
            {
                Console.WriteLine($"[접속] 다른 PC에서 입력할 API 주소 후보: http://{address}:{apiPort}");
            }

            string roomAddress = string.IsNullOrEmpty(advertisedAddress) ? addresses.FirstOrDefault() ?? "127.0.0.1" : advertisedAddress;
            Console.WriteLine($"[접속] 방 서버가 클라이언트에 안내할 주소: {roomAddress}");
            if (addresses.Length > 1 && string.IsNullOrEmpty(advertisedAddress))
            {
                Console.WriteLine("[접속] 주소 후보가 여러 개입니다. 클라이언트와 같은 네트워크의 IP를 Room:AdvertisedAddress로 지정할 수 있습니다.");
            }

            if (addresses.Length == 0 && string.IsNullOrEmpty(advertisedAddress))
            {
                Console.WriteLine("[접속] 사용 가능한 LAN IPv4가 없어 같은 PC에서만 접속할 수 있습니다.");
            }

            Console.WriteLine($"[접속] API는 TCP {apiPort}, 방 서버는 실행 로그에 표시되는 UDP 포트를 사용합니다.");
        }
    }
}
