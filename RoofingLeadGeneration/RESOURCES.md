# StormLead Pro — External Resources & Dependency Analysis

**Last updated:** April 2026  
**Purpose:** Complete inventory of every external API, service, library, and CDN dependency used by StormLead Pro, with cost, usage, and usefulness assessment.

---

## 1. Data Sources (Hail & Weather)

### NOAA SWDI (Severe Weather Data Inventory)
- **URL:** `https://www.ncei.noaa.gov/swdiws/json/nx3hail`
- **Cost:** Free — no API key required
- **What it does:** Provides verified NEXRAD radar hail signatures going back 10+ years. The backbone of risk scoring.
- **Usage in app:** `RealDataService.GetNoaaHailAsync()` — queries by bounding box around a search location
- **Usefulness:** ⭐⭐⭐⭐⭐ Core data source. Cannot be replaced cheaply. Only weakness is a ~120-day lag on recent storms.
- **Risk if removed:** App loses historical hail data entirely. Critical dependency.

### Tomorrow.io
- **URL:** `https://api.tomorrow.io/v4/timelines`
- **API key required:** Yes (`TomorrowIo__ApiKey`)
- **Cost:** Free tier — 500 requests/day, 2-year lookback. Paid plans from $49/mo.
- **What it does:** Fills the NOAA 120-day lag gap with near-real-time hail data. Also powers the Storm Alert background service (polls every 30 min).
- **Usage in app:** `RealDataService.GetTomorrowIoHailAsync()`, `StormAlertService`
- **Usefulness:** ⭐⭐⭐⭐⭐ Critical for two reasons: (1) recent storm data that NOAA doesn't have yet, (2) the entire Storm Alerts feature depends on it. Without it, alerts don't work and searches miss storms from the last 4 months.
- **Risk if removed:** Storm Alerts go dark. Recent hail data disappears. Significant product degradation.
- **Watch:** 500 req/day free tier will become a constraint as users grow. Monitor usage before scaling.

### Mesonet / Iowa State LSR
- **URL:** `https://mesonet.agron.iastate.edu/geojson/lsr.php`
- **Cost:** Free — no API key required
- **What it does:** Local Storm Reports from NWS spotters. Third hail data source used as a supplement.
- **Usage in app:** `RealDataService.GetMesonetHailAsync()`
- **Usefulness:** ⭐⭐⭐ Good supplemental source. Adds spotter-confirmed events that radar may have missed. Free and reliable. Lower data volume than NOAA but useful for edge cases.
- **Risk if removed:** Minor degradation in data completeness. Not critical.

---

## 2. Property & Address Data

### OpenStreetMap / Overpass API
- **URL:** `https://overpass-api.de/api/interpreter`
- **Cost:** Free — no API key required
- **What it does:** Provides real residential property addresses within a search radius. The source of the lead list.
- **Usage in app:** `RealDataService.GetAddressesAsync()` — queries by bounding box
- **Usefulness:** ⭐⭐⭐⭐⭐ Core dependency. Every lead starts here. Free with no usage limits (fair use).
- **Risk if removed:** App has no properties to show. Critical.
- **Watch:** Address coverage varies by region. Rural and new-construction areas may be sparse. A fallback to Google Places or HERE API may eventually be needed.

### Regrid
- **URL:** `https://app.regrid.com/api/v2/parcels/`
- **API key required:** Yes (`Regrid__Token`)
- **Cost:** Free Starter — 25 lookups/day. Paid plans from ~$99/mo.
- **What it does:** Public parcel records — owner name, property type, year built, assessed value. Used for the enrichment step.
- **Usage in app:** `RealDataService` enrichment flow — called when user clicks the enrich (⚡) button on a lead
- **Usefulness:** ⭐⭐⭐⭐ High value — owner name alone is a significant lead qualifier. 25/day free limit is a real constraint for active users.
- **Risk if removed:** Owner name enrichment goes away. Whitepages Pro still provides phone/email. Degraded but functional.
- **Action needed:** Current token is a trial JWT. Rotate when upgrading to a paid plan. Watch expiry date.

---

## 3. Contact Enrichment

### Whitepages Pro
- **URL:** `https://api.whitepages.com/v2/property/`
- **API key required:** Yes (`WhitepagesPro__ApiKey`)
- **Cost:** ~$0.22/record on the 1,000/mo plan. Current key is a trial.
- **What it does:** Appends phone numbers and email addresses to enriched leads. Called after Regrid in the enrichment chain.
- **Usage in app:** `RealDataService` enrichment — secondary call after Regrid returns owner name
- **Usefulness:** ⭐⭐⭐⭐ Phone + email are the whole point of enrichment. High value per call. Cost per record needs to be factored into pricing once real usage begins.
- **Risk if removed:** No phone/email enrichment. Leads have owner names only. Significant degradation for outreach.
- **Action needed:** Trial key — replace with production key before launch.

