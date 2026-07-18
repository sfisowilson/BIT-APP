namespace Afrobotics.Bit.Api.DTOs
{
    public class CreateUserDto
    {
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "Editor";
        public string AccountStatus { get; set; } = "Active";
    }

    public class UpdateUserDto
    {
        public string Id { get; set; } = string.Empty;
        public string? Role { get; set; }
        public string? AccountStatus { get; set; }
    }
}
