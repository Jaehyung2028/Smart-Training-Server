namespace Smart_Training_Server.Options;

public sealed class OperationsOptions
{
    public bool OpenBrowserOnStart { get; set; } = true;
    public bool PersistHistory { get; set; } = true;
    public string HistoryPath { get; set; } = "Data/operations-history.json";
}