### BatchSkipTracing
- **URL:** `https://api.batchskiptracing.com/api/lead`
- **API key required:** Yes (`BatchSkipTracing__ApiKey`) — currently blank
- **Cost:** ~$0.12/record
- **What it does:** Fallback enrichment when Whitepages Pro is not configured or fails. Single-contact return vs Whitepages multi-contact.
- **Usage in app:** `RealDataService` enrichment fallback
- **Usefulness:** ⭐⭐⭐ Good safety net at a lower price point. Useful as a budget option or for users who don't want Whitepages.
- **Risk if removed:** No fallback if Whitepages is down or unconfigured. Low risk — app degrades gracefully.
- **Action needed:** No key configured. Stays dormant until key is added.

---

## 4. Mapping & Geocoding

### Google Maps Platform
- **URLs:**
  - `https://maps.googleapis.com/maps/api/geocode/json` (geocoding)
  - `https://maps.googleapis.com/maps/api/js` (map + Places autocomplete)
- **API key required:** Yes (`GoogleMaps__ApiKey`)
- **Cost:** $200/mo free credit. Geocoding: $5/1,000 requests. Maps JS: $7/1,000 loads. Places: $17/1,000 requests. Well within free tier at current scale.
- **What it does:** (1) Address autocomplete on search bar and alerts form, (2) geocoding addresses to lat/lng, (3) interactive map display of results
- **Usage in app:** Frontend `Index.cshtml` (map + autocomplete), `AlertsController` (geocoding watched areas), `RoofHealthController` (reverse geocoding)
- **Usefulness:** ⭐⭐⭐⭐⭐ Core UI dependency. Map and autocomplete are primary UX elements.
- **Risk if removed:** Map goes blank. Address autocomplete breaks. Geocoding for alerts fails. Critical.
- **Watch:** Key is now rotated. Restrict to HTTP referrers in Google Cloud Console for production to prevent misuse.

### Leaflet.js
- **URL:** `https://cdnjs.cloudflare.com/ajax/libs/leaflet/1.9.4/`
- **Cost:** Free / open source
- **What it does:** Map rendering library. Google Maps JS provides the tile data; Leaflet handles the interactive map layer.
- **Usefulness:** ⭐⭐⭐⭐ Solid, lightweight. No issues.

---

## 5. Email Delivery

### Brevo (formerly Sendinblue)
- **SMTP:** `smtp-relay.brevo.com:587`
- **API key required:** Yes (`Email__Username`, `Email__Password`)
- **Cost:** Free — 300 emails/day forever
- **What it does:** Outbound SMTP relay for Storm Alert emails
- **Usage in app:** `EmailService` → `StormAlertService`, test email endpoint
- **Usefulness:** ⭐⭐⭐⭐⭐ Only outbound email provider. Storm Alerts don't work without it. Free tier is generous for alert-only use case.
- **Risk if removed:** Storm Alerts go silent.
- **Watch:** 300/day cap. At scale (many users, active storm season), this could be hit. Upgrade path: Brevo paid ($25/mo for 20k emails) or migrate to Amazon SES ($0.10/1,000).

### MailKit
- **NuGet:** `MailKit 4.7.1`
- **Cost:** Free / open source
- **What it does:** .NET SMTP client library. Handles TLS negotiation, authentication, and message sending.
- **Usage in app:** `EmailService.SendAsync()`
- **Usefulness:** ⭐⭐⭐⭐⭐ Industry standard for .NET email. No issues.

---

## 6. Authentication

### ASP.NET Core Cookie Auth
- **Cost:** Free (built-in)
- **What it does:** Session management for all logged-in users
- **Usefulness:** ⭐⭐⭐⭐⭐ Core auth layer. No issues.

### Google OAuth (`Microsoft.AspNetCore.Authentication.Google`)
- **API key required:** `Auth__Google__ClientId` / `Auth__Google__ClientSecret` — currently blank
- **Cost:** Free
- **What it does:** "Sign in with Google" button on the login page
- **Usefulness:** ⭐⭐⭐ Dormant — keys not configured. Good to have when ready to launch to reduce signup friction.

### Microsoft OAuth (`Microsoft.AspNetCore.Authentication.MicrosoftAccount`)
- **API key required:** `Auth__Microsoft__ClientId` / `Auth__Microsoft__ClientSecret` — currently blank
- **Cost:** Free
- **What it does:** "Sign in with Microsoft" button
- **Usefulness:** ⭐⭐ Lower priority than Google for this audience. Dormant.

---

## 7. Infrastructure

