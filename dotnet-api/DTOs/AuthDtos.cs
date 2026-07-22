namespace Afrobotics.Bit.Api.DTOs
{
    public class LoginRequestDto
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public UserSessionDto User { get; set; } = null!;
    }

    public class UserSessionDto
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;
    }

    /// <summary>DTO for token refresh requests (MReq 8).</summary>
    public class TokenRefreshDto
    {
        public string Token { get; set; } = string.Empty;
    }
}
