using System;
using System.Collections.Generic;
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
        Task<bool> DeleteUserAsync(string id);
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

            if (!string.IsNullOrEmpty(dto.Role))
            {
                user.Role = dto.Role;
            }
            if (!string.IsNullOrEmpty(dto.AccountStatus))
            {
                user.AccountStatus = dto.AccountStatus;
            }

            await _userRepository.UpdateAsync(user);
            await _userRepository.SaveChangesAsync();

            return user;
        }

        public async Task<bool> DeleteUserAsync(string id)
        {
            var user = await _userRepository.GetByIdAsync(id);
            if (user == null) return false;

            await _userRepository.DeleteAsync(user);
            await _userRepository.SaveChangesAsync();
            return true;
        }
    }
}
