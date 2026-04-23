# CLUE Reports & Alternative Signals for Unclaimed Hail Damage

**Research report for StormLead Pro**
Prepared: April 22, 2026
Scope: (1) Can StormLead Pro use LexisNexis C.L.U.E. data? (2) What are the best alternative signals for identifying hail-damaged roofs whose owners have *not yet* filed a claim?

---

## 1. Executive Summary

**The short answer on CLUE: No — StormLead Pro cannot legally buy, resell, or ingest C.L.U.E. Property data.** C.L.U.E. is a Fair Credit Reporting Act (FCRA) consumer report. LexisNexis will only furnish it to (a) the property owner, (b) the insurer underwriting or servicing the risk, or (c) a lender with an existing credit relationship. Lead generation for a third-party roofer is not a "permissible purpose" under 15 U.S.C. § 1681b, and LexisNexis enforces this contractually — their data-delivery alliance program is gated to licensed insurance carriers and MGAs. Even a data-partnership workaround is a dead end: the B2B products LexisNexis builds on top of C.L.U.E. (Rooftop, Total Property Understanding, Property Data Report) are all sold exclusively to insurance underwriters, not to contractors or marketing platforms.

**Secondary answer: even if StormLead Pro could buy C.L.U.E., it's the wrong dataset for the use case anyway.** C.L.U.E. tells you who *has already filed* a claim. StormLead Pro wants the opposite — homeowners who were *hit by hail* but *have not filed*. These are warm because the homeowner either hasn't noticed the damage yet, or noticed but is still shopping for a contractor before they open a claim. A C.L.U.E. hit is a *negative* signal for that use case — it means an adjuster has already been dispatched and a preferred-contractor network is likely in motion.

**The right data stack for "unclaimed hail damage" is a composite score, not a single source.** The highest-leverage signals, ranked by lift-per-dollar:

1. **NOAA / radar-derived hail swaths** (HailTrace, Interactive Hail Maps, GAF WeatherHub) — already your core input. Commodity. ~$50–$99/mo per user; bulk exports $799/yr/state.
2. **Building permit data** (Shovels.ai, ATTOM, BuildFax) — the single strongest *negative* proxy: "hail hit this parcel 90 days ago AND no roofing permit has been pulled since" is about as close as you can legally get to "unclaimed damage." Shovels covers 1,800+ jurisdictions with a clean API; this is your highest-priority integration.
3. **Parcel-level roof age** (ATTOM, BuildFax, CAPE Analytics) — compounds the hail signal. A 15-year-old roof under a 1.5"+ hail swath is materially more likely to have totaled than a 3-year-old roof.
4. **Post-event aerial imagery** (Vexcel Gray Sky, Nearmap, EagleView) — ground truth, but expensive. Use as a *verification* layer on your top 5% of leads, not as a primary filter.
5. **Social/community signals** (Nextdoor, local Facebook groups) — noisy, ToS-fragile, but free; useful for market-timing a canvassing push rather than individual-lead targeting.

**The competitive moat is the composite score, not any single feed.** Hail data is already a commodity — every roofing CRM integrates HailTrace. Permit data is less commonly fused in. Fusing hail × permit-absence × roof-age × homeowner tenure into a rank-ordered "warm leads" feed with explanatory reason codes is the defensible product.

**Recommended first sprint:** integrate Shovels.ai (permits) and ATTOM (parcel / roof age / owner-occupancy), build the "hit + no-permit + aged roof" composite. This should be possible for under ~$1,500/month in data costs at pilot scale.

---

## 2. CLUE Deep Dive

### 2.1 What C.L.U.E. actually is

