/*using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Infrastructure.Helper
{
    public class GoogleMapService : IMapService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;

        public GoogleMapService(IConfiguration configuration)
        {
            _apiKey = configuration["GoogleMaps:ApiKey"];
            _httpClient = new HttpClient();
            
        }

        // Single ETA
        // ===============================
        public async Task<TimeSpan> GetETAAsync(
            decimal originLat,
            decimal originLng,
            decimal destLat,
            decimal destLng)
        {
            try
            {
                string url =
                    $"https://maps.googleapis.com/maps/api/distancematrix/json" +
                    $"?origins={originLat},{originLng}" +
                    $"&destinations={destLat},{destLng}" +
                    $"&mode=driving&key={_apiKey}";

                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                    return TimeSpan.MaxValue;

                var json = await response.Content.ReadAsStringAsync();

                using JsonDocument doc = JsonDocument.Parse(json);

                var rows = doc.RootElement.GetProperty("rows");

                if (rows.GetArrayLength() == 0)
                    return TimeSpan.MaxValue;

                var element = rows[0].GetProperty("elements")[0];

                if (element.GetProperty("status").GetString() != "OK")
                    return TimeSpan.MaxValue;

                var seconds = element
                    .GetProperty("duration")
                    .GetProperty("value")
                    .GetDouble();

                return TimeSpan.FromSeconds(seconds);
            }
            catch
            {
                return TimeSpan.MaxValue;
            }
        }

        // ===============================
        // Matrix ETA for many drivers
        // ===============================
        public async Task<List<TimeSpan>> GetDistancesAsync(
            List<DriverLocationDto> drivers,
            double destLat,
            double destLng)
        {
            try
            {
                var origins = string.Join("|",
                    drivers.Select(d => $"{d.Lat},{d.Lng}"));

                string url =
                    $"https://maps.googleapis.com/maps/api/distancematrix/json" +
                    $"?origins={origins}" +
                    $"&destinations={destLat},{destLng}" +
                    $"&mode=driving&key={_apiKey}";

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return drivers.Select(_ => TimeSpan.MaxValue).ToList();

                var json = await response.Content.ReadAsStringAsync();

                using JsonDocument doc = JsonDocument.Parse(json);

                var rows = doc.RootElement.GetProperty("rows");

                var result = new List<TimeSpan>();

                foreach (var row in rows.EnumerateArray())
                {
                    var element = row.GetProperty("elements")[0];

                    if (element.GetProperty("status").GetString() != "OK")
                    {
                        result.Add(TimeSpan.MaxValue);
                        continue;
                    }

                    var seconds = element
                        .GetProperty("duration")
                        .GetProperty("value")
                        .GetDouble();

                    result.Add(TimeSpan.FromSeconds(seconds));
                }

                return result;
            }
            catch
            {
                return drivers.Select(_ => TimeSpan.MaxValue).ToList();
            }
        }

        // ===============================
        // ETA for shared trip
        // ===============================
        public async Task<TimeSpan> GetAdditionalETAToTripAsync(
            Trip trip,
            Order newOrder)
        {
            if (!trip.TripOrders.Any())
                return TimeSpan.Zero;

            var lastOrder = trip.TripOrders.Last().Order;

            decimal startLat =
                lastOrder.DropoffLat ?? lastOrder.PickupLat;

            decimal startLng =
                lastOrder.DropoffLng ?? lastOrder.PickupLng;

            return await GetETAAsync(
                startLat,
                startLng,
                newOrder.PickupLat,
                newOrder.PickupLng);
        }
    }
}
*/



using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Infrastructure.Helper
{
    public class FakeMapService : IMapService
    {
        // سرعة تقريبية (km/h)
        private const double AVERAGE_SPEED_KMH = 40;

        // ===============================
        // حساب المسافة (Haversine)
        // ===============================
        private double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371; // نصف قطر الأرض بالكيلومتر

            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private double ToRadians(double angle) => angle * Math.PI / 180;

        // ===============================
        // تحويل المسافة لوقت
        // ===============================
        private TimeSpan ConvertDistanceToTime(double distanceKm)
        {
            // time = distance / speed
            var hours = distanceKm / AVERAGE_SPEED_KMH;

            // نحول لثواني
            var seconds = hours * 3600;

            // نضيف شوية عشوائية (traffic simulation)
            var random = new Random();
            seconds += random.Next(30, 180); // +30 إلى 180 ثانية

            return TimeSpan.FromSeconds(seconds);
        }

        // ===============================
        // Single ETA
        // ===============================
        public Task<TimeSpan> GetETAAsync(
            decimal originLat,
            decimal originLng,
            decimal destLat,
            decimal destLng)
        {
            try
            {
                var distance = CalculateDistanceKm(
                    (double)originLat,
                    (double)originLng,
                    (double)destLat,
                    (double)destLng);

                var eta = ConvertDistanceToTime(distance);

                return Task.FromResult(eta);
            }
            catch
            {
                return Task.FromResult(TimeSpan.MaxValue);
            }
        }

        // ===============================
        // Matrix ETA (عدة سائقين)
        // ===============================
        public Task<List<TimeSpan>> GetDistancesAsync(
            List<DriverLocationDto> drivers,
            double destLat,
            double destLng)
        {
            try
            {
                var result = new List<TimeSpan>();

                foreach (var driver in drivers)
                {
                    var distance = CalculateDistanceKm(
                        (double)driver.Lat,
                        (double)driver.Lng,
                        destLat,
                        destLng);

                    var eta = ConvertDistanceToTime(distance);

                    result.Add(eta);
                }

                return Task.FromResult(result);
            }
            catch
            {
                return Task.FromResult(
                    drivers.Select(_ => TimeSpan.MaxValue).ToList()
                );
            }
        }

        // ===============================
        // ETA للـ Shared Trip
        // ===============================
        public async Task<TimeSpan> GetAdditionalETAToTripAsync(
            Trip trip,
            Order newOrder)
        {
            if (!trip.TripOrders.Any())
                return TimeSpan.Zero;

            var lastOrder = trip.TripOrders.Last().Order;

            decimal startLat =
                lastOrder.DropoffLat ?? lastOrder.PickupLat;

            decimal startLng =
                lastOrder.DropoffLng ?? lastOrder.PickupLng;

            return await GetETAAsync(
                startLat,
                startLng,
                newOrder.PickupLat,
                newOrder.PickupLng);
        }
    }
}