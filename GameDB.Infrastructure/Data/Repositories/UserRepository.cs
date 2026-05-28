using GameDB.Application.Interfaces;
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _db;

    public UserRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<User?> GetByIdAsync(int userId) =>
        _db.Users.FirstOrDefaultAsync(u => u.UserId == userId);

    public Task<User?> GetByEmailAsync(string email) =>
        _db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());

    public Task<User?> GetBySteamIdAsync(string steamId) =>
        _db.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);

    public Task<bool> EmailExistsAsync(string email) =>
        _db.Users.AnyAsync(u => u.Email == email.ToLowerInvariant());

    public Task<bool> UsernameExistsAsync(string username) =>
        _db.Users.AnyAsync(u => u.Username == username);

    public async Task<User> CreateAsync(User user)
    {
        // Email завжди в нижньому регістрі
        if (user.Email is not null)
            user.Email = user.Email.ToLowerInvariant();

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        if (user.Email is not null)
            user.Email = user.Email.ToLowerInvariant();

        _db.Users.Update(user);
        await _db.SaveChangesAsync();
    }
}
