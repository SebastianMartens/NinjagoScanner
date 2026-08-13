## 1. Remove statistics badges from Collection page

- [x] 1.1 Remove the `<div class="collection-stats">` block (and its contents) from `Collection.razor`
- [x] 1.2 Remove the `overview` variable and its computation (`CollectionOverviewResult`) from the Collection page code-behind if no other logic on that page references it
- [x] 1.3 Remove any now-unused `@using` directives or injected services that were only needed for the statistics computation

## 2. Clean up CSS

- [x] 2.1 Verify whether `.collection-stats` CSS rules are used by the Overview page or other pages
- [x] 2.2 If `.collection-stats` is not shared, remove the `.collection-stats` and `.collection-stats span` rules from `app.css`

## 3. Verify

- [x] 3.1 Build the solution and confirm no compilation errors
- [x] 3.2 Run the app and verify the collection page loads without badges and the Overview page statistics remain intact
