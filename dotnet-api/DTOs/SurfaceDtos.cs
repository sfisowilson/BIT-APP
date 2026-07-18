namespace Afrobotics.Bit.Api.DTOs
{
    /// <summary>MReq 11: Approval with campaign context and audit trail.</summary>
    public class ApprovalDto
    {
        public string Decision { get; set; } = "Approved";
        public string? RejectionReason { get; set; }
        public string? CampaignId { get; set; }
        public string? UserId { get; set; }
    }
}
