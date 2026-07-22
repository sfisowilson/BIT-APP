using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Afrobotics.Bit.Api.Migrations
{
    /// <summary>
    /// Upgrades any remaining plaintext passwords to BCrypt hashes.
    /// Checks if PasswordHash does not start with '$2' (the BCrypt prefix),
    /// and if it matches a known seed password, replaces it with its BCrypt equivalent.
    /// Unknown plaintext passwords are left as-is (admin must reset them).
    /// </summary>
    public partial class UpgradePasswordsToBcrypt : Migration
    {
        private static readonly (string Plaintext, string Bcrypt)[] KnownPasswords =
        {
            ("admin123",      BCrypt.Net.BCrypt.HashPassword("admin123")),
            ("editor123",     BCrypt.Net.BCrypt.HashPassword("editor123")),
            ("advertiser123", BCrypt.Net.BCrypt.HashPassword("advertiser123")),
        };

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (plaintext, bcrypt) in KnownPasswords)
            {
                migrationBuilder.Sql(
                    $"UPDATE \"Users\" SET \"PasswordHash\" = '{bcrypt.Replace("'", "''")}' " +
                    $"WHERE \"PasswordHash\" = '{plaintext}' " +
                    $"AND \"PasswordHash\" NOT LIKE '$2%'");
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Cannot reverse BCrypt — passwords are intentionally one-way.
            // Admins must issue new passwords if rollback is needed.
        }
    }
}
