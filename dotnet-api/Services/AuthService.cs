using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUserRepository _userRepository;
        private readonly IConfiguration _configuration;

        public AuthService(IUserRepository userRepository, IConfiguration configuration)
        {
            _userRepository = userRepository;
            _configuration = configuration;
        }

        public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
        {
            var user = await _userRepository.GetByEmailAsync(request.Email);
            if (user == null || user.PasswordHash != request.Password)
            {
                return null;
            }

            if (user.AccountStatus != "Active")
            {
                throw new InvalidOperationException("Account suspended. Contact system administrator.");
            }

            user.LastLoginAt = DateTime.UtcNow;
            // Entity is already tracked from GetByEmailAsync — just save changes
            await _userRepository.SaveChangesAsync();

            // Generate a real JWT token
            var token = GenerateJwtToken(user);

            return new LoginResponseDto
            {
                Token = token,
                User = new UserSessionDto
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role,
                    AccountStatus = user.AccountStatus
                }
            };
        }

        private string GenerateJwtToken(Models.User user)
        {
            var secret = _configuration["Jwt:Secret"]
                ?? "AFROBOTICS_BIT_SUPER_SECRET_SECURITY_KEY_2026_JWT";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role),
                new Claim("fullName", user.FullName),
                new Claim("accountStatus", user.AccountStatus)
            };

            var token = new JwtSecurityToken(
                issuer: null,
                audience: null,
                claims: claims,
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
