// 현재 API에서 배정 중인 포트를 기록합니다. 프로세스 종료가 확인되면 해제하여 다시 배정할 수 있습니다. 운영체제 전체의 포트 점유 검사는 별개입니다.
namespace Smart_Training_Server.Helper
{
    public static class PortHelper
    {
        // 현재 방 서버에 배정된 포트 집합입니다. 선택·추가·제거는 모두 같은 잠금으로 보호합니다.
        private static readonly HashSet<int> usedPorts = new();

        // 동시에 같은 포트를 고르지 않도록 선택과 배정을 함께 보호합니다.
        private static readonly object portGate = new();

        // 6000부터 배정 중이 아닌 번호를 골라 기록합니다. 해제한 번호도 다시 사용하며, 다른 프로그램의 점유 확인은 하지 않습니다.
        public static int GetAvailablePort()
        {
            lock (portGate)
            {
                int port = 6000;
                while (port <= 65535 && usedPorts.Contains(port))
                {
                    port++;
                }

                if (port > 65535)
                {
                    throw new InvalidOperationException("배정할 수 있는 방 서버 포트가 없습니다.");
                }

                usedPorts.Add(port);
                return port;
            }
        }

        // 실제 프로세스가 종료됐거나 실행되지 않았음을 확인한 뒤 호출합니다. 이미 해제한 번호는 무시합니다.
        public static void ReleasePort(int port)
        {
            lock (portGate)
            {
                usedPorts.Remove(port);
            }
        }
    }
}
