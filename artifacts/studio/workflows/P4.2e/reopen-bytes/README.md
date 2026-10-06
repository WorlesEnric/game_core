# Exact reopen asset bytes

The `.asset.gz` files decompress to the exact original YAML, including trailing spaces. `raw-sha256.json` records those uncompressed byte digests; they match the saved before/now maps. Compression avoids rewriting Unity YAML to satisfy whitespace checks. Diffs remain readable; no field is normalized for acceptance.
