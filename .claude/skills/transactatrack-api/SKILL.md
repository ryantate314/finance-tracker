---
name: transactatrack-api
description: Read and write Transactatrack data (categories, transactions, rules, accounts, owners, imports) via the local API on http://localhost:5080. Use when the user wants to query the ledger, edit categorization, manage rules/categories/accounts, or otherwise interact with Transactatrack data outside the Angular UI.
---

# Transactatrack API skill

You have read/write access to the Transactatrack API at `http://localhost:5080`. Use `curl` + `jq` from Bash.

## Hard rules

1. **Active family**: Every endpoint except `GET /api/families`, `GET /api/status`, and `/health/*` requires the `X-Family-Id` header. The user wants to **choose the family at the start of each session**. If you do not yet know which family to use:
   - Run `curl -s http://localhost:5080/api/families | jq` and present the list (name + id) to the user.
   - Ask which family to use. Remember the chosen `FamilyId` for the rest of the conversation.
   - If only one family exists, still confirm it before proceeding.
2. **Writes require confirmation**: Any `POST`, `PUT`, `PATCH`, `DELETE` must be presented to the user *before* execution with a one-sentence summary of what will change. Wait for an explicit go-ahead. Reads (`GET`) are free.
3. **Never invent IDs**. Always look up the GUID for a category/account/owner/etc. by name first (cache results within the conversation). If the lookup is ambiguous, ask the user to disambiguate.
4. **Preconditions**: Before touching anything, run `curl -s http://localhost:5080/api/status | jq '.api.status, .database.status'`. If the API is down, tell the user to `make api` rather than retrying in a loop.
5. **Family scope is silent**: The API filters every query by `X-Family-Id` automatically. A 404 on a known GUID usually means "wrong family" — double-check before assuming the row doesn't exist.

## Setup once per session

```bash
API=http://localhost:5080
# After asking the user which family to use:
FID=<paste-guid-here>
H=(-H "X-Family-Id: $FID")
```

Use `"${H[@]}"` in subsequent curls. (If a session restarts, re-ask.)

## Endpoint reference

All responses are JSON. Use `| jq` for readability. Enums are serialized as strings.

### Families (unscoped — no X-Family-Id)
| Method | Path | Body |
|---|---|---|
| `GET` | `/api/families` | — |
| `GET` | `/api/families/{id}` | — |
| `POST` | `/api/families` | `{"name": "..."}` |
| `PUT` | `/api/families/{id}` | `{"name": "..."}` |
| `DELETE` | `/api/families/{id}` | — (409 if dependents) |

### Owners
`GET|POST /api/owners`, `GET|PUT|DELETE /api/owners/{id}`
Body (create/update): `{"name": "..."}`

### Accounts
`GET|POST /api/accounts`, `GET|PUT|DELETE /api/accounts/{id}`
Body:
```json
{
  "ownerId": "<guid>",
  "name": "Checking",
  "institution": "Chase",
  "accountType": "Checking",
  "bankCode": "chase"
}
```
`accountType` ∈ `Checking | Savings | CreditCard | Loan | Investment | Cash | Other`.

### Categories
| Method | Path | Body |
|---|---|---|
| `GET` | `/api/categories` | — (returns categories + nested sub-categories) |
| `POST` | `/api/categories` | `{"name": "..."}` |
| `PUT` | `/api/categories/{id}` | `{"name": "..."}` |
| `DELETE` | `/api/categories/{id}` | — (409 if dependents) |
| `POST` | `/api/categories/{categoryId}/subcategories` | `{"name": "..."}` |
| `PUT` | `/api/categories/{categoryId}/subcategories/{id}` | `{"name": "..."}` |
| `DELETE` | `/api/categories/{categoryId}/subcategories/{id}` | — |

### Transactions
- **List** `GET /api/transactions` — query params:
  - `accountIds=<csv-of-guids>`
  - `categoryIds=<csv-of-guids>`
  - `from=YYYY-MM-DD`, `to=YYYY-MM-DD`
  - `q=<substring>` (matches Description or Merchant, case-insensitive)
  - `needsReview=true|false`
  - `page=1`, `pageSize=50` (max 200)
  - Only Committed transactions are returned.
  - Response: `{ "items": [...], "totalCount": N, "page": P, "pageSize": S }`
