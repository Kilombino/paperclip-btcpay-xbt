# WooCommerce checkout

Use the official BTCPay Greenfield WooCommerce plugin. XBT invoice payment methods
are `XBT-LN` and `XBT-CHAIN`. The plugin may submit underscore aliases;
XBTPay normalizes those through its legacy payment-method parser.

Use connector **2.8.6** or newer for its security fixes. For 2.8.6, apply
`woocommerce-2.8.6-expiry.patch` inside the connector directory with
`git apply /path/to/woocommerce-2.8.6-expiry.patch`. This preserves the upstream
invoice API verification and webhook retry behavior while correcting expiry
detection when processing and expired invoices share a WooCommerce status.
An explicit `afterExpiration` flag takes precedence; a shared `on-hold` status
alone is not evidence that an invoice expired. Keep a copy of the plugin before
updating and recheck this patch on subsequent connector updates.
The patch also prevents delayed payment-settled events from reopening completed
or refunded orders, and requests a retry if late-payment verification is unavailable.
Verified late payments still follow your configured late-paid order status; choose
an on-hold mapping if they require manual fulfillment review.

Run `php test-webhook-status.php /path/to/btcpay-greenfield-for-woocommerce`
against the patched connector. These isolated checks use fake orders and an API
stub; they do not contact a server, send email, or prove live payment settlement.

For older deployed XBTPay builds, copy `xbtpay-compat.php` to WordPress's
`wp-content/mu-plugins/`. It maps only the two XBT aliases for the default gateway,
leaving other networks unchanged. It does not enable any gateway or payment method.

Give WooCommerce store-scoped invoice-create, invoice-view and store-settings-view
permissions. Register a signed webhook separately. Do not grant spending access.
Keep credentials outside source control. Verify settlement against the invoice API
before fulfillment; never fulfill based solely on the browser return.

Before launch, test invoice totals including shipping/tax, signature rejection,
duplicate events, expiration, partial payment, and an actual settled payment.
Private Docker hostnames used in staging are not customer checkout URLs; configure
and test the final HTTPS invoice and return URLs before enabling checkout.

Runtime images normalize permissions on public `wwwroot` assets so deployments
using an unprivileged UID can render checkout. Wallet and database permissions
are unaffected.
