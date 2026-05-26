namespace GameDB.Application.DTOs;

// Відповідь на отримання цін
public class ItadPriceResponse
{
    public string id { get; set; } = null!; // Це ItadUuid
    public List<ItadDeal> deals { get; set; } = new();
}

public class ItadDeal
{
    public ItadShop shop { get; set; } = null!;
    public ItadPrice price { get; set; } = null!;
    public ItadPrice regular { get; set; } = null!;
    public decimal cut { get; set; } // Знижка у відсотках (наприклад 50.0)
    public string url { get; set; } = null!;
}

public class ItadShop
{
    public int id { get; set; }
    public string name { get; set; } = null!;
}

public class ItadPrice
{
    public decimal amount { get; set; }
    public string currency { get; set; } = null!;
}