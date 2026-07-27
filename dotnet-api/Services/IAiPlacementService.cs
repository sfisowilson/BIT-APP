using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services;

public interface IAiPlacementService
{
    Task<AiPlacementResponse> SuggestPlacementsAsync(AiPlacementRequest request, CancellationToken ct = default);
}
