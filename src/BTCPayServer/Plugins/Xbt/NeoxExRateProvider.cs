using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Xbt;

public sealed class NeoxExRateProvider(IHttpClientFactory factory) : IRateProvider
{
    public const string Endpoint = "https://neoxa.exchange/api/exchange/ticker/BTCB2_USDC";
    public RateSourceInfo RateSourceInfo => new("neoxex", "NeoxEX XBT/USDC", Endpoint);

    public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await factory.CreateClient().GetAsync(Endpoint, timeout.Token);
        response.EnsureSuccessStatusCode();
        return Parse(JObject.Parse(await response.Content.ReadAsStringAsync(timeout.Token)), DateTimeOffset.UtcNow);
    }

    public static PairRate[] Parse(JObject data, DateTimeOffset now)
    {
        if (data.Value<bool?>("success") != true || data.Value<string>("pair") != "BTCB2_USDC")
            throw new FormatException("NeoxEX did not return the BLAKE2b XBT/USDC pair");
        var ticker = data["ticker"] ?? throw new FormatException("Missing NeoxEX ticker");
        var computed = DateTimeOffset.FromUnixTimeMilliseconds(ticker.Value<long>("computedAt"));
        if (computed < now.AddMinutes(-5) || computed > now.AddMinutes(1))
            throw new FormatException("NeoxEX quote is stale or has an invalid timestamp");
        var price = ticker.Value<decimal>("lastPrice");
        if (price <= 0) throw new FormatException("Invalid NeoxEX price");
        // USDC is not silently relabeled USD; settlement remains XBT.
        return new[] { new PairRate(new CurrencyPair("XBT", "USDC"), new BidAsk(price, price)) };
    }
}
