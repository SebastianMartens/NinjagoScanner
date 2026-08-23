## 1. PictureService: revive upload, add photo-URL issuance

- [ ] 1.1 In `NinjagoScanner.PictureService/Protos/picture_service.proto`, add a client-streaming `UploadPhoto` RPC (metadata message with original file name, then byte-chunk messages) and remove `AnalyzePhoto`/`AnalyzePhotoRequest`/`AnalyzePhotoResponse`.
- [ ] 1.2 Add a `GetPhotoDownloadUrl`-style RPC (single request/response) returning a short-lived presigned GET URL for a given photo ID.
- [ ] 1.3 Implement `UploadPhoto`: validate file extension (jpg/jpeg/png/bmp/webp) and non-empty content, generate a photo ID, write bytes to S3 via `PhotoStore` (new write path), create the sidecar record, trigger Gemini analysis, return the resulting `CardEntry`.
- [ ] 1.4 Implement the download-URL RPC using the same S3 client `PhotoStore` already holds.
- [ ] 1.5 Delete `IUploadUrlIssuer`/`S3UploadUrlIssuer`-equivalent code once nothing references it (it currently lives in `Web.Bff`, retired in section 3).
- [ ] 1.6 Update/port `NinjagoScanner.PictureService.Tests` for the new `UploadPhoto` and download-URL RPCs; remove tests for the deleted `AnalyzePhoto`.

## 2. AWS: PictureService's own IAM identity

- [ ] 2.1 Add a new Terraform module (e.g. `infra/modules/iam-user/`) provisioning an IAM user + scoped policy: `s3:GetObject`/`PutObject`/`DeleteObject`/`ListBucket` on the photo bucket's `photos/*` prefix, `dynamodb:GetItem`/`PutItem`/`DeleteItem`/`Scan` on the sidecar table.
- [ ] 2.2 Wire the module into `infra/environments/prod/main.tf` alongside `photo_storage`/`sidecar_table`.
- [ ] 2.3 Generate the access key via Terraform (or `aws iam create-access-key` once, out of band, matching the Gemini-secret pattern of not committing real credentials) and record how to set it as a Fly secret in `infra/README.md`.

## 3. NinjagoScanner.Web: the merged project

- [ ] 3.1 Create `NinjagoScanner.Web` project (Blazor Server, Interactive Server render mode), add it to `NinjagoScanner.slnx`.
- [ ] 3.2 Port `NinjagoScanner.Web.Bff/Services/CardCatalogService.cs` and `Services/PictureServiceClient.cs` into the new project largely as-is (gRPC-calling logic unchanged).
- [ ] 3.3 Port `NinjagoScanner.Web.Client`'s pages (`Collection.razor`, `Gallery.razor`, `Review.razor`, `Table.razor`, `Upload.razor`, `Overview`/home, `About`) into the new project, rewriting each page's data-fetching from `HttpClient`/JSON calls to direct `CardCatalogService` calls.
- [ ] 3.4 Rewrite the upload page to stream `IBrowserFile` bytes to PictureService via the new `UploadPhoto` client-streaming RPC (replacing the old `POST /uploads` → PUT-to-S3 → `POST /uploads/{id}/confirm` sequence), keeping the existing file-picker/camera-hint markup and the upload-in-progress button-disable behavior.
- [ ] 3.5 Wire photo display (gallery/table/review pages) to call the new download-URL RPC per photo and render the returned S3 URL directly as the image source.
- [ ] 3.6 Port `NinjagoScanner.Web.Client`'s `wwwroot/appsettings.json`-equivalent configuration (service addresses, max upload size) into the new project's config, reusing `web-app-configuration`'s existing resolution precedence.
- [ ] 3.7 Port relevant tests from `NinjagoScanner.Web.Bff.Tests` and `NinjagoScanner.Web.Client.Tests` into `NinjagoScanner.Web.Tests`.
- [ ] 3.8 Retire `NinjagoScanner.Web.Client`, `NinjagoScanner.Web.Bff`, `NinjagoScanner.Web.Shared`, and their `.Tests` projects — remove from `NinjagoScanner.slnx` and delete the directories.

## 4. Containerize and configure Fly.io

- [ ] 4.1 Write `NinjagoScanner.Web/Dockerfile` (same build-context pattern as the existing two Dockerfiles).
- [ ] 4.2 Add `fly.toml` to `NinjagoScanner.Web`, `NinjagoScanner.CatalogService`, `NinjagoScanner.PictureService`, each declaring the app name (`ninjago-scanner-web`/`-catalog-service`/`-picture-service`), the gRPC port + separate health-check port for the two internal services, and public-vs-private reachability (only `web` gets a public IP).
- [ ] 4.3 `flyctl apps create` for all three apps in the same Fly org/region.
- [ ] 4.4 Set Fly secrets: Gemini API key/model and the new AWS IAM user's access key/secret on `picture-service`; any config `web`/`catalog-service` need.
- [ ] 4.5 Confirm all three apps join the same Fly private network and resolve each other via `*.internal` DNS.
- [ ] 4.6 First manual `flyctl deploy` for each app; verify `web` → `catalog-service`/`picture-service` gRPC calls succeed over 6PN, verify upload and photo display end-to-end.
- [ ] 4.7 Confirm Fly's health checks correctly probe the separate health-check port (not the gRPC port) for `catalog-service`/`picture-service` — adjust `fly.toml`'s check config if Fly's behavior differs from the ECS-era assumption (see design.md Decision 6).

## 5. CI/CD

- [ ] 5.1 Add `.github/workflows/deploy-web.yml`, `deploy-catalog-service.yml`, `deploy-picture-service.yml` — path-filtered on push to `main`, using `flyctl deploy --config <project>/fly.toml` (via `superfly/flyctl-actions` or a plain `flyctl` install step) and a `FLY_API_TOKEN` repo/environment secret.
- [ ] 5.2 Confirm `.github/workflows/ci.yml` builds/tests the new `NinjagoScanner.Web` project and no longer references the retired `Web.Client`/`Web.Bff`/`Web.Shared` projects.

## 6. Documentation

- [ ] 6.1 Update `infra/README.md`'s layout section to include the new IAM-user module and describe Fly.io as the compute host (linking out to each project's `fly.toml` rather than duplicating Fly-specific detail in Terraform docs).
- [ ] 6.2 Update `CLAUDE.md`'s architecture section to describe `NinjagoScanner.Web` (Blazor Server, direct gRPC calls) and Fly.io hosting, replacing the stale WASM/BFF/AWS description.
