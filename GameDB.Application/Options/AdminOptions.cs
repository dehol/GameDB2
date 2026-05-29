namespace GameDB.Application.Options;

public sealed class AdminOptions
{
    /// <summary>UserId з таблиці User, яким дозволено доступ до /Admin.</summary>
    public int[] UserIds { get; set; } = [];
}
