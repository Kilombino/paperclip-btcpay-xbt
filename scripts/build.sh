#!/bin/sh
set -eu
root=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
cd "$root"
python3 scripts/prepare.py
python3 -m unittest discover -s gateway -p test_gateway.py
sdk=mcr.microsoft.com/dotnet/sdk:10.0.400-noble
docker run --rm -v "$root/.build/nbxplorer:/source" \
  -v "$root/.build/btcpay/Paperclip/nuget:/packages" -w /source "$sdk" sh -c \
  'dotnet test NBXplorer.Tests/NBXplorer.Tests.csproj --filter FullyQualifiedName~XbtTests && dotnet pack NBXplorer.Client/NBXplorer.Client.csproj -c Release -o /packages'
docker run --rm -v "$root/.build/btcpay:/source" -w /source "$sdk" \
  dotnet test BTCPayServer.Tests/BTCPayServer.Tests.csproj --filter FullyQualifiedName~XbtTests
if [ "${1:-}" = '--test-only' ]; then exit 0; fi
docker build -t paperclip-nbxplorer:xbt-beta .build/nbxplorer
docker build -t paperclip-btcpay:xbt-beta .build/btcpay
docker build -t paperclip-cln-gateway:xbt-beta gateway
