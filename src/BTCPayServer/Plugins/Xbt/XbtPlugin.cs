using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.DependencyInjection;
using NBXplorer;

namespace BTCPayServer.Plugins.Xbt;

public sealed class XbtPaymentNetwork : BTCPayNetwork { }

public sealed class XbtPlugin : BaseBTCPayServerPlugin
{
    public const string SatsCurrency = "XBTSATS";
    public static readonly string[] RateRules = {
        "XBT_BTCB2 = 1;",
        "BTCB2_XBT = 1;",
        "BTCB2_X = BTCB2_XBT * XBT_X;",
        "XBT_XBTSATS = 100000000;",
        "XBTSATS_X = XBTSATS_XBT * XBT_X;",
        "XBT_X = neoxex(XBT_X);"
    };
    public override string Identifier => "Paperclip.XbtLightning";
    public override string Name => "Paperclip XBT (test)";
    public override string Description => "Bitcoin BLAKE2b on-chain and Lightning checkout. Not audited.";

    public override void Execute(IServiceCollection services)
    {
        var bootstrap = ((PluginServiceCollection)services).BootstrapServices;
        if (!bootstrap.GetRequiredService<SelectedChains>().Contains("XBT")) return;
        var nbx = bootstrap.GetRequiredService<NBXplorerNetworkProvider>().GetFromCryptoCode("XBT");
        var network = new XbtPaymentNetwork
        {
            CryptoCode = "XBT", DisplayName = "Bitcoin BLAKE2b",
            NBXplorerNetwork = nbx,
            CryptoImagePath = "imlegacy/paperclip.svg", LightningImagePath = "imlegacy/paperclip.svg",
            DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbx.NBitcoinNetwork.ChainName),
            WalletSupported = true, ReadonlyWallet = true, SupportLightning = true, ShowSyncSummary = true,
            CoinType = nbx.CoinType, SupportPayJoin = false, SupportRBF = false, VaultSupported = false,
            DefaultRateRules = RateRules
        };
        network.SetDefaultElectrumMapping(nbx.NBitcoinNetwork.ChainName);
        services.AddBTCPayNetwork((BTCPayNetworkBase)network);
        services.AddUIExtension("store-integrations-nav", "/Plugins/Xbt/Views/Nav.cshtml");
        var onchain = PaymentTypes.CHAIN.GetPaymentMethodId("XBT");
        services.AddDefaultPrettyName(onchain, "XBT on-chain (BLAKE2b)");
        services.AddSingleton<IPaymentMethodHandler>(p => ActivatorUtilities.CreateInstance<BitcoinLikePaymentHandler>(p, network, onchain));
        services.AddSingleton<IPaymentLinkExtension>(p => ActivatorUtilities.CreateInstance<BitcoinPaymentLinkExtension>(p, network, onchain));
        services.AddSingleton<ICheckoutModelExtension>(p => ActivatorUtilities.CreateInstance<BitcoinCheckoutModelExtension>(p, network, onchain));
        services.AddSingleton<IPaymentMethodBitpayAPIExtension>(p => ActivatorUtilities.CreateInstance<BitcoinPaymentMethodBitpayAPIExtension>(p, onchain));
        services.AddTransactionLinkProvider(onchain, new DefaultTransactionLinkProvider("https://mempool.guide/tx/{0}"));
        services.AddCurrencyData(new CurrencyData { Code = "XBT", Name = "Bitcoin BLAKE2b", Divisibility = 8, Crypto = true, Symbol = "XBT" });
        services.AddCurrencyData(new CurrencyData { Code = "BTCB2", Name = "Bitcoin BLAKE2b (XBT)", Divisibility = 8, Crypto = true, Symbol = "BTCB2" });
        services.AddCurrencyData(new CurrencyData { Code = SatsCurrency, Name = "XBT sats", Divisibility = 0, Crypto = true, Symbol = "XBT sats" });
        services.AddCurrencyData(new CurrencyData { Code = "USDC", Name = "USD Coin (pricing only)", Divisibility = 6, Crypto = true, Symbol = "USDC" });
        services.AddRateProvider<NeoxExRateProvider>();
        var pmi = PaymentTypes.LN.GetPaymentMethodId("XBT");
        services.AddDefaultPrettyName(pmi, "XBT Lightning (BLAKE2b)");
        services.AddSingleton<IPaymentMethodHandler>(p => ActivatorUtilities.CreateInstance<LightningLikePaymentHandler>(p, network, pmi));
        services.AddSingleton<IPaymentLinkExtension>(p => ActivatorUtilities.CreateInstance<LightningPaymentLinkExtension>(p, network, pmi));
        services.AddSingleton<ICheckoutModelExtension>(p => ActivatorUtilities.CreateInstance<LNCheckoutModelExtension>(p, network, pmi));
        services.AddSingleton<IPaymentMethodBitpayAPIExtension>(p => ActivatorUtilities.CreateInstance<LightningPaymentMethodBitpayAPIExtension>(p, pmi));
    }
}
