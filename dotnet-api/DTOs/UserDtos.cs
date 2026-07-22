namespace Afrobotics.Bit.Api.DTOs
{
    public class CreateUserDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string Role { get; set; } = "Editor";
        public string AccountStatus { get; set; } = "Active";
    }

    public class UpdateUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string? FullName { get; set; }
        public string? Email { get; set; }
        public string? Role { get; set; }
        public string? AccountStatus { get; set; }
    }

    /// <summary>MReq 9: DTO for requesting a role elevation.</summary>
    public class RoleRequestDto
    {
        public string RequestedRole { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    /// <summary>MReq 9: DTO for admin decision on a role request.</summary>
    public class RoleRequestDecisionDto
    {
        public string Decision { get; set; } = string.Empty; // Approved, Rejected
    }

    /// <summary>DTO for updating notification preferences.</summary>
    public class NotificationPreferencesDto
    {
        public string[] MutedNotifications { get; set; } = Array.Empty<string>();
    }
}
