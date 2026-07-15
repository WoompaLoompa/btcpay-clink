using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Clink.Models;
using BTCPayServer.Plugins.Clink.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("plugins/{storeId}/clink")]
public class ClinkController : Controller
{
    private readonly ClinkService _clinkService;
    private readonly NdebitRegistry _ndebitRegistry;
    private readonly NostrEventStore _store;
    private readonly ClinkNostrBridge _bridge;
    private readonly EmailNdebitStore _emailNdebitStore;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<ClinkController> _logger;

    public ClinkController(ClinkService clinkService, NdebitRegistry ndebitRegistry, NostrEventStore store, ClinkNostrBridge bridge, EmailNdebitStore emailNdebitStore, ApplicationDbContext db, ILogger<ClinkController> logger)
    {
        _clinkService = clinkService;
        _ndebitRegistry = ndebitRegistry;
        _store = store;
        _bridge = bridge;
        _emailNdebitStore = emailNdebitStore;
        _db = db;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("~/clink/store-ndebit")]
    public async Task<IActionResult> StoreNdebit([FromBody] NdebitStoreRequest request)
    {
        if (string.IsNullOrEmpty(request?.Ndebit))
            return BadRequest(new { error = "ndebit is required" });

        // Store ndebit by buyer email for subscription auto-pay
        if (!string.IsNullOrEmpty(request.BuyerEmail) && !string.IsNullOrEmpty(request.StoreId))
        {
            await _emailNdebitStore.Set(request.StoreId, request.BuyerEmail, request.Ndebit);
            _logger.LogInformation("StoreNdebit: stored ndebit for email {Email} in store {StoreId}",
                request.BuyerEmail, request.StoreId);
        }

        // Auto-pay the invoice if we have an invoiceId and Bolt11
        if (!string.IsNullOrEmpty(request.InvoiceId))
        {
            _ndebitRegistry.Store(request.InvoiceId, request.Ndebit);
            var data = _store.GetByBtcpayInvoiceId(request.InvoiceId);
            if (data != null)
            {
                try
                {
                    await _bridge.PayInvoice(request.Ndebit, data.Bolt11, data.AmountSats);
                    _store.MarkPaidByBtcpayInvoice(request.InvoiceId);
                    return Ok(new { status = "paid" });
                }
                catch (System.Exception ex)
                {
                    return Ok(new { status = "stored", autoPayError = ex.Message });
                }
            }
        }

        return Ok(new { status = "stored" });
    }

    [AllowAnonymous]
    [HttpGet("~/clink/ndebit-setup")]
    public async Task<IActionResult> NdebitSetup(string portalSessionId, string storeId, string email)
    {
        if (!string.IsNullOrEmpty(portalSessionId))
        {
            var session = await _db.PortalSessions
                .Include(s => s.Subscriber)
                    .ThenInclude(s => s.Offering)
                    .ThenInclude(o => o.App)
                    .ThenInclude(a => a.StoreData)
                .Include(s => s.Subscriber)
                    .ThenInclude(s => s.Customer)
                    .ThenInclude(c => c.CustomerIdentities)
                .FirstOrDefaultAsync(s => s.Id == portalSessionId);

            if (session?.Subscriber != null)
            {
                var store = session.GetStoreData();
                storeId = store.Id;
                email = session.Subscriber.Customer.Email.Get() ?? "";
            }
        }
        return View("NdebitSetup", new NdebitSetupModel { StoreId = storeId, Email = email });
    }

    [AllowAnonymous]
    [HttpGet("~/clink/check-subscription-invoice")]
    public async Task<IActionResult> CheckSubscriptionInvoice(string invoiceId)
    {
        if (string.IsNullOrEmpty(invoiceId))
            return Json(new { isSubscription = false });

        var linked = await _db.Set<SubscriberInvoiceData>()
            .AnyAsync(s => s.InvoiceId == invoiceId);
        return Json(new { isSubscription = linked });
    }

    [HttpGet("configure")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> Configure(string storeId)
    {
        var settings = await _clinkService.GetSettings(storeId);
        ViewData["ActivePage"] = "Clink";
        return View(settings ?? new ClinkSettings());
    }

    [HttpPost("configure")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> Configure(string storeId, ClinkSettings settings)
    {
        if (!string.IsNullOrEmpty(settings.Noffer) && !settings.Noffer.StartsWith("noffer1"))
        {
            TempData["ErrorMessage"] = "Invalid CLINK offer string. It must start with 'noffer1'.";
            ViewData["ActivePage"] = "Clink";
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


