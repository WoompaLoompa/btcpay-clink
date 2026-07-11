using System;

namespace BTCPayServer.Plugins.Clink.Models;

public class ClinkPaymentData
{
    public string? Noffer { get; set; }
    public int AmountSats { get; set; }
    public string? Bolt11 { get; set; }
    public string? InvoiceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public ClinkPaymentStatus Status { get; set; }
}

public enum ClinkPaymentStatus
{
    Pending,
    InvoiceGenerated,
    Paid,
    Expired,
    Failed
}
