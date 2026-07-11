namespace BTCPayServer.Plugins.Clink.Models;

public class PaymentNotification
{
    public string? InvoiceId { get; set; }
    public string? Bolt11 { get; set; }
    public int AmountSats { get; set; }
    public bool Paid { get; set; }
}
