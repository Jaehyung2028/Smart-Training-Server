using System.ComponentModel.DataAnnotations;
namespace Smart_Training_Server.Requests.Operations;
public sealed class SetShutdownRequest
{
    // Null explicitly means cancel/reopen. Cancellation must not depend on a client clock.
    public DateTimeOffset? AtUtc { get; set; }
    [Required, StringLength(200, MinimumLength = 1)] public string? Reason { get; set; }
    [Required, Range(0, long.MaxValue)] public long? ExpectedRevision { get; set; }
}
