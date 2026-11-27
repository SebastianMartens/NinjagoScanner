# NinjagoScanner

> **Draft.** Based on what the app does and says today. Target users and non-goals are agreed;
> the vision is not yet confirmed.

## Vision
NinjagoScanner lets Ninjago trading card collectors know which cards they have, which are
missing and which are spare, by photographing cards instead of typing them in. It makes
collecting more fun through progress, ranks and swapping with friends.

## Target users
Registration is open to anyone.

- **Children aged about 7–12 who collect** (the main audience): want to add cards quickly from a
  phone, see their collection fill up and find out what is still missing.
- **Parents**: help with photos and corrections, and need the app to be safe and free of costs
  for the child.
- **Enthusiasts of any age**: want a complete, searchable overview of a large collection.
- **Friends who collect**: want to see each other's collections and swap duplicates fairly.

Because children are the main audience, see [Safe for children](01-safe-for-children/_index.md),
including its urgent open point on card recognition.

German-speaking users come first, because Ninjago is most popular in German-speaking markets.
The interface is German only for now; other languages could follow. Cards can be German or
English.

## Non-goals
- **No chat or messaging** between users.
- **No public profiles**: only accepted friends can see a collection.
- **No monetisation**: no advertising, paid features, subscriptions or donations, and no hidden
  costs. It is a non-commercial hobby project.
- **No collecting of personal data**: a username and password are enough to register.
- **No guarantee** of availability, correct recognition or backups; deleted photos are gone.
- **No marketplace**: no buying, selling or price valuation of cards.
- **No other card games or collectibles** beyond Ninjago trading cards.

## How to read this tree
Goals are the numbered folders. Each goal's `_index.md` lists its capabilities; capabilities
contain features. A feature says what the user needs and how we know it is met, and links the
specs that build it. The format (front matter, status values) is maintained in Sebastian's
home repo.

Some specs belong to no goal on purpose, because they serve operation, development or speed:
observability, local-dev-environment, web-local-seed-mode, web-app-configuration,
web-page-render-performance, web-grpc-client-connection-reuse, catalog-service-service-info,
picture-service-aws-client-reuse, picture-service-aws-endpoint-override,
picture-service-nonblocking-presign, picture-service-sidecar-cache and
picture-service-directory-resolution.
