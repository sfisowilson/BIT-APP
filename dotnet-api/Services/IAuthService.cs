using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;

namespace Afrobotics.Bit.Api.Services
{
    public interface IAuthService
    {
        Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);
    }
}
