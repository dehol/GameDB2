namespace GameDB.Application.DTOs.Store;

public sealed record StorePriceInfo(
    decimal Price,
    short   Discount,
    string  Currency);
