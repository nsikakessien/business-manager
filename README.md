# Business Manager

A Windows desktop app for a small shop: products and stock, a till for sales
(cash, card, transfer or on account), customer accounts and a dashboard.

Built with .NET 10, WPF (MVVM via CommunityToolkit.Mvvm) and SQLite (EF Core).

## Run

```powershell
dotnet run --project src/BusinessManager.Desktop
dotnet test BusinessManager.slnx
```

## Where data lives

| What | Location |
| --- | --- |
| Database | `%LOCALAPPDATA%\BusinessManager\business-manager.db` |
| Backups | `%LOCALAPPDATA%\BusinessManager\Backups\` (newest 14 kept) |
| Logs | `%LOCALAPPDATA%\BusinessManager\Logs\` (30 days kept) |

Set `BUSINESS_MANAGER_DATA_DIR` to use a different folder (for example for a
training copy). Only one copy of the app can run per data folder.

**Backups** are taken automatically once a day at start-up, before every
schema upgrade, and on demand ("Back up now"). To restore, close the app and
copy a backup file over `business-manager.db` (delete any `-wal`/`-shm` files
next to it first).

## Project layout

```
src/
  BusinessManager.Domain          Entities and business rules (no dependencies)
  BusinessManager.Application     Service contracts, DTOs, user-facing errors
  BusinessManager.Infrastructure  EF Core/SQLite services, migrations, backups
  BusinessManager.Desktop         WPF views and view models
tests/
  *.Domain.Tests                  Unit tests for business rules
  *.Infrastructure.Tests          Integration tests against real SQLite files
  *.Desktop.Tests                 Input parsing and till calculations
```

## Rules worth knowing

- **Stock never goes negative.** Each change is recorded as a movement
  (receipt, adjustment, sale, void) with the resulting quantity.
- **Sales are atomic.** Stock, the customer account and the sale number are
  updated in one `BEGIN IMMEDIATE` transaction, so two tills can't sell the
  last unit twice and a failed sale changes nothing.
- **Unpaid amounts need a customer** with enough credit available. A credit
  limit of 0 means the customer must pay in full.
- **Prices are re-checked at checkout.** If a price changed after items were
  added, the sale is refused and the cart is refreshed.
- **Nothing is deleted.** Products and customers are archived; sales are
  voided (stock returned, account charge reversed). A customer can only be
  archived with a zero balance. Voiding does not refund money paid at the
  till; hand that back separately.
- **Money** has at most two decimal places; amounts are shown without a
  currency symbol using Windows regional settings.

## Schema changes

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/BusinessManager.Infrastructure --output-dir Persistence/Migrations
```

Migrations are applied automatically on start-up, after a safety backup.
A database created by a newer version of the app is refused rather than
risk damaging it.
