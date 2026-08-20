# NinjagoScanner.CardFotosMigration

One-time, disposable tool that copies the local `cardFotos/` directory (image + optional
`.json` sidecar per photo) into the cloud storage backend PictureService now uses: S3 for photo
bytes, DynamoDB for sidecar records, each photo keyed by a freshly generated photo ID. See
`openspec/changes/cloud-hosting-migration/design.md`'s "Migration — one-time copy, not move"
decision.

**Copy-only.** Nothing under `cardFotos/` is ever deleted or modified — verified by running it in
`--dry-run` mode against the full local `cardFotos/` (13,011 files, 6,833 images) with zero writes
and zero errors.

**Resumable.** Progress is tracked in a manifest file (default: a `.card-fotos-migration-manifest.json`
sibling of the source directory, never inside it) mapping each image file name to the photo ID it
was migrated to. Re-running skips anything already recorded there, so an interrupted run (network
blip, killed process, etc. — expected across ~1.6GB / ~13k files) can just be restarted.

## Usage

Requires AWS credentials via the standard SDK credential chain (env vars, shared credentials
file, or an assumed role) and the target S3 bucket + DynamoDB table to already exist (see
`infra/modules/photo-storage` and `infra/modules/sidecar-table`).

```powershell
dotnet run --project NinjagoScanner.CardFotosMigration -- `
  --bucket <photos-bucket-name> `
  --table <sidecar-table-name>
```

Options (all also settable via matching environment variables, e.g. `PHOTOS_BUCKET_NAME`):

| Flag | Default | Meaning |
|---|---|---|
| `--source` | auto-discovered `cardFotos/` next to the repo root | Directory to read photos from |
| `--bucket` | *(required)* | Target S3 bucket name |
| `--table` | *(required)* | Target DynamoDB table name |
| `--manifest` | `<parent of source>/.card-fotos-migration-manifest.json` | Progress-tracking file |
| `--parallelism` | `4` | Concurrent upload/write operations |
| `--dry-run` | `false` | Walk the source directory and report what *would* happen, without calling AWS |

Legacy sidecars written before the `AnalysisStatus` field existed (which used a plain `status`
key) are read correctly, same as PictureService's own legacy handling did.

After migration, spot-check a handful of records: compare a migrated DynamoDB item's
`SourceFileName`/`CardName`/`CardNumber` against the original sidecar JSON, and confirm the S3
object at `photos/{photoId}` opens as the same image.

Once migrated data has been verified, PictureService can be pointed at the new bucket/table (see
its `Storage:PhotosBucketName` / `Storage:SidecarTableName` configuration) — the local `cardFotos/`
directory is left in place afterward purely as an archive; nothing in the running app reads it
again.
