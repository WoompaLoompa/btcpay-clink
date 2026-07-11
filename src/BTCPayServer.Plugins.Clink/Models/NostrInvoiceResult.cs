namespace BTCPayServer.Plugins.Clink.Models;

public class NostrInvoiceResult
{
    public string Bolt11 { get; set; } = "";
    public string EventId { get; set; } = "";
    public string FromPub { get; set; } = "";
    public string PrivkeyHex { get; set; } = "";
}

public class NostrCheckResult
{
    public bool Paid { get; set; }
}

public class NostrPayResult
{
    public string Res { get; set; } = "";
    public string? Preimage { get; set; }
}

public class NostrErrorResult
{
    public string Error { get; set; } = "";
}

public class NdebitStoreRequest
{
    public string InvoiceId { get; set; } = "";
    public string Ndebit { get; set; } = "";
    public string? BuyerEmail { get; set; }
    public string? StoreId { get; set; }
}

public class NdebitSetupModel
{
    public string? StoreId { get; set; }
    public string? Email { get; set; }
}
