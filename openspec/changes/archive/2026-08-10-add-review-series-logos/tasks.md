## 1. Logo mapping

- [x] 1.1 In `Review.razor`'s `@code` block, add a `Dictionary<string, (string ImageFile, string Caption)>` (or equivalent small record) keyed by the exact series display string (e.g. `"Serie 2"`), mapping to the logo's image file name (under `wwwroot/images/`) and a caption string.
- [x] 1.2 Seed the dictionary with one real entry for `Serie 2` → `Series2_klein.jpg` with a real caption, as the worked example.

## 2. Button markup

- [x] 2.1 In the `review-series-buttons` loop, look up each `series` in the mapping; when found, render a small `<img>` inline in the button (before or alongside the label) with `src` pointing at the mapped image under `/images/` and `alt` set to the mapped caption.
- [x] 2.2 When the series has no mapping entry, render the button exactly as today - label only, no icon element.
- [x] 2.3 Confirm the button's `@onclick="() => ReassignSeriesAsync(photo, series)"` behavior is unchanged regardless of whether an icon is present.

## 3. Styling

- [x] 3.1 In `app.css`, add styling for the inline icon (~18-20px, vertically aligned with the label) scoped to `.review-btn`/`.review-series-buttons` so it doesn't affect other `.review-btn` usages on the page (e.g. status/nav buttons).

## 4. Verification

- [x] 4.1 Run the app and visually confirm: `Serie 2` shows its icon with correct alt text on hover/inspection; a series with no mapping (e.g. `Serie 1`) shows text only with no broken-image icon; clicking either type of button still reassigns the photo's series correctly.
