using System.Collections.Generic;

namespace GameDB.Application.DTOs;

public record class SteamGameData(int appid, string name, int last_modified, int price_change_number);

public record class SteamResponseData(
    List<SteamGameData> apps,
    bool have_more_results,
    int last_appid
);

public record class SteamAppList(SteamResponseData response);