- **Recategorize** `PATCH /api/transactions/{id}` — body:
  ```json
  { "categoryId": "<guid|null>", "subCategoryId": "<guid|null>" }
  ```
  Setting `categoryId: null` clears both. If `subCategoryId` is set, it must belong to `categoryId`. Sets `CategorizationSource=Manual`, clears `NeedsReview` and `AppliedRuleId`.

### Category rules
| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/category-rules` | Ordered by Priority |
| `POST` | `/api/category-rules` | See body below |
| `GET\|PUT\|DELETE` | `/api/category-rules/{id}` | — |
| `PUT` | `/api/category-rules/order` | Body: `[{ "id": "...", "priority": N }, ...]` |

Rule body:
```json
{
  "priority": 10,
  "matchField": "Description",        // Description | Merchant | AmountRange
  "matchType": "Contains",            // Contains | Equals | Regex (ignored for AmountRange)
  "pattern": "STARBUCKS",             // required unless matchField=AmountRange
  "amountMin": null,                  // decimal? — only used when matchField=AmountRange
  "amountMax": null,
  "targetCategoryId": "<guid>",
  "targetSubCategoryId": null,        // must belong to targetCategoryId if set
  "scope": "Family",                  // Family | Account
  "accountId": null,                  // required when scope=Account
  "isEnabled": true
}
```

### Imports
| Method | Path | Notes |
|---|---|---|
| `GET` | `/api/imports` | List batches (newest first) |
| `GET` | `/api/imports/{id}` | Batch + preview rows |
| `POST` | `/api/imports` | Multipart upload (`accountId`, `file`) |
| `POST` | `/api/imports/{id}/commit` | Pending → Committed |
| `POST` | `/api/imports/{id}/discard` | Pending → Discarded |
| `DELETE` | `/api/imports/{id}` | Remove batch |
| `POST` | `/api/imports/{id}/rerun-rules` | Pending only |
| `POST` | `/api/imports/{id}/suggest-llm` | Pending only; 202 async |

CSV upload example:
```bash
curl -s -X POST "$API/api/imports" "${H[@]}" \
  -F "accountId=<guid>" -F "file=@/path/to/statement.csv" | jq
```

## Common workflow recipes

### List uncategorized transactions
```bash
curl -s "$API/api/transactions?needsReview=true&pageSize=200" "${H[@]}" \
  | jq '.items | map({id, date, amount, description})'
```

### Find category id by name
```bash
curl -s "$API/api/categories" "${H[@]}" \
  | jq -r '.[] | select(.name | ascii_downcase == "groceries") | .id'
```

### Bulk recategorize from a search
Plan first, present to user, then execute the loop after approval:
```bash
ids=$(curl -s "$API/api/transactions?q=STARBUCKS&pageSize=200" "${H[@]}" | jq -r '.items[].id')
cat_id=<groceries-guid>
for id in $ids; do
  curl -s -X PATCH "$API/api/transactions/$id" "${H[@]}" \
    -H 'Content-Type: application/json' \
    -d "{\"categoryId\":\"$cat_id\",\"subCategoryId\":null}" >/dev/null
done
```
For bulk writes (≥3 mutations), state the count and a sample before running.

### Create a rule
```bash
curl -s -X POST "$API/api/category-rules" "${H[@]}" \
  -H 'Content-Type: application/json' \
  -d '{
    "priority": 100,
    "matchField": "Description",
    "matchType": "Contains",
    "pattern": "STARBUCKS",
    "targetCategoryId": "<guid>",
    "scope": "Family",
    "isEnabled": true
  }' | jq
```

## Error handling

- `400` — validation failure; `title` describes it. Surface it; don't retry.
- `404` — wrong ID or wrong family (silent filter). Re-list and verify.
- `409` — referential integrity (deleting an entity with dependents) or state conflict (committing a non-pending batch). Explain to the user; don't force.
- Connection refused — API isn't running. Tell the user to `make api`. Do not loop.

## Output style for the user

After a read, summarize the data — don't dump giant JSON unless asked. For writes, report what changed in one line (`PATCH transactions/abc12… → Groceries`). Use `file_path:line_number` format only for code references, not API IDs.
