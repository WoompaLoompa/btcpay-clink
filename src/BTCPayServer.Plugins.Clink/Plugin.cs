using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Clink.Services;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddSingleton<IUIExtension>(new UIExtension("Clink/ClinkStoreNav", "store-integrations-nav"));

        services.AddSingleton<IUIExtension>(new UIExtension("Clink/ClinkCheckoutPayment", "checkout-end"));
    }
}
