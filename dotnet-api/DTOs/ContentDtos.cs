namespace Afrobotics.Bit.Api.DTOs
{
    public class IngestVideoDto
    {
        public string Title { get; set; } = string.Empty;
        public string? SourceChannel { get; set; }
        public string StorageKey { get; set; } = string.Empty;
    }
}
