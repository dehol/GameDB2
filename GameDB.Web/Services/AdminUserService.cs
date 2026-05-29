using GameDB.Application.Options;
using Microsoft.Extensions.Options;

namespace GameDB.Web.Services;

public sealed class AdminUserService(IOptions<AdminOptions> options)
{
    public bool IsAdmin(int userId)
        => options.Value.UserIds.Contains(userId);
}
