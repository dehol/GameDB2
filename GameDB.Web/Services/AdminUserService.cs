namespace GameDB.Web.Services;

public sealed class AdminUserService
{
    private readonly IConfiguration _configuration;
    public AdminUserService(IConfiguration configuration)
    {
        _configuration = configuration;
    }
    
    public bool IsAdmin(int userId)
    {
        var ids = _configuration
            .GetSection("Admin:UserIds")
            .Get<int[]>() ?? [];

        return ids.Contains(userId);
    }
}
