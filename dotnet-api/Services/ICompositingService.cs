using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services
{
    /// <summary>
    /// Abstraction for the compositing engine. Swap implementations to change AI.
    /// Current:  BasicCompositingService (returns asset image)
    /// Future:   RunwayCompositingService : ICompositingService (Runway Gen-4 API)
    ///           Sam2CompositingService  : ICompositingService (SAM2 + IC-Light)
    /// </summary>
    public interface ICompositingService
    {
        Task<CompositedFrame> CompositeAsync(CompositingRequest request);
    }
}
