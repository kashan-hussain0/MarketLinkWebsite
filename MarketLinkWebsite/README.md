# MarketLink

MarketLink is an ASP.NET Core MVC application for local farmers-market pre-orders and pickup. Shoppers reserve produce from nearby farms, collect it at a market stall, and pay the farmer in cash at pickup.

## Requirements

- .NET 8 SDK or a newer SDK with the .NET 8 runtime available
- SQL Server (Express is fine)
- Internet access for map tiles and, if enabled, the hosted assistant model

## Database

```powershell
dotnet restore
dotnet ef database update --project MarketLinkWebsite/MarketLinkWebsite.csproj
```

The migrations live in `Data/Migrations`:

- `InitialCreate`
- `AddReviewHelpfulVotes`
- `AddWeeklyStockPlans`

The application also applies pending migrations at startup when `Database:ApplyMigrations` is `true`.

## Configuration

| Setting | Purpose |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | SQL Server connection string |
| `Database:ApplyMigrations` | Apply pending migrations on startup |
| `Database:Seed` | Seed roles, reference data and accounts on startup |
| `Seed:AdminEmail` / `Seed:AdminPassword` | Administrator account, required for the admin workspace |
| `Seed:MarketplaceData` | Seed the full demo marketplace dataset |
| `Seed:DemoPassword` | Shared password for the seeded farmer and customer accounts |
| `Catalog:UseDemoFallback` | Public demo catalogue when the database is unavailable, development only |
| `App:BaseUrl` | Public HTTPS origin used in password-reset emails |
| `Email:*` | SMTP settings for password reset, contact and account email |
| `Platform:Name` | Name shown in titles, navigation and notifications |
| `Platform:SupportEmail` / `Platform:SupportPhone` / `Platform:SupportAddress` | Contact details used by the Contact Us page and the assistant |
| `Platform:Latitude` / `Platform:Longitude` | Map pin for the team location |
| `Platform:CurrencySymbol` | Currency symbol used on every price |
| `Platform:MarketBookingWindowDays` | How many days ahead a pickup slot can be reserved |
| `Platform:AnnouncementBar` | Text of the scrolling strip at the top of every page |
| `Maps:Provider` | `openstreetmap` (default, no key needed) or `google` |
| `Maps:GoogleApiKey` | Browser key used only when `Maps:Provider` is `google` |
| `Ai:Provider` | `local` (default) or `gemini` |
| `Ai:ApiKey` | Hosted model key, required when `Ai:Provider` is `gemini` |
| `Ai:Models` | Comma separated model list, tried in parallel, first usable answer wins |
| `Ai:Model` | Single model, used when `Ai:Models` is empty |
| `Ai:Endpoint` | Hosted model endpoint |
| `Ai:AttemptTimeoutSeconds` | Seconds any one model may take before it is abandoned |
| `Ai:TotalTimeoutSeconds` | Seconds the whole hosted attempt may take before the local answer is used |

