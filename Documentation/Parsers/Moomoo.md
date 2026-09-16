# Moomoo OpenD

This integration is intended to synchronize Moomoo brokerage data through the official Moomoo OpenD gateway.

The implementation is designed around an already-running OpenD instance. GhostfolioSidekick does not manage brokerage credentials, OTP codes, trading passwords, or interactive OpenD login.

## Configuration

The example below is a template. Replace every value wrapped in angle brackets before use.

```json
{
  "moomoo": [
    {
      "name": "<CONNECTION_NAME>",
      "host": "<OPEND_HOST>",
      "port": <OPEND_PORT>,
      "enabled": true,
      "history-days": 95,
      "market-accounts": {
        "US": "<US_ACCOUNT_NAME>",
        "MY": "<MY_ACCOUNT_NAME>",
        "HK": "<HK_ACCOUNT_NAME>",
        "SG": "<SG_ACCOUNT_NAME>",
        "JP": "<JP_ACCOUNT_NAME>",
        "AU": "<AU_ACCOUNT_NAME>",
        "CA": "<CA_ACCOUNT_NAME>",
        "SH": "<CN_ACCOUNT_NAME>",
        "SZ": "<CN_ACCOUNT_NAME>"
      },
      "cash-and-funds-account": "<CASH_AND_FUNDS_ACCOUNT_NAME>"
    }
  ]
}
```

The referenced GhostfolioSidekick accounts should also be defined in the normal `accounts` configuration section.

## Phase 1: OpenD health check

Phase 1 only checks that Sidekick can connect to OpenD and that OpenD returns at least one REAL securities account. It does not read transactions, balances, positions, funds, or fees.

A one-shot health-check tool is included for deployment validation:

```bash
dotnet run --project Tools/MoomooHealthCheck/MoomooHealthCheck.csproj -- <OPEND_HOST> <OPEND_PORT>
```

Successful output is JSON and intentionally excludes brokerage account IDs:

```json
{
  "Connected": true,
  "RealAccountDetected": true,
  "RealAccountCount": 1,
  "Message": "OpenD is reachable and at least one REAL securities account is available."
}
```

The command exits with code `0` only when OpenD is reachable and at least one REAL securities account is detected. This structured result can also be consumed by an external health dashboard without duplicating OpenD account-discovery logic.

## Data model

Moomoo order identifiers are retained as the stable transaction identity used by the importer.

The normalization layer currently supports:

- BUY activities
- SELL activities
- order-level fees
- market-aware Ghostfolio account routing
- known cash balances
- stock classification
- ETF classification
- mutual fund classification
- bond classification
- commodity / precious-metal classification
- futures classification
- cryptocurrency classification for future transport support

Cash and non-exchange fund products can be routed to a dedicated account through `cash-and-funds-account` rather than being forced into a market-specific account.

## Symbol normalization

Common Moomoo market prefixes are normalized to provider-friendly tickers before Sidekick performs normal symbol matching.

Examples:

```text
US.TEST    -> TEST
HK.00700   -> 0700.HK
MY.1234    -> 1234.KL
SG.TEST    -> TEST.SI
JP.1234    -> 1234.T
AU.TEST    -> TEST.AX
CA.TEST    -> TEST.TO
SH.600000  -> 600000.SS
SZ.000001  -> 000001.SZ
```

These are synthetic examples only.

## Security

Do not commit or place the following values in the Sidekick configuration repository:

- Moomoo passwords
- OTP / MFA codes
- trading unlock passwords
- brokerage account IDs unless explicitly required by a future transport implementation
- OpenD remembered-login state

OpenD should be configured separately according to the official Moomoo documentation. Sidekick only requires network access to the configured OpenD endpoint.

The integration is intended for read-only portfolio synchronization. It should not call order-placement, order-modification, or trade-unlock endpoints.

## Current implementation status

Phase 1 is implemented:

- official `moomoo-api` SDK dependency
- configurable OpenD host and port
- connection callback handling
- REAL securities-account discovery
- bounded health-check timeout
- one-shot JSON health runner

Transaction, cash, fund, fee, and position retrieval are intentionally deferred to later phases.
