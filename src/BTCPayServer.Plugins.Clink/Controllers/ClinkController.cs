using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Plugins.Clink.Models;
using BTCPayServer.Plugins.Clink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Clink.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("plugins/{storeId}/clink")]
public class ClinkController : Controller
{
    private readonly ClinkService _clinkService;

    public ClinkController(ClinkService clinkService)
    {
        _clinkService = clinkService;
    }

    [HttpGet("configure")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> Configure(string storeId)
    {
        var settings = await _clinkService.GetSettings(storeId);
        return View(settings ?? new ClinkSettings());
    }

    [HttpPost("configure")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> Configure(string storeId, ClinkSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Noffer) && !settings.Noffer.StartsWith("noffer1"))
        {
            TempData["ErrorMessage"] = "Invalid CLINK offer string. It must start with 'noffer1'.";
            return View(settings);
        }

        await _clinkService.SetSettings(storeId, settings);

        TempData["SuccessMessage"] = "CLINK configuration saved successfully.";

        return RedirectToAction(nameof(Configure), new { storeId });
    }

    [HttpGet("payment-status")]
    public async Task<IActionResult> PaymentStatus(string storeId, string invoiceId)
    {
        var settings = await _clinkService.GetSettings(storeId);
        return Json(new { enabled = settings?.Enabled == true, noffer = settings?.Noffer });
    }

    [HttpGet("validate-noffer")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> ValidateNoffer(string storeId)
    {
        var settings = await _clinkService.GetSettings(storeId);
        if (settings?.Noffer == null)
            return Json(new { valid = false, message = "No noffer configured." });

        var valid = settings.Noffer.StartsWith("noffer1") && settings.Noffer.Length > 60;
        return Json(new
        {
            valid,
            message = valid ? "Offer string looks valid." : "Invalid noffer format. Must start with 'noffer1' and be at least 60 characters."
        });
    }

    [AllowAnonymous]
    [HttpPost("{invoiceId}/notify-payment")]
    public async Task<IActionResult> NotifyPayment(string storeId, string invoiceId, [FromBody] PaymentNotification notification)
    {
        var settings = await _clinkService.GetSettings(storeId);
        if (settings is not { Enabled: true })
            return NotFound();

        await _clinkService.RecordPayment(storeId, invoiceId, notification);

        return Ok(new { status = "recorded" });
    }

    [AllowAnonymous]
    [HttpGet("{invoiceId}/status")]
    public async Task<IActionResult> InvoiceStatus(string storeId, string invoiceId)
    {
        var paid = await _clinkService.IsPaymentRecorded(storeId, invoiceId);
        return Json(new { paid });
    }
}

public class PaymentNotification
{
    public string? InvoiceId { get; set; }
    public string? Bolt11 { get; set; }
    public int AmountSats { get; set; }
    public bool Paid { get; set; }
}
