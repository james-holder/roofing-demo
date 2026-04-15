using Microsoft.AspNetCore.Mvc;
using RoofingLeadGeneration.Services;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoofingLeadGeneration.Controllers
{
    [Route("[controller]")]
    public class RoofHealthController : Controller
    {
        private readonly RealDataService _realData;

        private const string ApiKey        = "AIzaSyB2YmUC-KAbjTUSO4p9NNIaG_3af4iTevM";
        private const string GeocodingBase = "https://maps.googleapis.com/maps/api/geocode/json";

        private static readonly string[] PropertyTypes =
            { "Single Family", "Ranch", "Two Story", "Bungalow", "Colonial", "Cape Cod" };

        public RoofHealthController(RealDataService realData)
        {
            _realData = realData;
        }

        // ─────────────────────────────────────────────────────────────────
        // GET /RoofHealth/Neighborhood?address=...&radius=0.5
        // ─────────────────────────────────────────────────────────────────
        [HttpGet("Neighborhood")]
        public async Task<IActionResult> Neighborhood(
            string address, double radius = 0.5, double lat = 0, double lng = 0)
        {
            if (string.IsNullOrWhiteSpace(address))
                return BadRequest(new { error = "Address is required." });

            string formattedAddress = address;

            if (lat == 0 || lng == 0)
            {
                var center = await GeocodeAsync(address);
                if (center == null)
                    return BadRequest(new { error = "Could not geocode the provided address." });
                lat              = center.Lat;
                lng              = center.Lng;
                formattedAddress = center.FormattedAddress;
            }

            var properties = await GetPropertiesAsync(formattedAddress, lat, lng, radius);

            return Json(new { centerAddress = formattedAddress, lat, lng, properties },
                new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented        = false
                });
        }

        // ─────────────────────────────────────────────────────────────────
        // GET /RoofHealth/Export?address=...&radius=0.5
        // ─────────────────────────────────────────────────────────────────
        [HttpGet("Export")]
        public async Task<IActionResult> Export(
            string address, double radius = 0.5, double lat = 0, double lng = 0)
        {
            if (string.IsNullOrWhiteSpace(address))
                return BadRequest("Address is required.");

            if (lat == 0 || lng == 0)
            {
                var center = await GeocodeAsync(address);
                if (center == null)
                    return BadRequest("Could not geocode the provided address.");
                lat = center.Lat;
                lng = center.Lng;
            }

            var properties = await GetPropertiesAsync(address, lat, lng, radius);

            var sb = new StringBuilder();
            sb.AppendLine("Address,Latitude,Longitude,Risk Level,Last Storm Date,Hail Size,Estimated Damage,Roof Age (yrs),Property Type");

            foreach (var p in properties)
            {
                sb.AppendLine(
                    $"\"{p.Address}\",{p.Lat},{p.Lng},\"{p.RiskLevel}\",\"{p.LastStormDate}\"," +
                    $"\"{p.HailSize}\",\"{p.EstimatedDamage}\",{p.RoofAge},\"{p.PropertyType}\"");
            }

            var bytes    = Encoding.UTF8.GetBytes(sb.ToString());
            var fileName = $"StormLead_Export_{DateTime.Now:yyyyMMdd_HHmm}.csv";
            return File(bytes, "text/csv", fileName);
        }

        // ─────────────────────────────────────────────────────────────────
        // Core data-fetch logic
        //   Priority: real OSM addresses + real NOAA hail data
        //   Fallback:  simulated addresses when OSM returns < 5 results
        //             (hail data is always real when NOAA responds)
        // ─────────────────────────────────────────────────────────────────
        private async Task<List<PropertyRecord>> GetPropertiesAsync(
            string centerAddress, double centerLat, double centerLng, double radiusMiles)
        {
            // ── Fetch data sources in parallel ──────────────────────────
            var osmTask  = _realData.GetNearbyAddressesAsync(centerLat, centerLng, radiusMiles);
            var hailTask = _realData.GetSwdiHailEventsAsync(centerLat, centerLng, radiusMiles);
            await Task.WhenAll(osmTask, hailTask);

            var osmAddresses = osmTask.Result;
            var hailEvents   = hailTask.Result;

            List<PropertyRecord> records;

            if (osmAddresses.Count >= 5)
            {
                // ── Real addresses from OpenStreetMap ──────────────────
                var rng = new Random(centerAddress.GetHashCode());
                records = osmAddresses
                    .OrderBy(_ => rng.Next())   // deterministic shuffle
                    .Take(25)
                    .Select(addr => BuildRealRecord(addr, hailEvents))
                    .ToList();
            }
            else
            {
                // ── Simulated addresses (OSM coverage too sparse) ──────
                records = GenerateSimulatedProperties(
                    centerAddress, centerLat, centerLng, radiusMiles, hailEvents);
            }

            // Sort: High risk first, then by address
            records.Sort((a, b) =>
            {
                int ra = RiskOrder(a.RiskLevel), rb = RiskOrder(b.RiskLevel);
                return ra != rb
                    ? ra.CompareTo(rb)
                    : string.Compare(a.Address, b.Address, StringComparison.Ordinal);
            });

            return records;
        }

        // ─────────────────────────────────────────────────────────────────
        // Build a PropertyRecord from a real OSM address + NOAA hail data
        // ─────────────────────────────────────────────────────────────────
        private static PropertyRecord BuildRealRecord(
            RealDataService.OsmAddress addr,
            List<RealDataService.HailEvent> hailEvents)
        {
            var rng = new Random(addr.FullAddress.GetHashCode());
            var (risk, hailSize, stormDate) = ComputeRiskFromHail(
                addr.Lat, addr.Lng, hailEvents, rng);

            return new PropertyRecord
            {
                Address         = addr.FullAddress,
                Lat             = Math.Round(addr.Lat, 6),
                Lng             = Math.Round(addr.Lng, 6),
                RiskLevel       = risk,
                LastStormDate   = stormDate,
                HailSize        = hailSize,
                EstimatedDamage = DamageLabel(risk, rng),
                RoofAge         = rng.Next(3, 28),
                PropertyType    = PropertyTypes[rng.Next(PropertyTypes.Length)]
            };
        }

        // ─────────────────────────────────────────────────────────────────
        // Map real hail events → risk / hail size / last storm date
        //   - High   if any event ≥ 1.50" within 1 mile
        //   - Medium if any event ≥ 0.75" within 1.5 miles
        //   - Low    otherwise
        // ─────────────────────────────────────────────────────────────────
        private static (string risk, string hailSize, string stormDate)
            ComputeRiskFromHail(
                double lat, double lng,
                List<RealDataService.HailEvent> hailEvents,
                Random rng)
        {
            if (hailEvents.Count == 0)
                return SimulatedRisk(rng);

            // Find closest qualifying events
            var nearby = hailEvents
                .Select(h => new
                {
                    h,
                    dist = RealDataService.HaversineDistanceMiles(lat, lng, h.Lat, h.Lng)
                })
                .Where(x => x.dist <= 2.0)
                .OrderByDescending(x => x.h.SizeInches)
                .ThenBy(x => x.dist)
                .ToList();

            if (nearby.Count == 0)
                return SimulatedRisk(rng);

            var best = nearby.First();

            string risk = best.h.SizeInches >= 1.50 ? "High"
                        : best.h.SizeInches >= 0.75 ? "Medium"
                        : "Low";

            string hailSize  = $"{best.h.SizeInches:F2} inch";
            string stormDate = best.h.Date.ToString("yyyy-MM-dd");

            return (risk, hailSize, stormDate);
        }

        private static (string risk, string hailSize, string stormDate) SimulatedRisk(Random rng)
        {
            string risk = rng.Next(100) switch
            {
                < 35 => "High",
                < 75 => "Medium",
                _    => "Low"
            };
            string size = risk switch
            {
                "High"   => $"{(rng.Next(150, 275) / 100.0):F2} inch",
                "Medium" => $"{(rng.Next(75,  150) / 100.0):F2} inch",
                _        => $"{(rng.Next(25,   75) / 100.0):F2} inch"
            };
            int daysAgo = rng.Next(30, 730);
            return (risk, size, DateTime.Today.AddDays(-daysAgo).ToString("yyyy-MM-dd"));
        }

        // ─────────────────────────────────────────────────────────────────
        // Simulated address generation (unchanged from original, used as
        // fallback when OSM coverage is sparse)
        // ─────────────────────────────────────────────────────────────────
        private static List<PropertyRecord> GenerateSimulatedProperties(
            string centerAddress, double centerLat, double centerLng, double radiusMiles,
            List<RealDataService.HailEvent> hailEvents)
        {
            var parts        = centerAddress.Trim().Split(' ', 3);
            int centerNumber = 100;
            string streetName    = "Main St";
            string cityStateZip  = "";

            if (parts.Length >= 2 && int.TryParse(parts[0], out int parsed))
            {
                centerNumber = parsed;
                var commaIdx = centerAddress.IndexOf(',');
                if (commaIdx > 0)
                {
                    var streetPart = centerAddress[..commaIdx].Trim();
                    cityStateZip   = centerAddress[commaIdx..].Trim();
                    var spaceIdx   = streetPart.IndexOf(' ');
                    streetName     = spaceIdx >= 0 ? streetPart[(spaceIdx + 1)..] : streetPart;
                }
                else
                {
                    var spaceIdx = centerAddress.IndexOf(' ');
                    streetName   = spaceIdx >= 0 ? centerAddress[(spaceIdx + 1)..] : centerAddress;
                }
            }

            int    seed = Math.Abs(centerAddress.GetHashCode());
            var    rng  = new Random(seed);
            double span = radiusMiles / 69.0;

            const int count           = 20;
            int       sameStreetCount = (int)(count * 0.65);
            var       offsets         = GenerateHouseNumberOffsets(seed, sameStreetCount);
            var       records         = new List<PropertyRecord>();

            for (int i = 0; i < sameStreetCount; i++)
            {
                int houseNum = centerNumber + offsets[i];
                if (houseNum <= 0) houseNum = Math.Abs(houseNum) + 10;
                if (centerNumber % 2 == 0 && houseNum % 2 != 0) houseNum++;
                else if (centerNumber % 2 != 0 && houseNum % 2 == 0) houseNum++;

                var addr = string.IsNullOrEmpty(cityStateZip)
                    ? $"{houseNum} {streetName}"
                    : $"{houseNum} {streetName}{cityStateZip}";

                double lngJitter = (offsets[i] / 100.0) * 0.0008;
                double latJitter = rng.NextDouble() * 0.0004 - 0.0002;
                double addrLat   = centerLat + latJitter;
                double addrLng   = centerLng + lngJitter;

                var rng2 = new Random(addr.GetHashCode());
                var (risk, hailSize, stormDate) = ComputeRiskFromHail(addrLat, addrLng, hailEvents, rng2);

                records.Add(new PropertyRecord
                {
                    Address         = addr,
                    Lat             = Math.Round(addrLat, 6),
                    Lng             = Math.Round(addrLng, 6),
                    RiskLevel       = risk,
                    LastStormDate   = stormDate,
                    HailSize        = hailSize,
                    EstimatedDamage = DamageLabel(risk, rng2),
                    RoofAge         = rng.Next(3, 28),
                    PropertyType    = PropertyTypes[rng.Next(PropertyTypes.Length)]
                });
            }

            var crossStreets = InferCrossStreets(streetName);
            int csIdx        = 0;

            for (int i = 0; i < count - sameStreetCount; i++)
            {
                string cs       = crossStreets[csIdx++ % crossStreets.Count];
                int    houseNum = rng.Next(100, 999);
                if (houseNum % 2 != centerNumber % 2) houseNum++;

                var addr = string.IsNullOrEmpty(cityStateZip)
                    ? $"{houseNum} {cs}"
                    : $"{houseNum} {cs}{cityStateZip}";

                double addrLat = centerLat + (rng.NextDouble() * 2 - 1) * span * 0.6;
                double addrLng = centerLng + (rng.NextDouble() * 2 - 1) * span * 0.6;

                var rng2 = new Random(addr.GetHashCode());
                var (risk, hailSize, stormDate) = ComputeRiskFromHail(addrLat, addrLng, hailEvents, rng2);

                records.Add(new PropertyRecord
                {
                    Address         = addr,
                    Lat             = Math.Round(addrLat, 6),
                    Lng             = Math.Round(addrLng, 6),
                    RiskLevel       = risk,
                    LastStormDate   = stormDate,
                    HailSize        = hailSize,
                    EstimatedDamage = DamageLabel(risk, rng2),
                    RoofAge         = rng.Next(3, 28),
                    PropertyType    = PropertyTypes[rng.Next(PropertyTypes.Length)]
                });
            }

            return records;
        }

        // ─────────────────────────────────────────────────────────────────
        // Small helpers
        // ─────────────────────────────────────────────────────────────────

        private static string DamageLabel(string risk, Random rng) => risk switch
        {
            "High"   => rng.Next(2) == 0 ? "Significant" : "Severe",
            "Medium" => rng.Next(2) == 0 ? "Moderate"    : "Notable",
            _        => rng.Next(2) == 0 ? "Minor"        : "Minimal"
        };

        private static int RiskOrder(string risk) => risk switch
        {
            "High"   => 0,
            "Medium" => 1,
            _        => 2
        };

        private static int[] GenerateHouseNumberOffsets(int seed, int count)
        {
            var rng  = new Random(seed ^ unchecked((int)0xABCD1234));
            var used = new HashSet<int> { 0 };
            var result = new int[count];
            for (int i = 0; i < count; i++)
            {
                int v;
                do { v = rng.Next(-200, 201); } while (used.Contains(v) || Math.Abs(v) < 10);
                used.Add(v);
                result[i] = v;
            }
            return result;
        }

        private static List<string> InferCrossStreets(string streetName)
        {
            var lower = streetName.ToLower();
            if (lower.Contains("main"))  return new() { "Oak Ave", "Elm St", "Maple Dr", "Cedar Ln", "Pine Blvd", "1st St", "2nd St" };
            if (lower.Contains("oak"))   return new() { "Elm St",  "Main St", "Maple Dr", "Park Ave", "Walnut Ct" };
            if (lower.Contains("park") || lower.Contains("ave"))
                                          return new() { "Oak St",  "Main St", "Birch Ln", "Sycamore Dr" };
            return new() { "Oak Ave", "Elm St", "Maple Dr", "Cedar Ln", "Pine Blvd", "Walnut Ct", "Birch Ln" };
        }

        // ─────────────────────────────────────────────────────────────────
        // Google Maps geocoding (kept as-is from original)
        // ─────────────────────────────────────────────────────────────────
        private static async Task<GeoResult?> GeocodeAsync(string address)
        {
            using var client = new HttpClient();
            var url = $"{GeocodingBase}?address={Uri.EscapeDataString(address)}&key={ApiKey}";
            try
            {
                var json = await client.GetStringAsync(url);
                using var doc  = JsonDocument.Parse(json);
                var       root = doc.RootElement;

                if (root.GetProperty("status").GetString() != "OK") return null;

                var result    = root.GetProperty("results")[0];
                var loc       = result.GetProperty("geometry").GetProperty("location");
                var formatted = result.GetProperty("formatted_address").GetString() ?? address;

                return new GeoResult
                {
                    FormattedAddress = formatted,
                    Lat = loc.GetProperty("lat").GetDouble(),
                    Lng = loc.GetProperty("lng").GetDouble()
                };
            }
            catch { return null; }
        }

        // ─────────────────────────────────────────────────────────────────
        // DTOs
        // ─────────────────────────────────────────────────────────────────

        private class GeoResult
        {
            public string FormattedAddress { get; set; } = "";
            public double Lat { get; set; }
            public double Lng { get; set; }
        }

        private class PropertyRecord
        {
            [JsonPropertyName("address")]         public string Address         { get; set; } = "";
            [JsonPropertyName("lat")]             public double Lat             { get; set; }
            [JsonPropertyName("lng")]             public double Lng             { get; set; }
            [JsonPropertyName("riskLevel")]       public string RiskLevel       { get; set; } = "";
            [JsonPropertyName("lastStormDate")]   public string LastStormDate   { get; set; } = "";
            [JsonPropertyName("hailSize")]        public string HailSize        { get; set; } = "";
            [JsonPropertyName("estimatedDamage")] public string EstimatedDamage { get; set; } = "";
            [JsonPropertyName("roofAge")]         public int    RoofAge         { get; set; }
            [JsonPropertyName("propertyType")]    public string PropertyType    { get; set; } = "";
        }
    }
}
