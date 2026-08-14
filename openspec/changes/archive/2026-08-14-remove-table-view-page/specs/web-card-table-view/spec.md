## REMOVED Requirements

### Requirement: Cards are rendered as a grouped table

**Reason**: The `/table` page is redundant with the Gallery and Collection pages, which now cover the same card-browsing functionality. Maintaining three overlapping browsing views adds navigation clutter for no remaining benefit.

**Migration**: Use the Gallery page (`/gallery`) for tile-based browsing or the Collection page (`/collection`) for list-based browsing with filtering.

### Requirement: Rows can be grouped

**Reason**: Grouping controls were specific to the removed table view.

**Migration**: The Collection page groups cards by series; use its grouping instead.

### Requirement: Rows can be filtered

**Reason**: Free-text filtering was specific to the removed table view.

**Migration**: The Collection page provides equivalent filtering.

### Requirement: A card's set can be assigned inline from the table

**Reason**: Inline set assignment was specific to the removed table view.

**Migration**: Set assignment remains available from the Collection and Review pages.

### Requirement: Row details can be expanded inline

**Reason**: Inline detail expansion (error message / reasoning summary) was specific to the removed table view.

**Migration**: Card detail information remains viewable on the Collection page's card detail view.

### Requirement: A thumbnail opens an enlarged image preview

**Reason**: The image preview modal was specific to the removed table view.

**Migration**: The Gallery page's lightbox provides equivalent enlarged image viewing.
