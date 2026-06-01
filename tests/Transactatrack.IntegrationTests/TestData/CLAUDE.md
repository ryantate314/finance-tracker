# Test fixtures — anonymize before committing

These files are checked into git. Bank exports in `data/` are **real** and must never be
copied here verbatim.

Before adding or updating any fixture derived from a real statement, replace every piece of
personal / identifying data with obviously-fake placeholders:

- **Account numbers** → all zeros (e.g. `0000000000000`)
- **Card last-four / share numbers** → `#0000`, `Share 00`
- **Merchant names & store IDs** → `SAMPLE MERCHANT`, `SAMPLE GROCERY #000`
- **Phone numbers** → `000-0000000`
- **Cities / states** → `ANYTOWN ST`
- **Account names / holder names** → generic (`SAMPLE CARD`)

Keep the file *structure* faithful (header preamble, column layout, sign conventions, blank
debit/credit columns, comment rows) so the parser is still exercised — only the values change.
When you tweak a fixture, update the matching assertions in `tests/Transactatrack.UnitTests/Imports/`.
