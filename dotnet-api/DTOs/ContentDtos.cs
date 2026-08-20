namespace Afrobotics.Bit.Api.DTOs
{
    public class IngestVideoDto
    {
        public string Title { get; set; } = string.Empty;
        public string Resolution { get; set; } = "1920x1080";
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int FrameRate { get; set; } = 50;
        public string Duration { get; set; } = "00:05:00";
        public string SourceChannel { get; set; } = "Manual Upload";
        public string? StorageKey { get; set; }
        public string? CampaignId { get; set; }
    }

    /// <summary>DTO for transitioning content to a new pipeline stage.</summary>
    public class TransitionStageDto
    {
        /// <summary>Target pipeline stage: Staging, Transcoding, SceneDetecting, Completed, Failed.</summary>
        public string TargetStage { get; set; } = string.Empty;

        /// <summary>Optional error message (used when transitioning to Failed).</summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>Response from the video probe endpoint — ffprobe-extracted metadata.</summary>
    public class VideoProbeResponseDto
    {
        /// <summary>Key to reference the pre-uploaded file when finalising the upload.</summary>
        public string ProbeKey { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Duration { get; set; } = "00:00:00";
        public int Fps { get; set; }
        public string Resolution { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public string Codec { get; set; } = string.Empty;
        public string Container { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }
}
