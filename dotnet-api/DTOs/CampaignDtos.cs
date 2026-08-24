namespace Afrobotics.Bit.Api.DTOs
{
    public class CreateCampaignDto
    {
        public string Name { get; set; } = string.Empty;
        // Deprioritized per client request — optional, no longer validated/required.
        public string? NamingStructureCode { get; set; }
        public string? TargetRegion { get; set; }
        public decimal? TotalBudget { get; set; }
    }

    public class UpdateCampaignDto
    {
        public string? Name { get; set; }
        public string? NamingStructureCode { get; set; }
        public string? TargetRegion { get; set; }
        public decimal? TotalBudget { get; set; }
        public string? Status { get; set; }
    }
}
