namespace Afrobotics.Bit.Api.DTOs
{
    public class CreateLogDto
    {
        public string EventCode { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info"; // Info, Warning, Major, Critical
        public string Module { get; set; } = string.Empty;
        public string User { get; set; } = "System";
        public string Description { get; set; } = string.Empty;
    }
}
