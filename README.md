# Paperclip XBT for BTCPay Server

**Beta · Not independently audited · Receiving integration**

Accept Bitcoin BLAKE2b (**XBT**, also known as **BTCB2**) on-chain and over
Lightning in BTCPay Server. Choose either payment method or both per store.

This repository contains the plugin source, a restricted CLN invoice gateway,
the XBT NBXplorer changes, and build scripts against pinned upstream releases.
**It is not a drop-in `.btcpay` plugin for a stock BTCPay installation.** XBT's
block format and separate network identity require the included core/client
patches. Do not install the plugin DLL alone into an existing production server.

## Features

- XBT on-chain receiving with per-invoice addresses and confirmation tracking.
- Core Lightning BOLT11 checkout, including private-channel route hints.
- Independent **on-chain** and **Lightning** switches under
  **Integrations → XBT payments**. They affect new invoices, not existing orders.
- Invoice currencies **XBT**, **BTCB2** (exactly 1 XBT), and **XBTSATS**
  (100,000,000 sats per XBT). Payment methods retain canonical IDs
  `XBT-CHAIN` and `XBT-LN`; BTCB2 is an alternate pricing ticker.
- Optional USDC pricing from NeoxEX's BTCB2/USDC pair, with stale/invalid quote
  checks. No implicit USDC/USD peg or SHA-256 BTC price fallback.
- Watch-only receiving wallet setup. Spending keys belong in compatible XBT
  wallet software. On-chain sending, PayJoin and hardware signing are disabled.

## Requirements

- Linux, Git, Python 3, Docker Engine and Docker Compose v2; builds require
  internet access and several GB of available memory and disk space.
- Your own synced XBT Knots backend, reachable over authenticated RPC and P2P.
  Mainnet's XBT fork checkpoint is checked before indexing. An unpruned node
  with txindex was used for testing. Pruned historical recovery is not validated.
