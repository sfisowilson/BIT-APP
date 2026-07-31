namespace Afrobotics.Bit.Api.DTOs;

/// <summary>A single shot within a scene, for shot-boundary UI markers and shot-aware tracking.</summary>
public class ShotDto
{
    public string Id { get; set; } = string.Empty;
    public int ShotIndex { get; set; }
    public int StartFrame { get; set; }
    public int EndFrame { get; set; }
    public double KeyframeTimestamp { get; set; }

    /// <summary>Relative URL to the shot's keyframe JPEG, or null if not extracted.</summary>
    public string? KeyframeUrl { get; set; }
}