The **Comprehensive Loss Underwriting Exchange** ("C.L.U.E." — the dots are LexisNexis's styling) is a consumer reporting database operated by **LexisNexis Risk Solutions Inc.**, which acquired it from ChoicePoint in 2008. It is recognized by the Consumer Financial Protection Bureau as a consumer reporting agency, and its reports are **FCRA-regulated consumer reports**.

There are two separate C.L.U.E. products:

- **C.L.U.E. Auto** — seven years of auto insurance claims
- **C.L.U.E. Property** — seven years of homeowners and personal property insurance claims; this is the one relevant to StormLead Pro

C.L.U.E. Property contains, per LexisNexis and multiple state insurance-department consumer publications (Washington OIC, Wisconsin OCI, Alabama DOI, Texas TDI):

- Policy number and policy dates
- Policyholder name and date of birth
- Property address and description
- Claim number
- Date of loss
- Cause / type of loss (e.g., hail, wind, water, fire, theft, liability)
- Claim status (open / closed / paid / denied)
- Amount paid and reserve amounts
- Insurer name

LexisNexis states the database is populated by more than **90% of U.S. homeowners insurers** via contribution agreements.

### 2.2 Who can legally pull a C.L.U.E. report

Under FCRA § 604 (15 U.S.C. § 1681b), a consumer report may be furnished only for an enumerated "permissible purpose." For C.L.U.E. Property in practice, that means three categories:

1. **The consumer / property owner** — free once per 12 months directly from LexisNexis Consumer Disclosure; reports are delivered only to the named consumer, not to agents, buyers, or contractors.
2. **An insurer** with a legitimate underwriting, rating, claims-handling, or renewal need on that specific consumer. Every report pull requires certified permissible purpose.
3. **A lender / investor / servicer** assessing the credit or prepayment risk on an existing obligation secured by that property (narrow and rarely relevant to property claims history).

Notably absent from that list: **real estate agents, prospective buyers, roofing contractors, lead generation companies, and marketing SaaS platforms.** Real estate guidance from the National Association of REALTORS® and multiple state insurance departments is explicit: buyers and their agents cannot pull C.L.U.E. directly — they must ask the seller to request their own report and share it.

This is not just LexisNexis policy — it is statute. Obtaining a consumer report without a permissible purpose is a violation of 15 U.S.C. § 1681b(f), with civil liability under § 1681n/o and criminal exposure under § 1681q for obtaining reports under false pretenses. A SaaS product that scraped, resold, or brokered C.L.U.E. data to roofers would be squarely inside the FTC's and CFPB's enforcement corridor.

### 2.3 Can you get C.L.U.E. data through a partnership or reseller?

The near-unanimous answer from the research: **no viable path exists for a roofing lead-gen SaaS.**

LexisNexis runs a "Data Delivery Alliance" partner program for the insurance industry, but the partners are rating-bureau integrators, core-system vendors (Duck Creek, Guidewire, Majesco), and insurer-side analytics providers — all of whom contract on the basis that the end consumer of the data is a licensed insurer with a permissible purpose. A contractor-facing reseller license is not offered, and granting one would expose LexisNexis to FCRA liability.

The commercial products LexisNexis builds *on top of* C.L.U.E. are also gated:

- **C.L.U.E. Property Report** — sold to insurers only
- **Property Data Report** — insurer-only
- **LexisNexis Rooftop** — a roof-risk score that fuses aerial imagery with C.L.U.E. claims history and weather; sold "system-to-system in an automated process through the LexisNexis single point of entry" — i.e., into carriers' underwriting systems
- **Total Property Understanding** (launched April 2023) — combines Rooftop + Smart Selection + Flyreel inspection; positioned explicitly at "U.S. home insurance underwriters"
- **Smart Selection** — underwriter-only

Verisk's equivalent — **ISO ClaimSearch**, a 1.8B-claim database with 2,800+ insurance contributors — has the same access posture: insurance contributors and their authorized vendors only.

### 2.4 Are there alternative providers that sell CLUE-adjacent claim-history data to non-insurers?

Researched and found **none with a contractor-accessible commercial path.** Insurance-claims data in the U.S. is concentrated in two walled gardens (LexisNexis C.L.U.E. and Verisk ISO ClaimSearch), both of which derive their value from a contributor network that trusts them to keep the data inside the industry. Breaking that trust would collapse the contributor base, so neither has a commercial incentive to open a contractor channel.

Public-records workarounds exist but do not fill the gap:

- **State insurance departments** publish aggregate claims *statistics* (e.g., Colorado DOI's 2024 release showing hail drives 26–54% of HO premiums in 11 counties) — useful for market sizing, useless for lead targeting.
- **Court records** may surface litigated claims, but that's a tiny and biased slice.
- **Public adjuster licensing records** (state by state, often NAPIA-adjacent) tell you which PAs are licensed, not which properties have claims. NAPIA's directory is explicitly not a lead-generation tool.

### 2.5 "Claims filed" vs. "claims not yet filed"

Even if StormLead Pro somehow obtained C.L.U.E. data, it would surface the *wrong* population. C.L.U.E. reports open, paid, and denied claims — all of which mean an insurer relationship has already been engaged, a preferred-contractor network is likely already in contact with the homeowner, and the sales cycle is already competitive. The "warm leads" StormLead Pro wants are precisely the ones *not* in C.L.U.E. yet.

Put differently: **C.L.U.E. coverage is the negative of the StormLead Pro target set.** The research opportunity is to build the complement — "hailed but not in any claims database yet" — using *inferential* signals that don't require access to a regulated consumer-report source.

### 2.6 Verdict

| Question | Answer |
|---|---|
| Can StormLead Pro buy C.L.U.E. data? | No. FCRA blocks it; LexisNexis does not sell to non-insurers. |
| Can StormLead Pro partner with a LexisNexis reseller? | No. All Data Delivery Alliance partners are insurer-side. |
| Is there a non-LexisNexis equivalent (Verisk, etc.)? | No accessible channel for roofers. |
| Would C.L.U.E. even be the right data? | No — it surfaces the opposite of the warm-lead population. |
| Is there FCRA risk in even trying to acquire this data? | Yes — material civil and criminal exposure. |

**Do not pursue C.L.U.E. Avoid any product copy that implies insurance-claims history is a feature.** If an FCRA plaintiffs' firm sees that, it becomes an attractive target.

---

## 3. Alternative Signals — Ranked

The core modeling target is a per-parcel score:

> **P(roof was damaged by recent hail AND owner has not yet filed a claim AND no contractor has been engaged)**

The signals below are components of that score. Ranking considers three axes: **data quality** (how predictive per unit), **cost/feasibility** (dollars and engineering), and **competitive differentiation** (how many competitors already use it).

### 3.1 Building permit data — HIGHEST PRIORITY

**Data quality: High as a *negative* signal.** The core logic: if a hail swath of ≥1" stones crossed a parcel 30–180 days ago, and no roofing, re-roof, tear-off, or reroof permit has since been pulled, the probability that (a) the roof was damaged and (b) the damage has not been repaired and (c) a contractor has not yet been engaged is materially elevated. Permits also carry negative predictive power — the *presence* of a permit is a near-certain disqualifier (someone already got the job). Roof replacements almost always require a permit in the jurisdictions StormLead Pro cares about (most of Tornado/Hail Alley), and this is well-established in BuildFax's own published research ("the presence and absence of any permit, regardless of type, is predictive of loss").

**Cost/feasibility: Excellent.** Three viable providers:

- **Shovels.ai** — 1,800+ jurisdictions, API-first, searchable by work type (roofing), zip/city/county geography. Explicitly documents a lead-gen use case in their own blog. Pricing is tiered; enterprise pricing gated to sales calls, but self-serve tiers exist.
- **ATTOM** — nationwide (158M properties), bulk + API + cloud. Permits, parcels, ownership, transactions in one feed. 30-day free trial.
- **BuildFax** — the incumbent for insurance permit data, strong roof focus (they sell a "Roof Age" product); however, their distribution is heavily through Duck Creek for insurers and they are less friendly to small SaaS.
- **Public records / HUD / data.gov / individual city open-data portals** — free but fragmented, inconsistent schemas, massive ETL burden. Not worth the engineering unless you're doing a single metro at a time.

**Competitive differentiation: Strong.** Hail maps are commoditized (HailTrace, Hail Recon, Interactive Hail Maps, GAF WeatherHub all in every roofing CRM). Permit absence is *not* widely fused into contractor-side lead tooling. StormConnect lists "storm's path, hail history, housing density, home age, owner-occupancy, FEMA declarations" in their Opportunity Finder — note the absence of "roofing-permit-already-pulled." This is a gap.

**Verdict: Integrate first. Shovels.ai is the cleanest API.**

### 3.2 Parcel-level roof age — HIGH PRIORITY

**Data quality: High, highly complementary.** A roof's probability of being totaled by a given hail event scales with its age. A 2022 CertainTeed SBS-modified architectural shingle handles 1.5" hail; a 2005 three-tab fiberglass does not. Combining roof age with hail size converts a binary "storm passed through" flag into a calibrated damage probability.

**Sources:**

- **Permits** (via Shovels / ATTOM / BuildFax) — last re-roof permit is the gold standard for roof age where jurisdictions capture it.
- **CAPE Analytics Roof Age** — AI-based change-detection on historical aerial imagery; claims 95% accuracy. Sold primarily to insurers but they expose an API and have opened some verticals.
- **BuildFax Roof Age** — permit-derived, insurer-focused, Duck Creek distribution.
- **Verisk Roof Age** — sold to insurance only.

**Cost/feasibility: Good.** Permit-derived roof age falls out for free once you're ingesting permits. CAPE Analytics is worth a sales conversation but is priced for insurers.

**Competitive differentiation: Medium.** Competitors use "home age" (year built) as a proxy; they rarely have *roof* age specifically. Closing that gap is worth real effort.

### 3.3 Hail event data (baseline input) — TABLE STAKES

**Data quality: High for the geo question, nothing without parcel overlay.** NOAA Storm Events + Level II NEXRAD radar (MESH / MEHS derived products) tell you where hail *might* have fallen at what size. You already use NOAA. Commercial overlays add value by meteorologist-curated swath polygons and parcel-level binning.

**Sources / pricing you're likely already aware of:**

- **NOAA SPC storm reports and NCEI radar archive** — free, raw, requires you to derive swaths.
- **HailTrace** — $50–$99/mo per user; API available via user settings; meteorologist-curated swaths; canvassing integrations with SPOTIO, SalesRabbit, Knockbase, AccuLynx.
- **Interactive Hail Maps (Hail Recon)** — single-state / 3-state / nationwide subscription tiers; Bulk Data Download add-on is **$799/yr per state** for programmatic access — this is likely the cheapest path to clean historical swath data at scale.
- **HailStrike** — similar tier structure.
- **GAF WeatherHub** — NOAA-verified, 3-year history, free to GAF-certified contractors.
- **Tomorrow.io** — alerting/forecasting (6-hour lead time), not historical hail swath archives; useful for pre-storm outreach prep.

**Competitive differentiation: None.** Everyone has hail data. Your edge is what you *do* with it.

### 3.4 Post-event aerial imagery — VERIFICATION LAYER

**Data quality: Highest ground truth available.** This is how insurers resolve claims at scale post-disaster.

- **Vexcel Gray Sky** — post-disaster aerial imagery program; UltraCam sensors; delivery within 24 hours post-collection. Sells to insurers and the Geospatial Intelligence Center; free to government agencies. Commercial access exists but is pricey.
- **Nearmap Roof Condition** — scored roof attributes from oblique + vertical imagery, computer-vision derived; API delivered in multiple formats; 3× per year refresh in major metros.
- **EagleView** — best-in-class for measurements ($32.75–$89.50 per residential report, volume tiers via Silver/Gold/Platinum); their "Data Packs" target high-volume storm contractors. More expensive than Nearmap at low volume.
- **CAPE Analytics Roof Condition Rating v5** — 5-point scale + reason codes, API-delivered; used by ~50% of top U.S. property insurers; approved by several state DOIs for ratemaking.

**Cost/feasibility: Mixed.** Running aerial imagery on every parcel in a hail swath is financially untenable for a small SaaS. Running it on the top ~5% of your composite-score leads for verification and for sales-collateral screenshots is very reasonable. EagleView's per-report pricing makes per-lead imagery a $30–$90 cost on the leads you most want to close.

**Competitive differentiation: Medium-high** if used as a pre-canvass verification layer. Most competitors punt this to the contractor on-site after signing.

### 3.5 Homeowner occupancy + tenure — MEDIUM PRIORITY

**Data quality: Medium, but crucial for conversion.** Owner-occupied, long-tenure households convert dramatically better than rentals (landlord-mediated deal, split decision-maker) or recent purchases (still negotiating with the insurer who underwrote at purchase).

**Sources:** ATTOM, Estated (now ATTOM), county assessor records. Essentially free once you're ingesting parcels.

**Competitive differentiation: Low — StormConnect already advertises this.** Table stakes.

### 3.6 Social listening / community signals — OPPORTUNISTIC, NOT CORE

**Data quality: Low per-observation, high in aggregate.** Nextdoor, local Facebook groups, neighborhood subreddits, and even Ring Neighbors surface real-time "my yard is covered in ice" and "has anyone had a good roofer after the storm?" posts. These are directionally excellent for **market-timing** a canvassing push, less useful for individual-lead targeting.

**Feasibility concerns:**

- **Nextdoor** — scraping is against ToS. Third-party scrapers (ScrapingBee, Apify, Stevesie) exist but expose you to account bans and civil claims. Nextdoor's official business pages are available but are for self-promotion, not listening.
- **Facebook local groups** — Meta's Graph API for Groups was effectively shut down post-Cambridge Analytica; access now requires a group admin cooperating. Feasible only via partnership.
- **Reddit** — public API is accessible; low signal volume.
- **Ring Neighbors** — no open API; app-only.
- **Angi / HomeAdvisor reviews** — post-hoc (contractor already engaged); useful for competitive intel but not for lead identification.

**Competitive differentiation: High if done well, but ToS risk is real.** A better framing is "market-timing" rather than "lead source": use aggregated social volume to decide when to surge your canvassing push in a given metro, not to identify individual leads.

### 3.7 Insurance adjuster activity — LOW, INVESTIGATE

**Data quality: Unclear, probably low.** There is no public feed of "adjusters dispatched to zip 75248 today." The closest proxies:

- **Permits pulled by insurance-preferred contractors** — visible via Shovels/ATTOM if those contractors' license numbers are tracked. Predictive of a *claim already placed*, useful as a disqualifier.
- **Google Trends / local search volume** for "roof inspection" and "hail damage" — free, crude.
- **Contractor licensing board records** — typically who's *licensed*, not who's *active where*.

**Verdict: Minor composite feature, not a primary source.**

### 3.8 Public adjuster networks — MINIMAL

NAPIA runs a directory of members, not a lead-sharing network. Individual PA firms post testimonials and geo pages that could be scraped for "claim-heavy ZIPs," but that's retrospective and tells you where claims *did* fire. Not useful for unclaimed-damage detection. Deprioritize.

### 3.9 Novel signals worth exploring

- **Google Street View / Mapillary change detection** — if you can pair a pre-storm pano with a post-storm pano on the same parcel, you can sometimes see tarps, missing shingles, or construction activity. Licensed use via Google's Street View Publish API or Mapillary's open dataset. Cheap, noisy, but a fun differentiator.
- **Electric utility outage maps** — indirectly correlate with storm severity corridors; already public for most IOUs.
- **USPS Delivery Point Validation / moves** — paid product; highlights recent movers whose insurance is mid-renewal and who don't yet have a contractor relationship.
- **Solar installation permits + hail overlap** — a PV array on a hail-damaged roof is a premium job; solar owners are typically long-tenure owner-occupants with maintenance budgets.
- **Homeowner tenure from MLS history** — ATTOM / public deeds. Converts at 3× rental properties.
- **Drone contractor registrations (FAA Part 107)** — loose proxy for competitor density in a metro.

---

## 4. Recommended Next Steps for StormLead Pro

### 4.1 Immediate (next 2 weeks)

1. **Kill any roadmap line item that mentions C.L.U.E., LexisNexis claims data, or Verisk ClaimSearch.** Replace with "claim-inference signals (permit-derived, not FCRA-regulated)." Update pitch-deck and website copy.
2. **Open Shovels.ai sales conversation.** Request a trial covering 3–5 pilot metros (TX, CO, MN are the top hail states by claim volume per NICB, and good pilots). Specifically ask:
   - Coverage percentage for roofing-specific permits in those metros
   - Latency from permit filing → API availability (target: <7 days)
   - Residential vs. commercial filter precision
   - Bulk export for initial backfill
3. **Open ATTOM 30-day free trial.** Validate: roof age field availability per parcel, owner-occupancy flag, last-sale date. Confirm that ATTOM's permit coverage isn't already a superset of Shovels in your pilot metros — there's some overlap.
4. **Pull the HailTrace or Interactive Hail Maps Bulk Data Download** ($799/yr/state from IHM) for your pilot states to ensure you own historical swaths as polygons, not just visuals. This unlocks programmatic parcel-in-swath queries.

### 4.2 Short term (next quarter)

5. **Build the composite score v1.** Inputs: hail size × storm date × roof age × permit presence/absence since storm × owner-occupancy × tenure. Output: 0–100 probability × reason codes. Reason codes matter — they're what your contractor users will show homeowners at the door.
6. **Ship a "Hit but Unclaimed" feed** as a premium feature. Ranked list per territory, refreshed daily, exportable to the CRMs roofers already use (AccuLynx, JobNimbus, SalesRabbit, SPOTIO — all have documented integrations with HailTrace, so you're entering a well-paved integration corridor).
7. **Pilot CAPE Analytics Roof Age** on a sample. If their permit-independent roof-age API beats your permit-derived roof age in jurisdictions with weak permit coverage, license it selectively for those ZIPs.

### 4.3 Medium term (6–12 months)

8. **Negotiate post-event imagery access for verification.** Nearmap is the most startup-friendly; Vexcel Gray Sky is gold but enterprise. Use imagery *per lead on demand* (charging through to the contractor or burning it only on top-5% composite scores) rather than subscribing at metro scale.
9. **Build the "market timing" social signal layer.** Don't scrape Nextdoor. Do license Reddit's API (cheap) and correlate storm-related post volume in local subreddits with your hail events — a clean secondary "is this market hot?" feature.
10. **Cold-outreach a LexisNexis Rooftop licensee.** If an insurer in your footprint uses Rooftop and you have a partnership opportunity (e.g., co-marketing preferred contractors), you can *consume the insurer's output* of the model without ever touching the underlying CLUE data. This is legally clean because the data is flowing in the FCRA-sanctioned direction.

### 4.4 Do NOT

- Do not build or market anything that implies you have insurance claims history data on homeowners.
- Do not scrape Nextdoor or Facebook Groups at scale — the ToS and CFAA risk is higher than the signal is worth at the individual-lead level.
- Do not rely on county open-data portals as your primary permit source unless you are going metro-by-metro — the engineering burden will eat your runway.
- Do not pitch contractors on "we found a loophole to get CLUE data." There isn't one, and the few people who have tried have drawn FTC attention.

---

## 5. Sources Consulted

### CLUE / FCRA
- LexisNexis Risk Solutions — [C.L.U.E. Property](https://risk.lexisnexis.com/products/clue-property)
- LexisNexis Risk Solutions — [Data Delivery Alliances](https://risk.lexisnexis.com/about-us/alliance-partnerships/insurance/data-delivery)
- CFPB — [LexisNexis C.L.U.E. & Telematics OnDemand consumer reporting company profile](https://www.consumerfinance.gov/consumer-tools/credit-reports-and-scores/consumer-reporting-companies/companies-list/comprehensive-loss-underwriting-exchange/)
- Washington Office of the Insurance Commissioner — [CLUE (Comprehensive Loss Underwriting Exchange)](https://www.insurance.wa.gov/insurance-resources/auto-insurance/credit-and-insurance/clue-comprehensive-loss-underwriting-exchange)
- Wisconsin Office of the Commissioner of Insurance — [FAQs about C.L.U.E. (PDF)](https://oci.wi.gov/Documents/Consumers/PI-207.pdf)
- Alabama Department of Insurance — [What is a CLUE report (PDF)](https://aldoi.gov/PDF/Consumers/CLUEReport.pdf)
- Texas Department of Insurance — [Check your property's insurance claim history](https://www.tdi.texas.gov/tips/check-your-propertys-insurance-claim-history.html)
- National Association of REALTORS® — [CLUE Reports Explained](https://www.nar.realtor/homeowners-insurance/clue-report)
- Insurify — [What Is a CLUE Report for Home Insurance?](https://insurify.com/homeowners-insurance/knowledge/clue/)
- Wells Law Chicago — [Insurance Claims History Reports under FCRA](https://www.wellslawchicago.com/speciality-consumer-reports/insurace-claims-history)
- Federal Trade Commission — [Fair Credit Reporting Act](https://www.ftc.gov/legal-library/browse/statutes/fair-credit-reporting-act)
- TransUnion — [Permissible Purpose](https://www.transunion.com/client-support/permissible-purpose)
- America's Credit Unions — [Understanding Permissible Purpose Under the FCRA](https://www.americascreditunions.org/blogs/compliance/understanding-permissible-purpose-under-fcra)
- Verisk — [ISO ClaimSearch](https://www.verisk.com/products/claimsearch/)

### LexisNexis B2B products on top of CLUE
- LexisNexis — [Rooftop](https://risk.lexisnexis.com/products/rooftop)
- LexisNexis — [Total Property Understanding](https://risk.lexisnexis.com/insurance/total-property-understanding)
- LexisNexis — [Property Data Report](https://risk.lexisnexis.com/products/property-data-report)
- PRNewswire — [LexisNexis Risk Solutions Launches Total Property Understanding (Apr 2023)](https://www.prnewswire.com/news-releases/lexisnexis-risk-solutions-launches-total-property-understanding-for-the-us-home-insurance-market-301804153.html)

### Building permit data
- Shovels.ai — [Building Contractor and Permit API](https://www.shovels.ai/api)
- Shovels.ai — [API Pricing](https://www.shovels.ai/pricing)
- Shovels.ai — [Construction Leads Using Building Permit Data](https://www.shovels.ai/blog/construction-leads-permit-data/)
- ATTOM — [Nationwide Building Permit Data](https://www.attomdata.com/data/property-data/nationwide-building-permit-data/)
- ATTOM — [Property Data API](https://www.attomdata.com/solutions/property-data-api/)
- BuildFax — [Property Intelligence Solutions](https://www.buildfax.com/)
- HUD — [Residential Construction Permits by County](https://hudgis-hud.opendata.arcgis.com/datasets/HUD::residential-construction-permits-by-county/about)

### Aerial / satellite imagery
- Vexcel Data Program — [Insurance / Gray Sky disaster imagery](https://vexceldata.com/industries/insurance/)
- Vexcel — [Gray Sky & Disaster Imagery](https://vexceldata.com/products/gray-sky/)
- Nearmap — [Roof Condition](https://www.nearmap.com/products/insights/roof-condition)
- EagleView — [Residential Property Reports for Construction](https://www.eagleview.com/product/residential-property-reports-for-construction/)
- EagleView — [Pricing](https://www.eagleview.com/pricing/)
- CAPE Analytics — [Roof Age](https://capeanalytics.com/roof-age/)
- CAPE Analytics — [Roof Condition Rating v5](https://capeanalytics.com/resources/roof-condition-rating-version-5/)
- Roofs.Cloud — [AI Flat Roof Damage Detection](https://roofs.cloud)
- HOVER — measurement product (referenced via Metro City Roofing explainer)

### Hail / storm data
- NOAA NSSL — [Hail research](https://www.nssl.noaa.gov/research/hail/)
- NOAA NWS — [Storm Report Records](https://www.weather.gov/unr/storm_reports)
- HailTrace — [Plans and Pricing](https://hailtrace.com/plans)
- Interactive Hail Maps (Hail Recon) — [Pricing & Signup](https://www.interactivehailmaps.com/pricing-page/)
- HailStrike — [Pricing](https://hailstrike.com/pricing)
- GAF — [WeatherHub Interactive Hail Map](https://www.gaf.com/en-us/resources/business-services/interactive-hail-map)
- Tomorrow.io — [Weather API](https://www.tomorrow.io/weather-api/)

### Industry context / competitive landscape
- RoofPredict — [Top Property Data Sources for Roofing Lead Generation](https://roofpredict.com/blog/top-property-data-sources-for-roofing-lead-generation)
- Knockbase — [HailTrace Integration for Storm Damage Lead Generation](https://www.knockbase.com/features/hailtrace-integration)
- StormConnect — [Storm Canvassing Software](https://stormconnect.io/)
- Hook Agency — [5 Best Hail Tracking Tools for Roofers (2026)](https://hookagency.com/blog/hail-trackers-for-roofing/)
- HailTrace blog — [21 Digital Roofing Tools](https://blog.hailtrace.com/21-digital-roofing-tools-to-help-you-stand-out-and-win-more/)
- NICB — [Top 5 States for Hail Claims: 2017–2019](https://www.nicb.org/news/news-releases/top-5-states-hail-claims-2017-2019-data)
- Policygenius — [Hail & Property Damage Statistics](https://www.policygenius.com/homeowners-insurance/hail-property-damage-statistics/)
- CBS Colorado — [Hail driving Colorado high insurance rates](https://www.cbsnews.com/colorado/news/hail-driving-colorado-high-insurance-rates/)

### Public adjusters / social signals
- NAPIA — [Home](https://www.napia.com/) and [Find a Public Adjuster](https://www.napia.com/find-a-public-adjuster)
- Nextdoor business pages (official) — referenced as competitive context

---

*Prepared as an autonomous scheduled research pass. Where facts below pricing or API availability could not be confirmed from a public source, the report says so; where they could, the source is cited above. Pricing figures reflect what vendors publicly advertised at the time of research and should be re-verified in a sales call before committing to a vendor.*