- For Lightning, your own XBT node with required BLAKE2b bit 512 and unified
  signature capability 514/515: **Core Lightning** (tested with
  `v26.06.8-blake2b.5`) or **LND** (tested with `0.21.3-beta-blake2b.14` from
  [paulscode/lightning-fork](https://github.com/paulscode/lightning-fork)).
  Receiving Lightning requires a funded channel with inbound liquidity.
- A dedicated XBT merchant wallet account public key, or a backed-up watch-only
  wallet created through BTCPay. **Do not import a SHA-256 BTC wallet.**

Pinned sources: BTCPay Server **2.4.4**, NBXplorer **2.6.13**. Exact revisions
are in [upstream.json](upstream.json). Mainnet is the supported deployment target.

## Build

```sh
git clone https://github.com/connorslab/paperclip-btcpay-xbt.git
cd paperclip-btcpay-xbt
sh scripts/build.sh
```

The script prepares upstream source under `.build`, applies the included patches,
runs focused tests, builds the custom NBXplorer client package, and creates:

- `paperclip-btcpay:xbt-beta`
- `paperclip-nbxplorer:xbt-beta`
- `paperclip-cln-gateway:xbt-beta`

Use `sh scripts/build.sh --test-only` to omit image builds. If sources change,
move the affected generated `.build/btcpay` or `.build/nbxplorer` directory aside
and rerun. Source preparation refuses to overwrite differing checkouts.

## Start an isolated installation

The example is for a **new installation**, not an automatic migration of an
existing BTCPay database. Back up existing services before adapting it.

1. Copy `.env.example` to `.env`, fill in the node connections, CLN public ID and
   RPC socket path, and choose a new database password (`openssl rand -hex 32`).
   Keep `.env` private (`chmod 600 .env`). No defaults contain working credentials.
2. Create writable app directories:
   `mkdir -p data/btcpay data/nbxplorer && sudo chown -R 1000:1000 data/btcpay data/nbxplorer`.
3. Give the gateway's supplemental `CLN_RPC_GID` group read/write access to the
   CLN socket. Mount the socket **file**, never the node's keys/data directory.
4. Run `docker compose up -d`. Only BTCPay is published, on
   `127.0.0.1:23000`; database, indexer and gateway have no published ports.
5. Open `http://localhost:23000`, create your administrator account and store.
   Connect the internal Lightning node and/or import your XBT wallet's account
   public key. Compare the sample receiving addresses with your wallet.
6. Set the desired payment switches and create a small invoice. Confirm actual
   receipt in your wallet/node before relying on automated settlement.

New indexers start at the current tip. An existing wallet needs a historical
rescan to show old transactions. Keep wallet recovery words and configuration
backups; the watch-only server cannot recover lost spending keys.

The socket-file mount avoids exposing CLN keys. If CLN recreates its RPC socket
after restarting, recreate the gateway with
`docker compose up -d --force-recreate gateway` to bind the new socket.

For LAN/remote access, use an authenticated, trusted HTTPS reverse proxy to
BTCPay, forwarding the original host and `X-Forwarded-Proto`. Camera scanning
requires a secure browser context and camera permission. Self-signed certificates
must be explicitly trusted on each device. Do not bypass browser TLS validation.

## Lightning with LND instead of CLN

BTCPay talks to LND natively, so LND needs no gateway: LND's own macaroons give
the same invoice-only boundary. `scripts/lnd-connection.sh` checks that the node
is a synced XBT LND node (bit 512 required, 514/515 present; it refuses a stock
SHA-256 node, as the CLN gateway does), bakes a macaroon limited to `info:read`,
`invoices:read` and `invoices:write` under its own root key id, and prints the
connection string with the node's pinned TLS certificate thumbprint:

```sh
LNCLI="docker exec lnd lncli --network mainnet" \
LND_REST=https://host.docker.internal:8080 \
LND_TLS_CERT=/path/to/lnd/tls.cert \
sh scripts/lnd-connection.sh
```

In `.env`, set `COMPOSE_PROFILES=` (empty, so the CLN gateway is not started) and
`XBT_LIGHTNING=` to the printed line. BTCPay reaches the node's REST port from its
container through `host.docker.internal`; the node's TLS certificate must include
the name or address used in `LND_REST` (`tlsextraip` / `tlsextradomain` in
`lnd.conf`). The macaroon cannot pay, manage channels, move on-chain funds or read
the seed. Unlike the CLN gateway it does not hide the node's other invoices from
BTCPay; use a node dedicated to the store if that matters. Revoke it alone with
`lncli deletemacaroonid <root key id>`.

## Safety and limitations

The CLN gateway allows invoice operations and limited status reads, and rejects
spending, channel management and secret extraction. It checks the configured
node identity and feature bits before creating invoices. It includes private-hop
hints; invoice holders can therefore see the receiving peer. Gateway access is
instance-wide: this is not isolation between mutually untrusted BTCPay operators.

On-chain transaction signing with XBT's unified signature rules is **not**
implemented here. On-chain spending, refunds and payouts must use compatible
external XBT software. Do not assume Bitcoin hardware wallets support XBT.
Merchant BOLT12 checkout is not implemented.

A successful paid Lightning checkout and real-chain indexing have been tested.
A fresh paid on-chain merchant checkout and induced reorg test remain outstanding.
The engineering review is **not an independent security audit**; see
[review and test notes](docs/REVIEW.md). Use small amounts while evaluating.

## Layout

- `src/`: plugin, UI, logo and focused C# tests.
- `patches/btcpay.patch`: required BTCPay integration changes.
- `patches/nbxplorer.patch`: XBT header hashing, chain identity and tests.
- `gateway/`: invoice-only CLN Unix-socket adapter and tests.
- `scripts/`: reproducible source preparation and build entry point.
- `.github/workflows/build.yml`: clean build/test checks.

No private deployments, node credentials, TLS private keys, wallets or local
test administrator setup scripts are distributed. MIT licensed; see
[third-party notices](THIRD_PARTY_NOTICES.md).
