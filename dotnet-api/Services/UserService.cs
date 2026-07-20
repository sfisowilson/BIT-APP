using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Afrobotics.Bit.Api.DTOs;
using Afrobotics.Bit.Api.Models;
using Afrobotics.Bit.Api.Repositories;

namespace Afrobotics.Bit.Api.Services
{
    public interface IUserService
    {
        Task<IEnumerable<User>> GetUsersAsync();
        Task<User> CreateUserAsync(CreateUserDto dto);
        Task<User?> UpdateUserAsync(UpdateUserDto dto);
        Task<(bool Success, string? Error)> DeleteUserAsync(string id, string requestingUserId);
    }

    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;

        public UserService(IUserRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public async Task<IEnumerable<User>> GetUsersAsync()
        {
            return await _userRepository.GetAllAsync();
        }

        public async Task<User> CreateUserAsync(CreateUserDto dto)
        {
            // Check for duplicate email
            var existing = await _userRepository.GetByEmailAsync(dto.Email);
            if (existing != null)
            {
                throw new ArgumentException("A user with this email address already exists.");
            }

            var user = new User
            {
                Id = "usr-" + Guid.NewGuid().ToString().Substring(0, 4),
                FullName = dto.FullName,
                Email = dto.Email,
                PasswordHash = dto.Email, // simple default password = email (dev only)
                Role = dto.Role,
                AccountStatus = dto.AccountStatus,
                LastLoginAt = DateTime.UtcNow
            };

            await _userRepository.AddAsync(user);
            await _userRepository.SaveChangesAsync();

            return user;
        }

        public async Task<User?> UpdateUserAsync(UpdateUserDto dto)
        {
            var user = await _userRepository.GetByIdAsync(dto.Id);
            if (user == null) return null;

            // ─── Validate ALL constraints BEFORE any modifications ───

            // Check for duplicate email if email is being changed
            if (!string.IsNullOrEmpty(dto.Email) && dto.Email != user.Email)
            {
                var existingByEmail = await _userRepository.GetByEmailAsync(dto.Email);
                if (existingByEmail != null && existingByEmail.Id != user.Id)
                {
                    throw new ArgumentException("A user with this email address already exists.");
                }
            }

            // Prevent removing the last admin's role
            if (!string.IsNullOrEmpty(dto.Role) && dto.Role != user.Role)
            {
                // Primary admin account is permanently protected — can never be demoted
                if (user.Email == "admin@afrobotics.co.za" && dto.Role != "Admin")
                {
                    throw new InvalidOperationException(
                        "The primary system administrator account (admin@afrobotics.co.za) cannot be demoted. This account must always retain the Admin role.");
                }

                if (user.Role == "Admin" && dto.Role != "Admin")
                {
                    var allUsers = await _userRepository.GetAllAsync();
                    var adminCount = allUsers.Count(u => u.Role == "Admin");
                    if (adminCount <= 1)
                    {
                        throw new InvalidOperationException(
                            "Cannot change the role of the last remaining administrator. At least one admin must exist at all times.");
                    }
                }
            }

            // Prevent suspending the last active admin
            if (!string.IsNullOrEmpty(dto.AccountStatus) && dto.AccountStatus != user.AccountStatus)
            {
                // Primary admin account can never be suspended
                if (user.Email == "admin@afrobotics.co.za" && dto.AccountStatus != "Active")
                {
                    throw new InvalidOperationException(
                        "The primary system administrator account (admin@afrobotics.co.za) cannot be suspended.");
                }

                if (dto.AccountStatus != "Active" && user.Role == "Admin")
                {
                    // If the role is also being changed away from Admin, we need to check using current role
                    var effectiveRole = !string.IsNullOrEmpty(dto.Role) ? dto.Role : user.Role;
                    if (effectiveRole == "Admin")
                    {
                        var allUsers = await _userRepository.GetAllAsync();
                        var activeAdminCount = allUsers.Count(u => u.Role == "Admin" && u.AccountStatus == "Active");
                        if (activeAdminCount <= 1 && user.AccountStatus == "Active")
                        {
                            throw new InvalidOperationException(
                                "Cannot suspend the last active administrator. At least one active admin must exist at all times.");
                        }
                    }
                }
            }

            // ─── All validations passed — apply changes ───

            if (!string.IsNullOrEmpty(dto.Email) && dto.Email != user.Email)
            {
                user.Email = dto.Email;
            }

            if (!string.IsNullOrEmpty(dto.FullName))
            {
                user.FullName = dto.FullName;
            }

            if (!string.IsNullOrEmpty(dto.Role) && dto.Role != user.Role)
            {
                user.Role = dto.Role;
            }

            if (!string.IsNullOrEmpty(dto.AccountStatus) && dto.AccountStatus != user.AccountStatus)
            {
                user.AccountStatus = dto.AccountStatus;
            }

            // Entity is already tracked from GetByIdAsync, just save changes
            await _userRepository.SaveChangesAsync();

            return user;
        }

        public async Task<(bool Success, string? Error)> DeleteUserAsync(string id, string requestingUserId)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) return (false, "User not found.");

            // Prevent self-deletion
            if (user.Id == requestingUserId)
            {
                return (false, "You cannot delete your own account.");
            }

            // Prevent deletion of the primary system administrator account
            if (user.Email == "admin@afrobotics.co.za")
            {
                return (false, "The primary system administrator account (admin@afrobotics.co.za) cannot be deleted. This account is permanently protected.");
            }

            // Prevent deleting the last admin
            if (user.Role == "Admin")
            {
                var allUsers = await _userRepository.GetAllAsync();
                var adminCount = allUsers.Count(u => u.Role == "Admin");
                if (adminCount <= 1)
                {
                    return (false, "Cannot delete the last remaining administrator. At least one admin must exist at all times.");
                }
            }

            await _userRepository.DeleteAsync(user);
            // Entity is tracked from GetByIdAsync — DeleteAsync marks it for removal, then save
            await _userRepository.SaveChangesAsync();
            return (true, null);
        }
    }
}
