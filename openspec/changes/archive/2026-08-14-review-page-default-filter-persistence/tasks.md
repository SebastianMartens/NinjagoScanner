## 1. Review page default

- [x] 1.1 Change the initial value of the `reviewStatusFilter` field in `Review.razor` from `AllFilterValue`/`"all"` to `ReviewStatuses.Unreviewed`.

## 2. Verification

- [x] 2.1 Add/update a test in `NinjagoScanner.Web.Tests` asserting the review page's review-status filter defaults to `Unreviewed`.
- [x] 2.2 Manually verify in the browser that opening `/review` shows the `Unreviewed`/"Nicht geprüft" segment selected and only unreviewed groups listed by default.
- [x] 2.3 Run `dotnet test NinjagoScanner.slnx` and confirm no regressions.
