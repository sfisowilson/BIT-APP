using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services
{
    /// <summary>
    /// Abstraction for the compositing engine. Swap implementations to change AI.
    /// Implementations: OpenCvCompositingService, PikaswapsCompositingService, PlanarWarpCompositingService.
    /// </summary>
    public interface ICompositingService
    {
        Task<CompositedFrame> CompositeAsync(CompositingRequest request);
    }
}
