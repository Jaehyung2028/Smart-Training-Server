namespace Smart_Training_Server.Options
{
    public class LobbyOptions
    {
        public string ServerExecutablePath { get; set; } = "RoomServer/Smart Training Room.exe";
        public int ServerReadyTimeoutSeconds { get; set; } = 60;
        public int MaxCount { get; set; }

        public int NameMinLength { get; set; }

        public int NameMaxLength { get; set; }

        public bool AllowDuplicateName { get; set; }
    }
}
