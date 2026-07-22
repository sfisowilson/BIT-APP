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
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
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

            var expiryHours = double.TryParse(_configuration["Jwt:ExpiryHours"], out var h) ? h : 8;

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
                expires: DateTime.UtcNow.AddHours(expiryHours),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// MReq 8: Refresh an expired (or near-expired) token.
        /// Validates the token signature (ignoring expiry) and issues a new token
        /// if within the configured refresh window, using the embedded user identity.
        /// </summary>
        public async Task<LoginResponseDto?> RefreshTokenAsync(string expiredToken)
        {
            var secret = _configuration["Jwt:Secret"]
                ?? "AFROBOTICS_BIT_SUPER_SECRET_SECURITY_KEY_2026_JWT";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));

            var validationParams = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false, // allow expired tokens within refresh window
                ClockSkew = TimeSpan.Zero
            };

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(expiredToken, validationParams, out var validatedToken);

                var issuedAt = validatedToken.ValidFrom;
                var refreshHours = double.TryParse(_configuration["Jwt:RefreshWindowHours"], out var rh) ? rh : 2;
                if (DateTime.UtcNow > issuedAt.AddHours(refreshHours))
                    return null; // outside refresh window — must re-login

                var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(userId))
                    return null;

                var user = await _userRepository.GetByIdAsync(userId);
                if (user == null || user.AccountStatus != "Active")
                    return null;

                var newToken = GenerateJwtToken(user);
                return new LoginResponseDto
                {
                    Token = newToken,
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
            catch
            {
                return null; // token tampered with or invalid
            }
        }
    }
}
