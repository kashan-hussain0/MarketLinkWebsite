namespace MarketLinkWebsite.Models.Enums;

public enum OrderStatus
{
    Pending,
    Accepted,
    Preparing,
    ReadyForPickup,
    Completed,
    Declined,
    Cancelled
}

public enum FarmerStatus
{
    PendingApproval,
    Active,
    Suspended,
    Rejected
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded
}

public enum UnitType
{
    Kg,
    Gram,
    Litre,
    Piece,
    Dozen,
    Bundle
}

public enum NotificationType
{
    Order,
    Account,
    Review,
    System,
    Announcement
}
