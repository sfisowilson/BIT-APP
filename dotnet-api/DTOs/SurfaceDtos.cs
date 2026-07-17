namespace Afrobotics.Bit.Api.DTOs
{
    public class ApprovalDto
    {
        public string Decision { get; set; } = "Approved"; // Approved or Excluded
        public string? RejectionReason { get; set; }
    }
}
