# Payday API spike — products, stock, invoicing, payroll

Run 2026-09-19 against the **sandbox** (`https://api.test.payday.is`, `Api-Version: alpha`) with a
throwaway company. Each finding below is a real request/response pair, trimmed. The plan this feeds
is "Workit on Payday" (see the plan artifact / memory note `workit-on-payday-plan`).

Status: **all 8 questions answered.** (The invoice test needed the sandbox company's system setup
completed first — SSN and address; Payday returns 401 "complete the required system setup" until then.)

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

(Note: reads of stock and movements can lag Payday's background jobs by about a minute — see 8.
Never conclude "no effect" from an immediate read.)

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

### 8. Invoicing a product line DOES move stock — asynchronously, when the invoice is issued

```
stock before: 10.0
POST /invoices  lines:[{"quantity":12,"unitPriceExcludingVat":1500,"vatPercentage":0,"productId":"<cable>"},
                       {"quantity":3,"unitPriceExcludingVat":12000,"vatPercentage":0,"sku":"SPIKE-LABOR"}]
200 {"status":"SENT","number":null,"amountExcludingVat":54000.0,...}     (22:18:09)
stock 0 s and 20 s later: 10.0, movements unchanged                        ← looked like "no effect"
… ~55 s later the invoice is issued (number: 1) and a movement appears:
  {"description":"Reikningur nr. 1 - Spike kúnni ehf.","changeInQuantity":-12.0,"quantityAfterChange":-2.0,
   "salesUnitPriceExcludingVAT":1500.0,"created":"2026-09-19T22:19:03Z"}
stock now: -2.0 ; labor product quantity: still null
```

First read said "no", the corrected read an hour later says "yes": Payday books the sale into
inventory when the invoice is **issued**, which happens on a background job roughly a minute after
creation (until then `number` is null and the invoice is internally `Pending`). Negative stock is
allowed. A service product (`quantity: null`) gets no movement.

**Design consequence — the big one, reversed from the first draft:** if Workit consumed stock at
usage time *and* billed the material by `productId`, the quantity would be taken twice. Options:

- (a) **Usage does nothing to Payday stock; the invoice consumes.** Workit's usages are the plan,
  Payday's invoice is the fact. Stock in Payday then lags reality until the job is billed — for a
  monthly-billed service job that can be weeks. Simple, no double count, no reversal logic.
- (b) **Usage consumes via movement; invoice lines are sent *without* `productId`/`sku`** (free-text
  line with price + VAT + the product's ledger account is not settable per line — check). Stock is
  live, but the invoice loses its product link and Payday's sales-per-product reporting.
- (c) **Usage consumes via movement; at invoice time Workit posts a compensating `+qty` movement**
  right after creating the invoice. Live stock and a product-linked invoice, at the cost of a
  timing window (the compensation lands before Payday's own −qty, so stock briefly reads high) and
  a noisy movement ledger.

Recommendation: **(a)** for v1 — it is exactly how Payday itself behaves when someone types an
invoice by hand, it keeps one truth, and the "stock lags until billed" gap can be shown in Workit as
"logged, not yet invoiced" per material from Workit's own usages. Revisit (c) only if owners need
live Payday stock. Also: a `sku`-only line comes back with `productId` filled and the description
rewritten to `"<SKU> - <name>"`. VAT lines are refused until the company has a VAT number.

## Still open

1. Timesheet item-name matching (`Dagvinna`/`Yfirvinna` vs the company's payroll items) and whether a
   re-upload replaces or adds — needs a sandbox company with at least one payroll employee.
2. Whether `createClaim` / `createElectronicInvoice` work in the sandbox (`hasClaimCollection: false`
   on this company) — only matters for Phase 3.

## Lifecycle rules confirmed while cleaning up

| Action | Result |
|---|---|
| `DELETE /invoices/{id}` on a SENT invoice | `400 Issued invoice … cannot be deleted. Consider cancelling the invoice instead.` |
| `PUT /invoices/{id} {"status":"CANCELLED"}` right after creation | `400 Invoice with status Pending cannot be updated to status CANCELLED` (status is `SENT` on read but `Pending` internally for a while) |
| `DELETE /products/{id}` once on an invoice | `400 … Only products that don't have invoices, estimates or recurring invoices can be deleted.` |
| `DELETE /customers/{id}` once invoiced | `400 {"errorCode":21001,"errorMessage":"Deleting customer … not allowed…"}` — a third error shape |

## Design decisions these settle

- Stock: **the invoice consumes** (8). Usages do not post movements in v1 (option a) — Workit shows "logged, not yet invoiced" from its own data instead. No reservation exists (1). If Workit ever posts movements, reversal = positive movement and never `quantity` on PUT (2).
- Product updates from Workit never carry `quantity` (2). Workit doesn't edit products at all in v1 anyway.
- Material vs labor default from `quantity == null`, owner-overridable (3).
- Invoice lines always carry price + VAT from the Workit cache; `productId` is for the ledger and Payday rewrites the description to `SKU - name` (4, 8). A company needs a VAT number in Payday before VAT lines work — surface that in onboarding.
- Cache paging by `pages`; movements are already a ledger we can show in the console (5).
- Error handling: string or `{message}` bodies, surfaced verbatim (6).
- Payroll export reports `employeeSSNsNotOnRecord` back to the owner (7).

## Cleanup

Left in the sandbox company "ÓS rafverktakar ehf." (they are referenced by the test invoice and
cannot be deleted; Payday wipes the sandbox periodically): products `SPIKE-LABOR`, `SPIKE-CABLE`
(5 movements), customer "Spike kúnni ehf.", invoice nr. 1 `c784ff10…` (54 000 kr., 0 % VAT, not emailed); stock of `SPIKE-CABLE` is now −2.
