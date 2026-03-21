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

        public GoogleMapService(string apiKey)
        {
            _apiKey = apiKey;
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
