namespace Afrobotics.Bit.Api.DTOs
{
    public class CreateRenderDto
    {
        public string ContentId { get; set; } = string.Empty;
        public string SurfaceId { get; set; } = string.Empty;
        public string CampaignId { get; set; } = string.Empty;
        public string AssetId { get; set; } = string.Empty;
        public string? ExportPreset { get; set; }
    }
}
