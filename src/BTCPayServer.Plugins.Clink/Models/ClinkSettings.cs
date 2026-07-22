using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Clink.Models;

public class ClinkSettings
{
    [Display(Name = "Enable CLINK Payments")]
    public bool Enabled { get; set; }

    [Display(Name = "CLINK Offer String")]
    public string? Noffer { get; set; }

    [Display(Name = "Title")]
    public string Title { get; set; } = "Lightning (CLINK)";

    [Display(Name = "Description")]
    public string Description { get; set; } = "Pay with your Lightning wallet via the CLINK protocol.";

    [Display(Name = "Invoice Timeout (seconds)")]
    public int InvoiceTimeout { get; set; } = 600;

    [Display(Name = "Poll Interval (ms)")]
    public int PollInterval { get; set; } = 5000;

    [Display(Name = "Additional Nostr Relays (one per line)")]
    public string? AdditionalRelays { get; set; }

    [Display(Name = "Webhook Secret (optional)")]
    public string? WebhookSecret { get; set; }
}
