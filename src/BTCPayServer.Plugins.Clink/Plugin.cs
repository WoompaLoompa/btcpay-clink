using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Plugins.Clink.Services;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Clink;

public class ClinkPlugin : BaseBTCPayServerPlugin
{
    public override IBTCPayServerPlugin.PluginDependency[] Dependencies { get; } =
    [
        new IBTCPayServerPlugin.PluginDependency { Identifier = nameof(BTCPayServer), Condition = ">=2.4.0" }
    ];

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<ClinkService>();
        services.AddSingleton<ClinkNostrBridge>();
        services.AddSingleton<NostrEventStore>();
        services.AddSingleton<NdebitRegistry>();
        services.AddSingleton<EmailNdebitStore>();

        services.AddSingleton<ILightningConnectionStringHandler>(sp =>
            new ClinkConnectionStringHandler(
                sp.GetRequiredService<ClinkNostrBridge>(),
                sp.GetRequiredService<NostrEventStore>(),
                sp.GetRequiredService<ILoggerFactory>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<EmailNdebitStore>()));

        services.Configure<RazorViewEngineOptions>(o =>
        {
            o.ViewLocationFormats.Add("Views/{0}.cshtml");
            o.PageViewLocationFormats.Add("Views/{0}.cshtml");
            o.AreaViewLocationFormats.Add("Views/{0}.cshtml");
        });

        services.AddUIExtension("store-integrations-nav", "Clink/ClinkStoreNav");
        services.AddUIExtension("ln-payment-method-setup-custom", "Clink/LightningSetupCustom");
        services.AddUIExtension("checkout-end", "Clink/ClinkCheckoutPayment");
    }
}
