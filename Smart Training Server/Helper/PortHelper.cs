namespace Smart_Training_Server.Helper
{
    public static class PortHelper
    {
        private static readonly HashSet<int> UsedPorts = new();
        private static readonly object _lock = new();

        public static int GetAvailablePort()
        {
            lock (_lock)
            {
                int port = 6000;
                while (UsedPorts.Contains(port))
                    port++;

                UsedPorts.Add(port);
                return port;
            }
        }
    }
}
