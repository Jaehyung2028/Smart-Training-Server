namespace Smart_Training_Server.Models
{
    public class ContentInfo
    {
        public string RootContent { get; set; } = string.Empty;
        public int Depth { get; set; } = 0;
        public int Id { get; set; }
        public int ParentId { get; set; }
        public string Title { get; set; } = string.Empty;

        public string? Path { get; set; }
        public string? LobbyServerExecutablePath { get; set; }

        public List<ContentInfo> Children { get; set; } = new();
    }
}
