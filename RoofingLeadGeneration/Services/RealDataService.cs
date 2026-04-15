using System.Text.Json;

namespace RoofingLeadGeneration.Services
{
    /// <summary>
    /// Wraps the three free external data sources:
    ///   1. OpenStreetMap Overpass API  — real nearby addresses   (no key needed)
    ///   2. NOAA SWDI API               — real hail event history  (no key needed)
    ///   3. Regrid Parcel API           — property owner names     (free 25/day token)
    ///
    /// Sign-ups:
    ///   Regrid  → https://app.regrid.com  (free Starter account, 25 lookups/day)
    ///   Then add your token to appsettings.json: "Regrid": { "Token": "YOUR_TOKEN" }
    /// </summary>
    public class RealDataService
    {
        private readonly IHttpClientFactory _httpFactory;
        private readonly IConfiguration     _config;
        private readonly ILogger<RealDataService> _logger;

        public RealDataService(IHttpClientFactory factory, IConfiguration config, ILogger<RealDataService> logger)
        {
            _httpFactory = factory;
            _config      = config;
            _logger      = logger;
        }

        // ─────────────────────────────────────────────────────────────────
        // 1. OpenStreetMap Overpass  –  real residential addresses
        //    No key needed. Rate limit: 1 req/sec recommended.
        //    Docs: https://wiki.openstreetmap.org/wiki/Overpass_API
        // ─────────────────────────────────────────────────────────────────
        public async Task<List<OsmAddress>> GetNearbyAddressesAsync(
            double lat, double lng, double radiusMiles)
        {
            double radiusMeters = radiusMiles * 1609.34;

            // Query for nodes and ways that have both a house number and a street
            var query = $@"[out:json][timeout:25];
(
  node[""addr:housenumber""][""addr:street""](around:{radiusMeters:F0},{lat},{lng});
  way[""addr:housenumber""][""addr:street""](around:{radiusMeters:F0},{lat},{lng});
);
out center;";

            try
            {
                using var client  = _httpFactory.CreateClient("overpass");
                var       content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("data", query)
                });

                var resp = await client.PostAsync(
                    "https://overpass-api.de/api/interpreter", content);

                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Overpass API returned {Status}", resp.StatusCode);
                    return new List<OsmAddress>();
                }

