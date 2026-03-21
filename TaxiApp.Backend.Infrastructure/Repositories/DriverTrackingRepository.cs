using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;
using TaxiApp.Backend.Infrastructure.Helper;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class DriverTrackingRepository : IDriverTrackingRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hub;
        private readonly ActiveTripStore _activeTripStore;

        public DriverTrackingRepository(
            ApplicationDbContext context,
            IHubContext<NotificationHub> hub,
            ActiveTripStore activeTripStore)

        {
            _context = context;
            _hub = hub;
            _activeTripStore = activeTripStore;
        }
        public async Task UpdateDriverLocationAsync(string driverId, decimal lat, decimal lng)
        {
            var driver = await _context.Drivers
            .FirstOrDefaultAsync(d => d.UserId == driverId);

            if (driver == null)
                return;

            // تحديث آخر موقع للسائق
            driver.LastLat = lat;
            driver.LastLng = lng;
            driver.LastSeenAt = DateTime.UtcNow;

            // حفظ في جدول المواقع
            var location = new DriverLocation
            {
                DriverId = driverId,
                Lat = lat,
                Lng = lng,
                RecordedAt = DateTime.UtcNow
            };

            _context.DriverLocations.Add(location);

            await _context.SaveChangesAsync();

            await BroadcastLocation(driverId, lat, lng);
        }

        private async Task BroadcastLocation(  string driverId,  decimal lat,  decimal lng)
        {
            // إرسال للمكتب دائماً
            await _hub.Clients
                .Group("office")
                .SendAsync("DriverLocationUpdated", new
                {
                    driverId,
                    lat,
                    lng
                });

            // استخدام ActiveTripStore بدل استعلام قاعدة البيانات
            if (!_activeTripStore.TryGetTrip(driverId, out int tripId))
                return;

            // إرسال للركاب فقط إذا كانت الرحلة نشطة
            await _hub.Clients
                .Group($"trip-{tripId}")
                .SendAsync("DriverLocationUpdated", new
                {
                    driverId,
                    lat,
                    lng
                });
        }
    }
}
