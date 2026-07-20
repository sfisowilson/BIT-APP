namespace Afrobotics.Bit.Api.DTOs
{
    public class CreateAssetDto
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "Image"; // Image, Logo, Video
        public string BrandCategory { get; set; } = string.Empty;
        public string? CampaignId { get; set; }  // MReq 10: associate asset with a campaign
    }

    public class UpdateAssetDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? BrandCategory { get; set; }
        public string? CampaignId { get; set; }
    }

    public class AssociateAssetDto
    {
        public string AssetId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
    }
}
