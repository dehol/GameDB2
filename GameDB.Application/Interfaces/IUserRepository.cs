using GameDB.Domain.Entities;

namespace GameDB.Application.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int userId);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetBySteamIdAsync(string steamId);
    Task<bool> EmailExistsAsync(string email);
    Task<bool> UsernameExistsAsync(string username);
    Task<User> CreateAsync(User user);
    Task UpdateAsync(User user);
}
