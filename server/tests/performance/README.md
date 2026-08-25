# Search performance verification

Use a disposable PostgreSQL 17 database and a non-production API deployment. Never run the
dataset against a database containing real users or files. Apply all migrations, then seed:

```bash
psql "$SEARCH_PERFORMANCE_CONNECTION" \
  --set SEARCH_PERFORMANCE_DATABASE_CONFIRMED=YES_DEDICATED_DATABASE \
  --file server/tests/performance/datasets/search-seed.sql
```

The dataset creates exactly 300,000 synthetic FileEntry rows, 10 synthetic users, direct and
inherited shares, six MIME categories, and both missing states. Values contain no production
names, paths, identifiers, or credentials. Generate short-lived access tokens for those users
through the isolated test deployment and keep them only in the process environment.

Warm-up and the measured run use 20 fixed cases, four virtual users, five minutes, a 100-item
maximum page, a p95 threshold below two seconds, and an error-rate threshold below one percent:

```bash
SEARCH_BASE_URL=https://api.performance.invalid \
SEARCH_TOKENS='<ten-comma-separated-short-lived-tokens>' \
k6 run server/tests/performance/k6/search.js
```

On the Raspberry Pi target, record p50, p95, maximum, error rate, CPU model/core count, physical
memory, index sizes, Migration elapsed time, and `EXPLAIN (ANALYZE, BUFFERS)` for short prefix,
trigram, and authorization traversal. Do not record query values, names, tokens, user identifiers,
or physical paths. Store only the redacted aggregate summary in `docs/testing/`.

Destroy the disposable database after the run. Do not attempt row-by-row cleanup in a shared
database.
