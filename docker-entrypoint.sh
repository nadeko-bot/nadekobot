#!/bin/sh

set -e

data_init="/app/data_init"
data="/app/data"

ls $data
# Merge data_init into data without overwrites.
# TODO: Barkranger will hopefull fix this, move for testing
cp -R -n $data_init/* $data
cp -n "$data_init/creds_example.yml" "$data/creds.yml"

ls $data

echo "Yt-dlp update"
# TODO: Update yt-dlp. It should not crash the entrypoint if ca-certificates is not installed
# yt-dlp -U

echo "Running NadekoBot"
exec "$@"