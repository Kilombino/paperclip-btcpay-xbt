<?php
// Isolated regression checks against the installed connector source. No WordPress,
// database, network, emails, or real orders are loaded.
namespace {
    class WC_Payment_Gateway {}
    class WC_Order {
        public $status = 'on-hold', $notes = [], $paid = false, $meta = [];
        function get_status() { return $this->status; }
        function has_status($states) { return in_array($this->status, (array)$states, true); }
        function add_order_note($note) { $this->notes[] = $note; }
        function payment_complete() { $this->paid = true; $this->status = 'processing'; }
        function get_meta($key) { return $this->meta[$key] ?? ''; }
        function update_meta_data($key, $value) { $this->meta[$key] = $value; }
        function save() {}
    }
    function __($text, $domain = '') { return $text; }
    function get_option($key, $default = false) {
        if ($key === 'btcpay_gf_protect_order_status') return $GLOBALS['protect'] ?? 'no';
        if ($key !== 'btcpay_gf_order_states') return $default;
        $map = (new \BTCPayServer\WC\Helper\OrderStates)->getDefaultOrderStateMappings();
        $map['Expired'] = $map['ExpiredPaidPartial'] = 'wc-on-hold';
        return $map;
    }
    function wp_die($message, $title, $args) { throw new \RuntimeException($message, $args['response']); }
}
namespace BTCPayServer\WC\Helper {
    class Logger { static function debug($message) {} }
    class GreenfieldApiHelper { static function invoiceIsFullyPaid($id) { return true; } }
    class GreenfieldApiWebhook { const WEBHOOK_EVENTS = ['InvoicePaymentSettled', 'InvoiceSettled', 'InvoiceExpired', 'InvoiceProcessing', 'InvoiceInvalid', 'InvoiceReceivedPayment']; }
}
namespace BTCPayServer\Result {
    class Invoice {
        const STATUS_SETTLED = 'Settled', ADDITIONAL_STATUS_PAID_OVER = 'PaidOver';
        function getStatus() { return \BTCPayServer\Client\Invoice::$status; }
        function isSettled() { return $this->getStatus() === 'Settled'; }
        function getData() { return ['additionalStatus' => \BTCPayServer\Client\Invoice::$additional]; }
    }
}
namespace BTCPayServer\Client {
    class Invoice {
        static $status = 'Settled', $additional = 'None', $fail = false;
        function __construct($url, $key) {}
        function getInvoice($store, $invoice) {
            if (self::$fail) throw new \RuntimeException('API unavailable');
            return new \BTCPayServer\Result\Invoice;
        }
    }
}
namespace {
    $root = $argv[1] ?? die("Usage: php test-webhook-status.php PLUGIN_DIRECTORY\n");
    require $root . '/src/Helper/OrderStates.php';
    require $root . '/src/Gateway/AbstractGateway.php';
    class TestGateway extends \BTCPayServer\WC\Gateway\AbstractGateway {
        public function getPaymentMethods(): array { return ['XBT-CHAIN', 'XBT-LN']; }
        function __construct() { $this->apiHelper = (object)['url'=>'https://invalid.example', 'apiKey'=>'fixture', 'storeId'=>'fixture']; }
        function process($order, $event) { return $this->processOrderStatus($order, (object)$event); }
        function remember($order, $id) { $this->markDeliveryProcessed($order, $id); }
        function seen($order, $id) { return $this->isDeliveryProcessed($order, $id); }
        public function updateWCOrderStatus(\WC_Order $order, string $status): void {
            if ($status !== 'BTCPAY_IGNORE') $order->status = str_replace('wc-', '', $status);
        }
        public function updateWCOrderPayments(\WC_Order $order): void {}
    }
    $checks = 0;
    function check($ok, $message) { global $checks; if (!$ok) throw new \RuntimeException($message); $checks++; }
    $g = new TestGateway;
    foreach ([false, null] as $expiry) {
        $o = new WC_Order; $event = ['type'=>'InvoicePaymentSettled','invoiceId'=>'fixture'];
        if ($expiry !== null) $event['afterExpiration'] = $expiry;
        $g->process($o, $event);
        check($o->status === 'on-hold' && !str_contains(implode(' ', $o->notes), 'expired'), 'Shared status must not imply expiry');
    }
    $o = new WC_Order;
    $g->process($o, ['type'=>'InvoicePaymentSettled','invoiceId'=>'fixture','afterExpiration'=>true]);
    check(str_contains(implode(' ', $o->notes), 'expired') && $o->status === 'processing', 'Verified late payment follows configured late-paid mapping');
    foreach ([['Processing','None',false], ['Settled','PaidLate',false], ['Settled',null,false], ['Settled','None',true], ['Settled','PaidOver',true]] as [$status,$additional,$paid]) {
        \BTCPayServer\Client\Invoice::$status = $status;
        \BTCPayServer\Client\Invoice::$additional = $additional;
        $o = new WC_Order;
        $result = $g->process($o, ['type'=>'InvoiceSettled','invoiceId'=>'fixture']);
        check($o->paid === $paid && $result === $paid, 'Settlement must match authoritative invoice status');
    }
    \BTCPayServer\Client\Invoice::$fail = true; $o = new WC_Order;
    try { $g->process($o, ['type'=>'InvoiceSettled','invoiceId'=>'fixture']); throw new \RuntimeException('Expected retry'); }
    catch (\RuntimeException $e) { check($e->getCode() === 503 && !$o->paid, 'API failure must request retry without paying'); }
    foreach (['no','yes'] as $protect) foreach (['Settled','Processing'] as $apiStatus) foreach (['completed','refunded'] as $state) foreach (['InvoiceExpired','InvoiceInvalid','InvoiceProcessing','InvoiceReceivedPayment','InvoicePaymentSettled'] as $type) {
        $GLOBALS['protect'] = $protect;
        \BTCPayServer\Client\Invoice::$status = $apiStatus;
        $o = new WC_Order; $o->status = $state; $g->process($o, ['type'=>$type,'afterExpiration'=>true,'invoiceId'=>'fixture']);
        check($o->status === $state, 'Old events must not regress a final order');
    }
    $GLOBALS['protect'] = 'no'; $o = new WC_Order;
    try { $g->process($o, ['type'=>'InvoicePaymentSettled','invoiceId'=>'fixture','afterExpiration'=>true]); throw new \RuntimeException('Expected retry'); }
    catch (\RuntimeException $e) { check($e->getCode() === 503 && $o->status === 'on-hold' && !$o->meta, 'Late-payment API failure must leave status and delivery history untouched'); }
    \BTCPayServer\Client\Invoice::$fail = false;
    \BTCPayServer\Client\Invoice::$status = 'Settled';
    $g->process($o, ['type'=>'InvoicePaymentSettled','invoiceId'=>'fixture','afterExpiration'=>true]);
    check($o->status === 'processing', 'Successful late-payment retry follows configured mapping');
    $o = new WC_Order;
    for ($i = 0; $i < 25; $i++) $g->remember($o, (string)$i);
    check(count($o->meta['BTCPay_processed_deliveries']) === 20 && $g->seen($o,'24') && !$g->seen($o,'0'), 'Delivery history must remain bounded');
    echo "$checks webhook status checks passed\n";
}
