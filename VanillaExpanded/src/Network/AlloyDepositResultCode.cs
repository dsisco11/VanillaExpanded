namespace VanillaExpanded.Network;

public enum AlloyDepositResultCode : int
{
    Success = 0,
    InvalidRequest = 1,
    InventoryClosed = 2,
    InvalidRecipe = 3,
    InsufficientItems = 4,
    InsufficientSpace = 5,
    TransferFailed = 6
}
