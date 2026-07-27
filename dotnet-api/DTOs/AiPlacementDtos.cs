namespace Afrobotics.Bit.Api.DTOs;

public class AiPlacementRequest
{
    public string Prompt { get; set; } = string.Empty;
    public string ContentId { get; set; } = string.Empty;
    public string SceneId { get; set; } = string.Empty;
    public List<AiPlacementSurface> Surfaces { get; set; } = new();
    public List<AiPlacementAsset> Assets { get; set; } = new();
}

public class AiPlacementSurface
{
    public string Id { get; set; } = string.Empty;
    public string SurfaceType { get; set; } = string.Empty;
    public double ConfidenceScore { get; set; }
}

public class AiPlacementAsset
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BrandCategory { get; set; } = string.Empty;
}

public class AiPlacementResponse
{
    public List<AiPlacementPair> Placements { get; set; } = new();
    public string Explanation { get; set; } = string.Empty;
    public string ModelUsed { get; set; } = string.Empty;
}

public class AiPlacementPair
{
    public string SurfaceId { get; set; } = string.Empty;
    public string AssetId { get; set; } = string.Empty;
    public string Reasoning { get; set; } = string.Empty;
}
