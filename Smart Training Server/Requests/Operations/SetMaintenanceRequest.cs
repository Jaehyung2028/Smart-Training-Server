using System.ComponentModel.DataAnnotations;

namespace Smart_Training_Server.Requests.Operations;
public sealed class SetMaintenanceRequest
{
    [Required] public bool? Enabled { get; set; }
    [Required, StringLength(200, MinimumLength = 1)] public string? Reason { get; set; }
    [Required, Range(0, long.MaxValue)] public long? ExpectedRevision { get; set; }
}
