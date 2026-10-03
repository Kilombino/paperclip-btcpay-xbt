#!/bin/sh
# Prepare an XBT LND node for BTCPay: check it is a BLAKE2b node, bake an
# invoice-only macaroon and print the BTCPay connection string.
#
# Usage:
#   LNCLI="docker exec lnd lncli --network mainnet" \
#   LND_REST=https://host.docker.internal:8080 \
#   LND_TLS_CERT=/path/to/lnd/tls.cert \
#   sh scripts/lnd-connection.sh
#
# LNCLI     command that runs lncli against the node (default: lncli)
# LND_REST  LND REST URL as BTCPay will reach it from its container
# LND_TLS_CERT  the node's tls.cert, to pin its SHA-256 thumbprint
#
# The macaroon only allows info:read, invoices:read and invoices:write: BTCPay can
# create, read and cancel invoices and see the node's identity, nothing else. It
# cannot pay, open or close channels, move on-chain funds or read the seed. It uses
# its own root key id, so it can be revoked alone: lncli deletemacaroonid <id>.
set -eu
LNCLI=${LNCLI:-lncli}
LND_REST=${LND_REST:?Set LND_REST, e.g. https://host.docker.internal:8080}
LND_TLS_CERT=${LND_TLS_CERT:?Set LND_TLS_CERT to the node tls.cert}
ROOT_KEY_ID=${ROOT_KEY_ID:-48}

info=$($LNCLI getinfo)
# A BLAKE2b node requires option_blake2b (bit 512) and signs with unified sighash
# (514/515). A stock SHA-256 LND has neither: refuse it, as the CLN gateway does.
printf '%s' "$info" | python3 -c '
import json, sys
info = json.load(sys.stdin)
features = info.get("features", {})
chains = info.get("chains", [])
ok = (str(512) in features and features[str(512)].get("is_required")
      and (str(514) in features or str(515) in features)
      and any(c.get("chain") == "bitcoin" and c.get("network") == "mainnet" for c in chains)
      and info.get("synced_to_chain"))
if not ok:
    sys.exit("Not a synced XBT (BLAKE2b) mainnet LND node: bit 512 required and 514/515 expected.")
print("Node", info["identity_pubkey"], info.get("alias", ""), "is a synced XBT LND node.", file=sys.stderr)
'

macaroon=$($LNCLI bakemacaroon --root_key_id "$ROOT_KEY_ID" info:read invoices:read invoices:write)
thumbprint=$(openssl x509 -in "$LND_TLS_CERT" -outform DER | openssl dgst -sha256 -r | cut -d' ' -f1)

echo "Invoice-only macaroon baked with root key id $ROOT_KEY_ID (revoke: lncli deletemacaroonid $ROOT_KEY_ID)." >&2
echo "Put this in .env (keep it private):" >&2
echo "XBT_LIGHTNING=type=lnd-rest;server=${LND_REST%/}/;macaroon=$macaroon;certthumbprint=$thumbprint"
