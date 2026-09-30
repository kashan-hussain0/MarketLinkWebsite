namespace MarketLinkWebsite.Models.ViewModels;

public class CartItemViewModel
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public int QuantityAvailable { get; set; }
    public int FarmerProfileId { get; set; }
    public string FarmerName { get; set; } = string.Empty;
    public string FarmName { get; set; } = string.Empty;
    public string MarketName { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public List<CartFarmMarket> FarmerMarkets { get; set; } = [];
    public decimal LineTotal => UnitPrice * Quantity;
}

public class CartFarmMarket
{
    public int MarketId { get; set; }
    public string MarketName { get; set; } = string.Empty;
    public string MarketDay { get; set; } = string.Empty;
}

public class CartViewModel
{
    public List<CartItemViewModel> Items { get; set; } = [];
    public int ItemCount => Items.Sum(item => item.Quantity);
    public decimal GrandTotal => Items.Sum(item => item.LineTotal);
}
