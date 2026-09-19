# Payday API spike — products, stock, invoicing, payroll

Run 2026-09-19 against the **sandbox** (`https://api.test.payday.is`, `Api-Version: alpha`) with a
throwaway company. Each finding below is a real request/response pair, trimmed. The plan this feeds
is "Workit on Payday" (see the plan artifact / memory note `workit-on-payday-plan`).

Status: **7 of 8 questions answered.** The invoice → stock question needs the sandbox company's
system setup completed (Payday refuses to create invoices until then); see "Still open".

## Findings

### 1. `reserved: true` on a movement is ignored → there is no reservation via the API

```
POST /products/{id}/movements
{"date":"2026-09-19","description":"MNT102 · Anna (reserved)","changeInQuantity":-12,"reserved":true}
200 {"changeInQuantity":-12.0,"reserved":false,"quantityAfterChange":88.0,"reservedAfterChange":null,...}
```

Stock went 100 → 88 immediately. **Design consequence:** Workit cannot reserve; a usage either
consumes stock at log time or does nothing until invoiced. (Decided by finding 8 once available.)

### 2. Stock is a ledger of movements, and `PUT /products/{id}` with `quantity` writes a correction into it

```
PUT /products/{id}  {"name":"…","sku":"SPIKE-CABLE","quantity":5,...}
200 {"quantity":5.0,...}
GET /products/{id}/movements → top entry:
  {"description":"Birgðaleiðrétting","changeInQuantity":-78.0,"quantityAfterChange":5.0}
```

So sending `quantity` on an update is a **stock correction**, not ignored. `PUT` **without**
`quantity` leaves stock alone (`5.0` stayed `5.0`).
**Design consequence:** Workit must never include `quantity` in a product update. Reversals are
plain positive movements (`+5` → `quantityAfterChange: 10.0`); there is no delete-movement.

### 3. Service vs stock product: `quantity` is `null` for a product created without stock

```
POST /products {"name":"Spike vinna klst","sku":"SPIKE-LABOR","salesUnitPriceExcludingVAT":12000,"vatPercentage":24,"salesLedgerAccountId":"…"}
200 {"quantity":null,"inventoryLedgerAccountId":null,"costOfGoodsSoldLedgerAccountId":null,...}

POST /products {..."sku":"SPIKE-CABLE","quantity":100,"purchaseDate":"2026-09-19","purchaseUnitPriceExcludingVAT":900}
200 {"quantity":100.0,...}   movements: [{"description":"Upphafsbirgðastaða","changeInQuantity":100,"purchaseUnitPriceExcludingVAT":900,"salesUnitPriceExcludingVAT":1500}]
```

`quantity == null` ⇒ no stock tracking. Good enough as a *default* for the Material/Labor role in
Workit; the owner-set role stays the source of truth. Note the sandbox company had **no inventory or
COGS ledger accounts** (`/products/inventoryLedgerAccounts` → `[]`) and stock tracking still worked
— those accounts are for bookkeeping the purchase, not a prerequisite for movements.

### 4. Invoice lines do NOT take the price from the product

```
POST /invoices  lines:[{"description":"Cable","quantity":12,"vatPercentage":0,"productId":"…"}]
400 "Invoice line with ID '' in position '1' is not valid. Either unit price excluding VAT or unit price including VAT must be specified."
```

`productId`/`sku` on a line is a **reference**, not a price lookup. **Design consequence:** the
console keeps sending `unitPriceExcludingVat` and `vatPercentage` on every line, taken from the
Workit product cache (which is refreshed from Payday) — Payday still books to the product's ledger
account, but the price is ours to send.

Also: a company without a VAT number cannot use VAT lines at all:
`400 "Unable to add VAT to invoice line. Company is not VAT obligated or does not have a VAT number."`

### 5. List shapes and paging

```
GET /products/?perpage=10&page=1&orderBy=sku&order=asc&query=SPIKE
200 {"products":[...],"perPage":10,"total":2,"page":1,"pages":1}

GET /products/{id}/movements
200 {"productMovements":[...],"perPage":100,"total":3,"page":1,"pages":1}   (newest first)

GET /products/sku/?sku=SPIKE-LABOR → the single product object (200)
```

Wrapper object with `total`/`pages`; movements default to 100 per page, newest first. `query`
matched on SKU prefix as well as name.

### 6. Error bodies are plain text (or `{"message":…}`), status 400

| Case | Response |
|---|---|
| Duplicate SKU | `400 Unable to Create the product. Product already exists with the following SKU: SPIKE-CABLE` |
| Unknown ledger account | `400 Unable to create product. Invalid data. No sales ledger account found with id: …` |
| Movement without date | `400 {"message":"Invalid data. Date is required."}` |
| Invoice before company setup | `401 Unable to create invoice. Please complete the required system setup before creating an invoice` (note: 401, not 400) |
| Wrong environment's client | `401 {"error":"invalid_client","error_description":"Client authentication failed…"}` |

Show these verbatim in the console, same rule as our own 409s. Some bodies are JSON, some plain
strings — the client must handle both.

### 7. Payroll timesheet upload

```
POST /payroll/upload/timesheet
[{"ssn":"0101013000","name":"Spike Starfsmaður","items":[{"name":"Dagvinna","quantity":10.5},{"name":"Yfirvinna","quantity":1}]}]
200 {"payoutId":"00000000-0000-0000-0000-000000000000","totalEmployeesInFile":0,"totalEmployeesRead":0,"employeeSSNsNotOnRecord":null}
```

The endpoint answers with a `payoutId` and read/unknown counts — so the export can report exactly
which employees Payday did not recognise. Item-name matching and re-upload semantics still need a
company with payroll employees (`GET /payroll/employees` was `[]`).

Other reads: `GET /general/vat` → `[0.00, 11.00, 24.00]`; `GET /payroll/pension/funds/0` → fund
list with `number`/`name`; `GET /companies/me` includes `hasClaimCollection`.

### 8. Does invoicing a product line move stock? — **still open**

Blocked on the sandbox company's setup (see below). Everything is staged: product `SPIKE-CABLE`
(stock 10), customer "Spike kúnni ehf.", and the invoice body from finding 4 with prices added.

## Still open / what's needed

1. In `app.test.payday.is` complete the company setup (company SSN, address, **VAT number**, whatever
   the setup checklist asks) so `POST /invoices` is allowed. Then re-run the finding-8 invoice and read
   `/products/{id}/movements`.
2. Create one payroll employee in the sandbox to check item-name matching for the timesheet upload.

## Design decisions these settle

- Stock: consume-or-nothing, never reserve (1). Workit posts movements only; reversal = positive movement (2).
- Product updates from Workit never carry `quantity` (2). Workit doesn't edit products at all in v1 anyway.
- Material vs labor default from `quantity == null`, owner-overridable (3).
- Invoice lines always carry price + VAT from the Workit cache; `productId` is for the ledger (4).
- Cache paging by `pages`; movements are already a ledger we can show in the console (5).
- Error handling: string or `{message}` bodies, surfaced verbatim (6).
- Payroll export reports `employeeSSNsNotOnRecord` back to the owner (7).

## Cleanup

Sandbox items created: products `SPIKE-LABOR`, `SPIKE-CABLE` (+5 movements), customer
"Spike kúnni ehf.". Kept until finding 8 is run; then deleted (products can be deleted while
unreferenced; after an invoice references `SPIKE-CABLE` it can only be archived).
