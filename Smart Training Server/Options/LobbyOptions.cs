namespace Smart_Training_Server.Options
{
    public class LobbyOptions
    {
        public int MaxCount { get; set; }

        public int NameMinLength { get; set; }

        public int NameMaxLength { get; set; }

        public bool AllowDuplicateName { get; set; }
    }
}