Move the connection string, every password and every API key into user secrets or environment variables before the code is published anywhere:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=...;Database=MarketLinkDB;Trusted_Connection=True;TrustServerCertificate=True"
dotnet user-secrets set "Seed:AdminPassword" "..."
dotnet user-secrets set "Maps:GoogleApiKey" "..."
dotnet user-secrets set "Ai:ApiKey" "..."
```

Leave `MultipleActiveResultSets` out of the connection string. MARS disables savepoints, which the checkout and approval transactions rely on.

Without an SMTP host the application logs email previews in development and keeps password-reset responses generic in production.

### Maps

OpenStreetMap needs no account or key and is the default, so maps work out of the box. Setting `Maps:Provider` to `google` and supplying `Maps:GoogleApiKey` switches every embedded map and pin to Google Maps. The admin settings page shows which provider is active and whether a key is present. An API key placed in `appsettings.json` is still visible to anyone who can read the source, so restrict it by domain in the Google Cloud console and move it to user secrets before launch.

### Market assistant

`Ai:Provider` set to `local` answers shopper questions entirely from the MarketLink database, which keeps the site working with no external dependency and no cost. Setting it to `gemini` with a valid `Ai:ApiKey` upgrades the assistant to a hosted model.

The hosted model is given a snapshot of the live markets, farms and produce on every question and is instructed to answer only from that data, so it cannot invent produce, prices or timings. It is also told to reply in plain text, and the chat panel renders its lines as real paragraphs and bullet lists rather than showing raw markup.

Hosted models occasionally report high demand, so the list in `Ai:Models` is called in parallel and the first usable answer wins. Each attempt is capped by `Ai:AttemptTimeoutSeconds` and the whole hosted attempt by `Ai:TotalTimeoutSeconds`, so a slow or unavailable model can never make the page hang. If nothing usable comes back, the request falls back to the local catalogue answer, so the assistant never goes silent. Answers that come from the hosted model are tagged in the chat panel.

## Seeded accounts

Set `Seed:AdminEmail`, `Seed:AdminPassword`, `Seed:MarketplaceData` and `Seed:DemoPassword`, then start the application once. Seeding is idempotent, so restarting never duplicates rows.

| Role | Email | Password |
| --- | --- | --- |
| Admin | value of `Seed:AdminEmail` | value of `Seed:AdminPassword` |
| Farmer | `maya@greenwoodfarm.test` | value of `Seed:DemoPassword` |
| Farmer | `bilal@razaorchard.test` | value of `Seed:DemoPassword` |
| Farmer | `sofia@sunrisedairy.test` | value of `Seed:DemoPassword` |
| Farmer | `james@millstreetbakery.test` | value of `Seed:DemoPassword` |
| Farmer | `emily@cedarhillapiary.test` | value of `Seed:DemoPassword` |
| Farmer | `daniel@brooksfamilymeats.test` | value of `Seed:DemoPassword` |
| Customer | `jordan.lee@example.test` | value of `Seed:DemoPassword` |
| Customer | `priya.sharma@example.test` | value of `Seed:DemoPassword` |
| Customer | `marcus.webb@example.test` | value of `Seed:DemoPassword` |
| Customer | `aisha.khan@example.test` | value of `Seed:DemoPassword` |

The demo dataset also creates nine categories, five markets, twenty-seven products with inventory and weekly stock plans, historical orders across every status, verified-purchase reviews, favourites, notifications and audit log entries. Change the passwords and set `Seed:MarketplaceData` to `false` before a public launch.

## Run

```powershell
dotnet run --project MarketLinkWebsite/MarketLinkWebsite.csproj
```

The public catalogue, customer account, farmer workspace and admin workspace are served from the same MVC application. In Visual Studio use Shift+F5 to stop a running instance, then Rebuild Solution and F5. If the debugger freezes on an exception, turn off Tools, Options, Debugging, General, Break on thrown.

## Roles

- `Customer`
- `Farmer`
- `Admin`

Farmer accounts begin with pending approval and are redirected to a waiting page until an administrator approves them. Every workspace is protected with role-based authorization, and a customer can only open their own orders, addresses and favourites.

The farmer workspace belongs to farmers. An administrator has no farm of their own, so the `ActiveFarmerAccess` policy grants it to the `Farmer` role only and an administrator is redirected to the access-denied page. Administrator oversight of growers happens in the admin area instead, through Farmers, Orders, Moderation and Reports. An administrator can open the customer area to support a shopper.

A suspended or pending farmer is refused by the `ActiveFarmerRequirement` handler, which checks both the farmer status and the linked user account on every request. Their produce also disappears from the public catalogue and their farm profile returns not found.

Suspending a farmer hides the farm and its listings but keeps the `Farmer` role, so the grower can still sign in and read their own account. Reactivating restores the role if it is ever missing and puts the listings that were only hidden by the suspension back on the marketplace, provided they still have stock in the inventory.

## Order responsibility

The farmer who supplies the products owns the reservation. Accepting, preparing, marking ready, completing, declining and cancelling all happen in the farmer workspace, and each change notifies the customer. A customer can edit or cancel their own reservation only while it is still pending. Once a farmer accepts it the farmer is preparing the items, so the customer is told to contact the market instead, and the farmer gains a cancel action with a mandatory reason that is shown to the customer on the order page.

The admin order area is a read-only record. It shows who ordered, which farm supplied each item, what was purchased, the real placed date and time, the payment state and a fourteen day volume chart. There is no admin approve or decline action, so the console does not become a bottleneck when many orders arrive at once. The admin still approves farmer applications, suspends and reactivates growers, moderates content and reads reports.

## Reviews and ratings

Any signed-in shopper can rate a farm and comment on a product, whether or not they have collected from it yet. A purchase is recorded as verified only when the shopper really has a completed order covering that farm or item, so the badge always means something. A shopper can review each farm or item once, and the page then switches from a review button to an already-reviewed badge instead of offering a duplicate. The review form offers every item that is currently listed, so a shopper is never forced to buy something first to say something about it.

Reviews appear on the public farm page and the product page for everyone, and in the farmer workspace where the farmer sees the headline and comment the shopper wrote and can reply to each one. The admin farmer list shows the real review count, how many are still unanswered and the most recent excerpt, all read from the database.

## Admin workspace

- Dashboard with platform totals, open orders and a live activity feed.
- Farmers with approve, reject, suspend with a reason, reactivate and a permanent delete. Deleting requires typing `DELETE` and is refused while any order is still open on the platform.
- Customers with activate and deactivate.
- Markets and product categories with full create, edit and delete, including operating days, timings and map coordinates.
- Order ledger, read only, grouped by farm.
- Moderation for inappropriate listings and reviews.
- Reports covering total orders, net sales, returning customers, refund rate, revenue by month, revenue across markets, most active farmers, top products, and a CSV export of all of it.
- Notifications for platform-wide announcements.
- Settings showing the platform identity, contact details, map provider, assistant configuration, live record counts and shortcuts to the master data screens.

## Storefront features

- Scrolling announcement marquee across the top of every page, paused on hover and disabled when the operating system requests reduced motion.
- Auto-rotating farmer spotlight carousel on the home page. It advances every 4.5 seconds, pauses on hover, loops continuously and reads directly from the database, so a newly approved farmer appears on its own.
- Light and dark theme switch in the navigation bar, the farmer top bar and the admin top bar. The choice is stored in the browser, applied before first paint to avoid a flash, and falls back to the operating system preference on a first visit. The dark palette is a soft green-tinted charcoal rather than near-black, defined with custom properties and covering cards, tables, forms, menus, alerts, both sidebars and every workspace. It also neutralises the Bootstrap light utilities the markup uses directly, such as `bg-white`, `text-dark`, `border`, `bg-light`, `table-light` and `btn-light`, so no white panel or near-black label is left behind on a dark page. Scrollbars are themed to match.
- Every page asks the browser for the visitor's location once, stores it in a cookie for ninety days, and from then on shows the true straight-line distance to each market. Distances are hidden rather than shown as zero when the location is not known yet.
- Fully circular category tiles on the home page, read from the database with a live item count under each name. The tiles are capped at 118 pixels so nine categories stay compact, every icon sits at the same height regardless of label length, and the icon area shrinks to thirty pixels. A category that has an image uses it, otherwise a matching icon is chosen from the name.
- Dynamic Categories dropdown in the navigation bar, listing every active category from the database with its thumbnail and live item count, plus a Portals dropdown that jumps straight to the customer, farmer or admin workspace.
- Live date and time under the trust line on the home page, refreshed every second in the browser.
- Catalogue filters on the produce page for category, market, market day, price and sort order, all populated from the database.
- Product and farm pages show real customer reviews with a star distribution, verified-purchase badges, helpful voting and farmer replies.
- Every product page names the farm that grew the item. The card shows the farm picture, the grower's name, the farm rating, how many items the farm currently has live, and which market it trades at, and the whole card links to the farm profile. Underneath it a "Where you collect this" panel lists the pickup stall with its address, opening hours and market day, the farm's own address with its trading days and pickup windows, and a map link for each, so a shopper can see both the stall and the farm before they order.
- Farm profile pages show a stock summary with how many items are in stock, how many units are reserved this week, how many markets the farm trades at and its rating. Every listing carries a stock badge, and the page embeds a real map of the farm with a facts panel listing the address, coordinates, trading days, pickup window, order cut-off and the markets the farm belongs to. The map pin marks where the farm grows and packs an order, which is not the same as a shop you can walk into.
- Market cards and the market page carry a real photograph, and a market without one falls back to a neutral stall icon rather than a broken image.
- The checkout page lists what each item in the basket is and which farm grew it, together with the markets that farm trades at. The market dropdown marks which markets can cover the whole basket and which are missing a farm, and if no single market can cover everything it names the conflicting farms and suggests splitting the order.
- The customer order page embeds a map of the pickup market with open-map and directions links, and lists every farm the order draws from with that farm's picture, item count, trading days, pickup windows, a link to the farm profile and a link to the farm's own location.
- Bootstrap, Bootstrap Icons and the icon font are served from `wwwroot/lib` instead of a public CDN, and the web fonts load without blocking the first paint. The application therefore has no render-blocking third-party request on any page.
- About Us and Contact Us pages. The contact page shows the configured team details, a working message form and an embedded map of the office location.
- Market map page with search and market-day filters and an embedded map of every active market.
- Market assistant that answers from the live catalogue: market days and addresses, directions links, farm ratings, live produce availability, pickup windows and how payment works. It understands multi-word questions such as "organic apples" and filters filler words out of the search. It is reachable by guests as well as signed-in shoppers, shows a typing indicator while it thinks, and tags answers that came from the hosted model.

## Session behaviour

Every sign-in stamps the cookie with an identifier created when the application starts. If the application is restarted, the previous cookie no longer matches and the browser is sent back to the login page, so a stale administrator session cannot survive a restart.

## Forms and media

Image fields are free text rather than strict URL fields, so a pasted address is never blocked by the browser. They show a live preview as you type, with a clear message when the address is malformed or the picture cannot load, and an explicit option to remove the current photo and fall back to the default farm image. Any image that fails anywhere on the site falls back to a neutral placeholder instead of a broken-image icon.

Create and edit forms block submission when a required field is empty and name the field that needs attention, then clear that message as soon as the form becomes valid. The server repeats every check so nothing invalid is ever stored.

## Scope

Payment collection and delivery logistics are intentionally out of scope. Orders use market pickup and cash at pickup. The market assistant answers from the MarketLink database by default, and the hosted model is optional, always grounded in that same database, and always falls back to the local answer.