### Fly.io
- **URL:** fly.io
- **Cost:** Free hobby tier (shared CPU, 256MB RAM, 3GB storage). Paid from $1.94/mo for dedicated.
- **What it does:** Hosts the ASP.NET Core app. Docker-based deployment.
- **Usefulness:** ⭐⭐⭐⭐ Good fit for a small SaaS. Easy deploys, global edge, reasonable pricing. Fly secrets manager handles env vars cleanly.
- **Watch:** 256MB RAM may become a constraint as the data services scale. Monitor memory usage.

### SQLite + EF Core (`Microsoft.EntityFrameworkCore.Sqlite`)
- **Cost:** Free / open source
- **What it does:** Local database for users, leads, enrichments, watched areas, sent alerts
- **Usefulness:** ⭐⭐⭐⭐ Perfect for single-instance deployment. Zero ops overhead.
- **Watch:** Not suitable for multi-instance (horizontal scaling) or very high write concurrency. Migration path to PostgreSQL when needed — EF Core makes this straightforward.

---

## 8. Frontend Libraries (CDN)

### Tailwind CSS
- **URL:** `https://cdn.tailwindcss.com`
- **Cost:** Free
- **What it does:** Utility-first CSS framework. All UI styling.
- **Usefulness:** ⭐⭐⭐⭐⭐ Used everywhere. No issues.
- **Watch:** CDN version (`cdn.tailwindcss.com`) does not support purging unused CSS — fine for dev/demo, but for production a build step would reduce page size.

### Font Awesome 6.5
- **URL:** `https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.5.0/`
- **Cost:** Free (CDN version)
- **What it does:** Icon library used throughout the UI
- **Usefulness:** ⭐⭐⭐⭐⭐ Used on every page. No issues.

### Google Fonts (Inter)
- **URL:** `https://fonts.googleapis.com`
- **Cost:** Free
- **What it does:** Inter typeface used across all pages
- **Usefulness:** ⭐⭐⭐ Nice-to-have. Could be self-hosted to avoid the extra DNS lookup.

---

## 9. Summary Table

| Resource | Type | Cost | Criticality | Status |
|---|---|---|---|---|
| NOAA SWDI | Hail data | Free | 🔴 Critical | ✅ Active |
| Tomorrow.io | Hail data / Alerts | Free (500/day) | 🔴 Critical | ✅ Active |
| Mesonet LSR | Hail data | Free | 🟡 Supplemental | ✅ Active |
| OpenStreetMap / Overpass | Addresses | Free | 🔴 Critical | ✅ Active |
| Google Maps | Geocoding / Map | Free tier | 🔴 Critical | ✅ Active |
| Regrid | Owner name / parcel | Free (25/day) | 🟠 Important | ✅ Active, trial token |
| Whitepages Pro | Phone / email | ~$0.22/record | 🟠 Important | ✅ Active, trial key |
| BatchSkipTracing | Phone / email fallback | ~$0.12/record | 🟡 Fallback | ⚪ Dormant (no key) |
| Brevo SMTP | Alert emails | Free (300/day) | 🔴 Critical for alerts | ✅ Active |
| MailKit | Email library | Free | 🔴 Critical for alerts | ✅ Active |
| Fly.io | Hosting | Free tier | 🔴 Critical | ✅ Active |
| SQLite / EF Core | Database | Free | 🔴 Critical | ✅ Active |
| Google OAuth | Auth | Free | 🟢 Nice-to-have | ⚪ Dormant (no key) |
| Microsoft OAuth | Auth | Free | 🟢 Nice-to-have | ⚪ Dormant (no key) |
| Tailwind CSS | UI | Free | 🟠 Important | ✅ Active |
| Font Awesome | Icons | Free | 🟡 UI | ✅ Active |
| Google Fonts | Typography | Free | 🟢 Nice-to-have | ✅ Active |
| Leaflet.js | Map library | Free | 🟠 Important | ✅ Active |

---

## 10. Things to Watch / Action Items

- **Regrid trial token** — expires, can't be rotated. Sign up for a paid plan before launch and get a permanent key.
- **Whitepages Pro trial key** — replace with production key before charging users for enrichment.
- **Tomorrow.io 500 req/day** — monitor as user count grows. Each search + each watched area poll costs requests. Upgrade before hitting the cap.
- **Google Maps key restriction** — set HTTP referrer restrictions in Google Cloud Console to prevent unauthorized use of the key.
- **Tailwind CDN** — swap to a build-time Tailwind setup before production to reduce page load size.
- **SQLite → PostgreSQL** — plan migration when moving to multi-instance or if write contention becomes an issue.
- **Brevo 300/day cap** — fine for now. Watch during active storm seasons with many users.
- **BatchSkipTracing** — no key configured. Either add a key or remove the dead code path to reduce confusion.
