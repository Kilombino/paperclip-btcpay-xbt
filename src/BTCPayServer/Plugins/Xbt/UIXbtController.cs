using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Xbt;

public class XbtSettings
{
    public bool OnChainEnabled { get; set; }
    public bool LightningEnabled { get; set; }
    public static XbtSettings From(StoreBlob blob) => new() {
        OnChainEnabled = !blob.IsExcluded(PaymentTypes.CHAIN.GetPaymentMethodId("XBT")),
        LightningEnabled = !blob.IsExcluded(PaymentTypes.LN.GetPaymentMethodId("XBT"))
    };
    public void Apply(StoreBlob blob)
    {
        blob.SetExcluded(PaymentTypes.CHAIN.GetPaymentMethodId("XBT"), !OnChainEnabled);
        blob.SetExcluded(PaymentTypes.LN.GetPaymentMethodId("XBT"), !LightningEnabled);
    }
}

[Route("stores/{storeId}/xbt")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIXbtController(StoreRepository repository) : Controller
{
    [HttpGet]
    public IActionResult Settings() => View("~/Plugins/Xbt/Views/Settings.cshtml", XbtSettings.From(HttpContext.GetStoreData().GetStoreBlob()));

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(XbtSettings model)
    {
        var store = HttpContext.GetStoreData();
        var blob = store.GetStoreBlob();
        model.Apply(blob);
        store.SetStoreBlob(blob);
        await repository.UpdateStore(store);
        TempData[WellKnownTempData.SuccessMessage] = "XBT payment settings saved. Changes apply to new invoices.";
        return RedirectToAction(nameof(Settings), new { storeId = store.Id });
    }
}