                var json = await resp.Content.ReadAsStringAsync();
                return ParseOverpassAddresses(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Overpass API call failed");
                return new List<OsmAddress>();
            }
        }

        private static List<OsmAddress> ParseOverpassAddresses(string json)
        {
            using var doc  = JsonDocument.Parse(json);
            var       list = new List<OsmAddress>();

            if (!doc.RootElement.TryGetProperty("elements", out var elements))
                return list;

            foreach (var el in elements.EnumerateArray())
            {
                if (!el.TryGetProperty("tags", out var tags))           continue;
                if (!tags.TryGetProperty("addr:housenumber", out var hn)) continue;
                if (!tags.TryGetProperty("addr:street",      out var st)) continue;

                double elLat, elLng;
                var type = el.GetProperty("type").GetString();
                if (type == "node")
                {
                    elLat = el.GetProperty("lat").GetDouble();
                    elLng = el.GetProperty("lon").GetDouble();
                }
                else if (el.TryGetProperty("center", out var center))
                {
                    elLat = center.GetProperty("lat").GetDouble();
                    elLng = center.GetProperty("lon").GetDouble();
                }
                else continue;

                var city  = tags.TryGetProperty("addr:city",     out var c) ? c.GetString() ?? "" : "";
                var state = tags.TryGetProperty("addr:state",    out var s) ? s.GetString() ?? "" : "";
                var zip   = tags.TryGetProperty("addr:postcode", out var z) ? z.GetString() ?? "" : "";

                var addr = $"{hn.GetString()} {st.GetString()}";
                if (!string.IsNullOrEmpty(city))  addr += $", {city}";
                if (!string.IsNullOrEmpty(state)) addr += $", {state}";
                if (!string.IsNullOrEmpty(zip))   addr += $" {zip}";

                list.Add(new OsmAddress
                {
                    FullAddress = addr,
                    HouseNumber = hn.GetString() ?? "",
                    Street      = st.GetString() ?? "",
                    City        = city,
                    State       = state,
                    Lat         = elLat,
                    Lng         = elLng
                });
            }

            return list;
        }

        // ─────────────────────────────────────────────────────────────────
        // 2. NOAA Severe Weather Data Inventory (SWDI)  –  real hail events
        //    No API key needed. Data typically available 120+ days back.
        //    Dataset: nx3hail (NEXRAD Level-3 Hail Signatures)
        //    Docs: https://www.ncei.noaa.gov/products/severe-weather-data-inventory
        //
        //    NOTE: The SWDI API has a 1-year limit per query. We make two calls
        //    to cover the last 2 years of storm history.
        // ─────────────────────────────────────────────────────────────────
        public async Task<List<HailEvent>> GetSwdiHailEventsAsync(
            double lat, double lng, double radiusMiles)
        {
            // Expand bounding box 50% beyond search radius for better coverage
            double span  = (radiusMiles * 1.5) / 69.0;
            double west  = Math.Round(lng - span, 4);
            double east  = Math.Round(lng + span, 4);
            double south = Math.Round(lat - span, 4);
            double north = Math.Round(lat + span, 4);

            // Data is available from ~120 days ago and back (NOAA processing lag)
            var   end     = DateTime.UtcNow.AddDays(-121);
            var   start   = end.AddYears(-2);

            // SWDI max date range = 1 year: split into two queries
            var midpoint = end.AddDays(-(end - start).Days / 2);

            var allEvents = new List<HailEvent>();
            await FetchSwdiBatch(west, south, east, north, start,    midpoint, allEvents);
            await FetchSwdiBatch(west, south, east, north, midpoint, end,      allEvents);

            _logger.LogInformation("SWDI returned {Count} hail events near {Lat},{Lng}",
                allEvents.Count, lat, lng);

            return allEvents;
        }

        private async Task FetchSwdiBatch(
            double west, double south, double east, double north,
            DateTime from, DateTime to, List<HailEvent> results)
        {
            // URL format: /swdiws/json/{dataset}/{startYYYYMMDD}-{endYYYYMMDD}?bbox=W,S,E,N
            var url = $"https://www.ncei.noaa.gov/swdiws/json/nx3hail" +
                      $"/{from:yyyyMMdd}-{to:yyyyMMdd}" +
                      $"?bbox={west},{south},{east},{north}";
            try
            {
                using var client = _httpFactory.CreateClient("noaa");
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("SWDI returned {Status} for {Url}", resp.StatusCode, url);
                    return;
                }

                var json = await resp.Content.ReadAsStringAsync();
                ParseSwdiJson(json, results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SWDI batch fetch failed");
            }
        }

        private static void ParseSwdiJson(string json, List<HailEvent> events)
        {
            using var doc = JsonDocument.Parse(json);

            // SWDI can return { "data": [...] } or just [...]
            JsonElement arr;
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("data", out arr))
            { /* arr is set */ }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            { arr = doc.RootElement; }
            else return;

            foreach (var item in arr.EnumerateArray())
            {
                double? hLat  = GetDoubleField(item, "LAT");
                double? hLng  = GetDoubleField(item, "LON");
                double? size  = GetDoubleField(item, "MXHAILSIZE");

                if (hLat is null || hLng is null) continue;

                // Parse YYYYMMDDHHMMSS timestamp
                DateTime date = DateTime.UtcNow.AddYears(-1);
                if (item.TryGetProperty("ZTIME", out var zt))
                {
                    var s = zt.GetString() ?? "";
                    if (s.Length >= 8)
                        DateTime.TryParseExact(
                            s[..8], "yyyyMMdd", null,
                            System.Globalization.DateTimeStyles.None, out date);
                }

                events.Add(new HailEvent
                {
                    Lat        = hLat.Value,
                    Lng        = hLng.Value,
                    SizeInches = size ?? 0.75,
                    Date       = date
                });
            }
        }

        // ─────────────────────────────────────────────────────────────────
        // 3. Regrid Parcel API  –  property owner names
        //    Requires a free Regrid token (25 lookups/day on free Starter plan).
        //    Sign up at: https://app.regrid.com
        //    Add token to appsettings.json: "Regrid": { "Token": "..." }
        //    Docs: https://support.regrid.com/api/using-the-parcel-api-v1
        // ─────────────────────────────────────────────────────────────────
        public async Task<string?> GetRegridOwnerAsync(double lat, double lng)
        {
            var token = _config["Regrid:Token"];
            if (string.IsNullOrWhiteSpace(token)) return null;

            // GET /api/v1/parcel?lat={lat}&lon={lng}&token={token}&limit=1&radius=50
            var url = $"https://app.regrid.com/api/v1/parcel" +
                      $"?lat={lat}&lon={lng}&token={token}&limit=1&radius=50";
            try
            {
                using var client = _httpFactory.CreateClient("regrid");
                var resp = await client.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Regrid returned {Status}", resp.StatusCode);
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync();
                return ParseRegridOwner(json);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Regrid API call failed");
                return null;
            }
        }

        private static string? ParseRegridOwner(string json)
        {
            using var doc = JsonDocument.Parse(json);

            // Structure: { "parcels": { "features": [ { "properties": { "fields": { ... } } } ] } }
            if (!doc.RootElement.TryGetProperty("parcels",  out var parcels))  return null;
            if (!parcels.TryGetProperty("features",          out var features)) return null;
            if (features.GetArrayLength() == 0)                                 return null;

            var first = features[0];
            if (!first.TryGetProperty("properties", out var props)) return null;
            if (!props.TryGetProperty("fields",      out var fields)) return null;

            // Regrid uses different field names depending on county data source
            foreach (var name in new[] { "owner", "owner1", "owner_name", "ownerName", "OWNER_NAME" })
            {
                if (fields.TryGetProperty(name, out var v))
                {
                    var raw = v.GetString();
                    if (!string.IsNullOrWhiteSpace(raw))
                        return System.Globalization.CultureInfo.CurrentCulture
                                     .TextInfo.ToTitleCase(raw.ToLower().Trim());
                }
            }
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // Haversine distance helper (used by RoofHealthController)
        // ─────────────────────────────────────────────────────────────────
        public static double HaversineDistanceMiles(
            double lat1, double lng1, double lat2, double lng2)
        {
            const double R    = 3958.8;
            var          dLat = (lat2 - lat1) * Math.PI / 180;
            var          dLng = (lng2 - lng1) * Math.PI / 180;
            var          a    = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                              + Math.Cos(lat1 * Math.PI / 180)
                              * Math.Cos(lat2 * Math.PI / 180)
                              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        // ─────────────────────────────────────────────────────────────────
        // Shared helper
        // ─────────────────────────────────────────────────────────────────
        private static double? GetDoubleField(JsonElement el, string prop)
        {
            if (!el.TryGetProperty(prop, out var v)) return null;
            if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
            if (v.ValueKind == JsonValueKind.String &&
                double.TryParse(v.GetString(),
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var d))
                return d;
            return null;
        }

        // ─────────────────────────────────────────────────────────────────
        // DTOs
        // ─────────────────────────────────────────────────────────────────
        public record OsmAddress
        {
            public string FullAddress { get; init; } = "";
            public string HouseNumber { get; init; } = "";
            public string Street      { get; init; } = "";
            public string City        { get; init; } = "";
            public string State       { get; init; } = "";
            public double Lat         { get; init; }
            public double Lng         { get; init; }
        }

        public record HailEvent
        {
            public double   Lat        { get; init; }
            public double   Lng        { get; init; }
            public double   SizeInches { get; init; }
            public DateTime Date       { get; init; }
        }
    }
}
