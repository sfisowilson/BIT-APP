namespace Afrobotics.Bit.Api.DTOs
{
    public class IngestVideoDto
    {
        public string Title { get; set; } = string.Empty;
        public string Resolution { get; set; } = "1920x1080 (1080p)";
        public int FrameRate { get; set; } = 50;
        public string Duration { get; set; } = "00:05:00";  // MReq 1: HH:MM:SS
        public string SourceChannel { get; set; } = "Manual Upload";
        public string? StorageKey { get; set; }
        public string? CampaignId { get; set; }  // MReq 10: associate with campaign
    }
}
