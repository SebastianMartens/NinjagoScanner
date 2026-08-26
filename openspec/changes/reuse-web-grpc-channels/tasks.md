## 1. Prerequisite check

- [x] 1.1 Confirm `add-opentelemetry-observability` is deployed to all
      three Fly apps and producing traces before starting this change
- [ ] 1.2 Capture (or locate, if already captured as part of that change's
      task 5.4) a "before" trace for a page that fires multiple backend
      calls (e.g. `/review`) showing repeated connection-setup cost per
      call

## 2. Apply the fix

- [x] 2.1 In `CatalogServiceClient`, construct one `GrpcChannel` for
      `catalogServiceAddress` in the constructor, store it as a field, and
      replace every method's local `GrpcChannel.ForAddress(...)` with that
      field
- [x] 2.2 Configure the channel's `SocketsHttpHandler` with a bounded
      `PooledConnectionLifetime` (e.g. 5 minutes) so it re-resolves DNS
      periodically instead of holding one connection indefinitely
- [x] 2.3 In `PictureServiceClient`, construct one `GrpcChannel` for
      `pictureServiceAddress` in the constructor, store it as a field, and
      replace every method's local `GrpcChannel.ForAddress(...)` with that
      field
- [x] 2.4 Configure the same bounded `PooledConnectionLifetime` on
      `PictureServiceClient`'s channel
- [x] 2.5 Remove now-unused `using var channel = ...` / `using var call =
      ...` disposal patterns that assumed a per-call channel, keeping
      per-call disposal only where it's about the RPC call itself (e.g.
      the client-streaming `UploadPhoto` call), not the channel

## 3. Verify

- [ ] 3.1 Build and run all three services locally; exercise scan, list,
      download-URL, and update flows through `NinjagoScanner.Web`,
      confirming identical behavior to before the change
- [x] 3.2 Run the existing test suite (`dotnet test NinjagoScanner.slnx`),
      including `PictureServiceClientGetCardsAsyncTests` and related client
      tests, to confirm no regression
- [ ] 3.3 With tracing active, load a page that fires multiple backend
      calls and capture an "after" trace; confirm repeated connection-setup
      spans are gone
- [ ] 3.4 Manually verify resilience: restart the target service locally
      (or redeploy it on Fly) while the Web app is running, then confirm a
      subsequent call succeeds within the configured
      `PooledConnectionLifetime` window rather than failing indefinitely

## 4. Deploy

- [ ] 4.1 Deploy `NinjagoScanner.Web` to Fly
- [ ] 4.2 Spot-check the deployed app: exercise the main flows, confirm
      traces in Grafana Cloud show single, reused connections per service
      instead of per-call setup
