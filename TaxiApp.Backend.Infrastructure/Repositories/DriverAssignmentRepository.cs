using Azure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Core.Settings;
using TaxiApp.Backend.Infrastructure.Data;
using TaxiApp.Backend.Infrastructure.Helper;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class DriverAssignmentRepository : IDriverAssignmentRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> userManager;
        private readonly TaxiSettings _settings;
        private readonly IEtaCacheService _etaCache;
        private readonly INotificationRepository _notification;
        private readonly IHubContext<NotificationHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ActiveTripStore _activeTripStore;
        private readonly IMapService mapService;
        private readonly IMemoryCache _cache;
        private readonly IAdminAssignmentRepository adminAssignmentRepository;
        private readonly ISettingsRepository settingsRepository;

        private const string CACHE_KEY = "SYSTEM_MODE";

        public DriverAssignmentRepository(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,

            IOptions<TaxiSettings> settings,
            IEtaCacheService etaCache,
            INotificationRepository notification,
            IHubContext<NotificationHub> hubContext,
            IServiceScopeFactory scopeFactory,
            ActiveTripStore activeTripStore,IMapService mapService, IMemoryCache cache,IAdminAssignmentRepository adminAssignmentRepository)
        {
            _context = context;
            this.userManager = userManager;
            _settings = settings.Value;
            _etaCache = etaCache;
            _notification = notification;
            _hubContext = hubContext;
            _activeTripStore = activeTripStore;
            this.mapService = mapService;
            this._cache = cache;
            this.adminAssignmentRepository = adminAssignmentRepository;
            this.settingsRepository = settingsRepository;
        }


        // Assign Driver
        // ==========================
        public async Task<string> DriverAssignAsync(   Order order,string? excludedDriverId = null)
        {

            if (order == null) return "Order not found";

            var mode = await adminAssignmentRepository.GetModeAsync();

            if (mode == SystemMode.Manual)
            {
                return "System is manual";
            }

          

            if (order.IsManuallyAssigned)
            {
                return "Manual override active";
            }

            if (order.Priority==OrderPriority.Urgent)
            {
                return await AssignOrderUrgentAsync(order, excludedDriverId);
            }

            
            if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.SearchingDriver)
                return "Order not pending";

            var sharedDrivers = await GetSharedDrivers(order,  TimeSpan.FromMinutes(_settings.MaxSharedEtaMinutes), excludedDriverId);
            var nearDrivers = await GetNearestDrivers(order, TimeSpan.FromMinutes(_settings.MaxEtaMinutes), excludedDriverId);

            var candidates = sharedDrivers.Concat(nearDrivers).Where(c => c.DriverId != excludedDriverId) // 🔥 هذا هو الحل
    .ToList();

            if (!candidates.Any())
            {
                var queueDriver = await GetNextDriverFromQueueAsync();

                if (queueDriver == null)
                {
                    // إذا لم نجد أي سائق متاح، نغير الحالة لإيقاف الـ Background Service عن ملاحقة هذا الطلب
                    order.Status = OrderStatus.NoDriverFound;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    await _notification.SendNotificationAsync(
                                       order.PassengerId, // التأكد من وجود CustomerId في موديل الـ Order
                                     NotificationType.NoDriverFound,
                                       "نعتذر منك",
                                 "للأسف لم نجد سائقاً متاحاً حالياً، يرجى المحاولة مرة أخرى لاحقاً",
                                      order.OrderId
                                                      );
                    return "No driver available";
                }


                order.LastOfferedDriverId = queueDriver.UserId;
                order.TripOfferSentAt = DateTime.UtcNow;
                order.Status = OrderStatus.SearchingDriver;
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                await SendTripOffer(order,  queueDriver.UserId);

                return "Driver from queue notified";
            }

            // 🔥 بدون بلوك — فقط جلب المخالفات
            var violations = await _context.Violations
                .Where(v => v.Status == ViolationStatus.Active)
                .GroupBy(v => v.DriverId)
                .Select(g => new { DriverId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DriverId, x => x.Count);

           

            var scores = await CalculateDriversScore(candidates, order , violations);

            foreach (var c in candidates)
            {
                c.Score = scores[c.DriverId];
            }

            var bestDriver = candidates.Where(c => scores.ContainsKey(c.DriverId))
    .OrderBy(c => scores[c.DriverId])
    .FirstOrDefault();

            if (bestDriver == null)
                return "No valid driver found";

            // ❌ إذا نفس السائق → لا تعيد الإرسال
            if (bestDriver.DriverId == order.LastOfferedDriverId)
            {
                order.TripOfferSentAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return "Same driver, delay retry";
            }
            order.LastOfferedDriverId = bestDriver.DriverId;
            order.TripOfferSentAt = DateTime.UtcNow;
            order.Status = OrderStatus.SearchingDriver;
            order.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await SendTripOffer(order,bestDriver.DriverId, isUrgent: false);

            return "Driver offer sent";
        }

        public async Task<string> AssignTripEmergencyAsync(int tripId, string? excludedDriverId = null)
        {
            var mode = await adminAssignmentRepository.GetModeAsync();

            if (mode == SystemMode.Manual)
            {
                return "System is manual";
            }

           

            var trip = await _context.Trips
                .Include(t => t.TripOrders)
                .ThenInclude(o => o.Order)
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
                return "Trip not found";

            if (trip.IsManuallyAssigned)
                return "Manual override active";

            var lastDriver = await _context.Drivers
                .FirstOrDefaultAsync(d => d.UserId == excludedDriverId);

            if (lastDriver == null || !lastDriver.LastLat.HasValue || !lastDriver.LastLng.HasValue)
                return "Old driver location not found";

            double lat = (double)lastDriver.LastLat.Value;
            double lng = (double)lastDriver.LastLng.Value;

            int totalPassengers = trip.TripOrders
                .Where(o => o.StatusInTrip == TripOrderStatus.Assigned ||
                            o.StatusInTrip == TripOrderStatus.PickedUp)
                .Sum(o => o.Order.PassengerCount);

            var activeThreshold = DateTime.UtcNow.AddMinutes(-10);

            var drivers = await _context.Drivers.AsNoTracking()
                .Where(d =>
                    d.LastLat.HasValue &&
                    d.LastLng.HasValue &&
                    d.LastSeenAt >= activeThreshold &&
                    d.UserId != excludedDriverId &&
                    d.Status != DriverStatus.offline)
                .Include(d => d.Vehicles)
                .ToListAsync();

           

           

            var activeTrips = await _context.Trips
                .Include(t => t.TripOrders)
                .Where(t => t.Status == TripStatus.Assigned || t.Status == TripStatus.InProgress)
                .ToListAsync();

            var tripMap = activeTrips
     .Where(t => t.DriverId != null)
     .GroupBy(t => t.DriverId!)
     .ToDictionary(g => g.Key, g => g.First());

            // 🔥 فلترة أولية (بدون ETA)
            var filtered = new List<Driver>();

            foreach (var d in drivers)
            {
                var vehicle = GetActiveVehicle(d);
                if (vehicle == null) continue;

                int currentPassengers = 0;

                if (tripMap.TryGetValue(d.UserId, out var activeTrip))
                {
                    currentPassengers = activeTrip.TripOrders
                        .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp)
                        .Sum(o => o.Order.PassengerCount);
                }

                int availableSeats = vehicle.Seats - currentPassengers;

                if (availableSeats < totalPassengers)
                    continue;

                filtered.Add(d);
            }

            // 🔴 fallback
            if (!filtered.Any())
            {
                var queueDriver = await GetNextDriverFromQueueAsync();

                if (queueDriver == null)
                {
                    trip.Status = TripStatus.NoDriverFound;
                    await _context.SaveChangesAsync();

                    foreach (var tripOrder in trip.TripOrders)
                    {
                        await _notification.SendNotificationAsync(
                            tripOrder.Order.PassengerId,
                            NotificationType.NoDriverFound,
                            "نعتذر منك",
                            "تعذر العثور على سائق لإكمال رحلتك المشتركة",
                            tripOrder.OrderId,
                            trip.TripId
                        );
                    }

                    return "No driver available";
                }

                if (queueDriver.UserId == trip.LastOfferedDriverId)
                {
                    trip.TripOfferSentAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return "Same driver, delay retry";
                }

                trip.Status = TripStatus.SearchingDriver;
                trip.DriverId = null;
                trip.TripOfferSentAt = DateTime.UtcNow;
                trip.LastOfferedDriverId = queueDriver.UserId;

                foreach (var o in trip.TripOrders)
                {
                    if (o.Order.Status != OrderStatus.AssignedToTrip)
                    {
                        o.StatusInTrip = TripOrderStatus.Assigned;
                        o.Order.Status = OrderStatus.AssignedToTrip;
                    }
                }

                await _context.SaveChangesAsync();

                await SendTripOfferForWholeTrip(trip, queueDriver.UserId);

                return "Offer sent to queue driver";
            }



            // 🔥 أهم جزء: حساب ETA مرة واحدة (Matrix)
            var locations = filtered
                .Select(d => new DriverLocationDto(
                    (double)d.LastLat!.Value,
                    (double)d.LastLng!.Value))
                .ToList();

            var etas = await mapService.GetDistancesAsync(locations, lat, lng);

            // 🔥 دمج ETA
            var candidates = new List<(Driver Driver, TimeSpan Eta)>();

            for (int i = 0; i < filtered.Count; i++)
            {
                candidates.Add((filtered[i], etas[i]));
            }

            // 🔥 اختيار الأفضل حسب ETA
            var best = candidates.OrderBy(x => x.Eta).First();
            var bestDriver = best.Driver;

            if (bestDriver.UserId == trip.LastOfferedDriverId)
            {
                trip.TripOfferSentAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return "Same driver, delay retry";
            }

            trip.Status = TripStatus.SearchingDriver;
            trip.DriverId = null;
            trip.TripOfferSentAt = DateTime.UtcNow;
            trip.LastOfferedDriverId = bestDriver.UserId;

            foreach (var o in trip.TripOrders)
            {
                if (o.Order.Status != OrderStatus.AssignedToTrip)
                {
                    o.StatusInTrip = TripOrderStatus.Assigned;
                    o.Order.Status = OrderStatus.AssignedToTrip;
                }
            }

            await _context.SaveChangesAsync();

            await SendTripOfferForWholeTrip(trip, bestDriver.UserId);

            return "Emergency offer sent";
        }

        private async Task<string> AssignOrderUrgentAsync(Order order, string? excludedDriverId = null)
        {
           

           

            if (order == null)
                return "Order not found";

            if (order.IsManuallyAssigned)
                return "Manual override active";

            if (order.Status != OrderStatus.Pending && order.Status != OrderStatus.SearchingDriver)
                return "Order not pending";

            double lat = (double)order.PickupLat!;
            double lng = (double)order.PickupLng!;

            int passengers = order.PassengerCount;

            var activeThreshold = DateTime.UtcNow.AddMinutes(-10);

            var drivers = await _context.Drivers.AsNoTracking()
                .Where(d =>
                    d.LastLat.HasValue &&
                    d.LastLng.HasValue &&
                    d.LastSeenAt >= activeThreshold &&
                    d.UserId != excludedDriverId &&
                    d.Status != DriverStatus.offline)
                .Select(d => new
                {
                    Driver = d,
                    Vehicle = d.Vehicles
            .Where(v => v.IsCurrent && v.IsActive)
            .FirstOrDefault()
                })
    .Where(x => x.Vehicle != null)
    .ToListAsync();

           



            var activeTrips = await _context.Trips
                .Include(t => t.TripOrders)
                .Where(t => t.Status == TripStatus.Assigned || t.Status == TripStatus.InProgress)
                .ToListAsync();

            var tripMap = activeTrips
                .Where(t => t.DriverId != null)
                .GroupBy(t => t.DriverId!)
.ToDictionary(g => g.Key, g => g.First());

            // 🔥 فلترة أولية (بدون ETA)
            var filtered = new List<Driver>();

          

            foreach (var item in drivers)
            {
                var d = item.Driver;
                var vehicle = item.Vehicle;


                int currentPassengers = 0;

                if (tripMap.TryGetValue(d.UserId, out var activeTrip))
                {
                    currentPassengers = activeTrip.TripOrders
                        .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp ||
                                    o.StatusInTrip == TripOrderStatus.Assigned)
                        .Sum(o => o.Order.PassengerCount);
                }

                int availableSeats = vehicle.Seats - currentPassengers;

                if (availableSeats < passengers)
                    continue;

                filtered.Add(d);
            }

            filtered = filtered.Take(10).ToList();

            // 🔴 fallback (queue)
            if (!filtered.Any())
            {
                var queueDriver = await GetNextDriverFromQueueAsync();

                if (queueDriver == null)
                {
                    order.Status = OrderStatus.NoDriverFound;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    await _notification.SendNotificationAsync(
                        order.PassengerId,
                        NotificationType.NoDriverFound,
                        "نعتذر منك",
                        "لم يتم العثور على سائق قريب حالياً",
                        order.OrderId
                    );

                    return "No urgent driver available";
                }

                if (queueDriver.UserId == order.LastOfferedDriverId)
                {
                    order.TripOfferSentAt = DateTime.UtcNow;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return "Same driver, delay retry";
                }

                order.Status = OrderStatus.SearchingDriver;
                order.TripOfferSentAt = DateTime.UtcNow;
                order.LastOfferedDriverId = queueDriver.UserId;
                order.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                await SendTripOffer(order, queueDriver.UserId, isUrgent: true);

                return "Offer sent to queue driver";
            }

            // 🔥 حساب ETA (Matrix)
            var locations = filtered
                .Select(d => new DriverLocationDto(
                    (double)d.LastLat!.Value,
                    (double)d.LastLng!.Value))
                .ToList();

            var etas = await mapService.GetDistancesAsync(locations, lat, lng);

            // 🔥 دمج ETA
            var candidates = new List<(Driver Driver, TimeSpan Eta)>();

            for (int i = 0; i < filtered.Count; i++)
            {
                candidates.Add((filtered[i], etas[i]));
            }

            // 🔥 اختيار الأفضل
            var best = candidates.OrderBy(x => x.Eta).First();
            var bestDriver = best.Driver;

            if (bestDriver.UserId == order.LastOfferedDriverId)
            {
                order.TripOfferSentAt = DateTime.UtcNow;
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return "Same driver, delay retry";
            }

            order.Status = OrderStatus.SearchingDriver;
            order.TripOfferSentAt = DateTime.UtcNow;
            order.LastOfferedDriverId = bestDriver.UserId;
            order.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            await SendTripOffer(order, bestDriver.UserId, isUrgent: true);

            return "Urgent driver offer sent";
        }
        private async Task SendTripOfferForWholeTrip(Trip trip, string driverId)
        {
            await _notification.SendNotificationAsync(
    driverId,
    NotificationType.NewTripOffer,
    "New Shared Trip",
    "You have a trip with multiple passengers",
    null,
    trip.TripId,
    new
    {
        orders = trip.TripOrders.Select(o => new
        {
            orderId = o.OrderId,
            pickup = o.Order.PickupLocation,
            dropoff = o.Order.DropoffLocation,
            passengers = o.Order.PassengerCount
        }),
        countdown = 180
    }
);

          

        }
        // Send Offer
        // ==========================
        private async Task SendTripOffer(Order order, string driverId, bool isUrgent = false)
        {
            await _notification.SendNotificationAsync(
        driverId,
        NotificationType.NewTripOffer,
        isUrgent ? "طلب مستعجل 🔥" : "New Trip Request", // عنوان ديناميكي
        isUrgent ? "هذا الطلب قريب جداً منك، يرجى الاستجابة فوراً" : "You have a new trip request",
        order.OrderId,
        null,
        new
        {
            pickup = order.PickupLocation,
            dropoff = order.DropoffLocation,
            passengers = order.PassengerCount,
            countdown = 180,
            isUrgent = isUrgent // إرسال العلم للفرونت آند لتمييز الطلب (مثلاً صوت تنبيه أقوى)
        }
    );





        }

        // Shared Drivers
        // ==========================
        private async Task<List<DriverCandidate>> GetSharedDrivers(  Order order, TimeSpan maxExtraTime,  string? excludedDriverId)
        {
            double pickupLat = (double)order.PickupLat;
            double pickupLng = (double)order.PickupLng;

            // 🔥 أضف تعريف وقت الصلاحية هنا
            var activeThreshold = DateTime.UtcNow.AddMinutes(-10);

            var trips = await _context.Trips
                .AsNoTracking()
                .Where(t =>
                    (t.Status == TripStatus.Assigned || t.Status == TripStatus.InProgress)
                    && t.Driver.Status == DriverStatus.Shared
                    && t.Driver.LastSeenAt >= activeThreshold // ✅ تأكد أن السائق نشط حالياً
                    && t.DriverId != excludedDriverId)
                .Include(t => t.Driver)
                .ThenInclude(d => d.Vehicles)
                .Include(t => t.TripOrders)
                .ThenInclude(o => o.Order)
                .ToListAsync();

            var result = new List<DriverCandidate>();

            foreach (var trip in trips)
            {
                var vehicle = GetActiveVehicle(trip.Driver);
                if (vehicle == null) continue;

                int passengers = trip.TripOrders
                    .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp)
                    .Sum(o => o.Order.PassengerCount);

                if ((vehicle.Seats - passengers) < order.PassengerCount)
                    continue;

                double distanceMeters = HaversineDistance(
                    pickupLat, pickupLng,
                    (double)trip.Driver.LastLat!,
                    (double)trip.Driver.LastLng!);

                if (distanceMeters > _settings.SearchRadiusMeters)
                    continue;

                var cacheKey = $"shared-{trip.DriverId}-{order.OrderId}";

                TimeSpan eta;

                if (!_etaCache.TryGet(cacheKey, out eta))
                {
                    eta = await mapService.GetAdditionalETAToTripAsync(trip, order);
                    _etaCache.Set(cacheKey, eta, _settings.EtaCacheSeconds);
                }

                if (eta > maxExtraTime)
                    continue;

                result.Add(new DriverCandidate
                {
                    DriverId = trip.DriverId,
                    Eta = eta,
                    DistanceMeters = distanceMeters,
                    IsShared = true
                });
            }

            return result;
        }

        // Nearest Drivers
        // ==========================
        private async Task<List<DriverCandidate>> GetNearestDrivers(Order order, TimeSpan maxEta, string? excludedDriverId)
        {
            decimal pickupLat = order.PickupLat!;
            decimal pickupLng = order.PickupLng!;

            // 🔥 أضف تعريف وقت الصلاحية هنا (10 دقائق)
            var activeThreshold = DateTime.UtcNow.AddMinutes(-10);

            // 1️⃣ جلب السائقين المتاحين
            var drivers = await _context.Drivers
                .AsNoTracking()
                .Where(d =>
                    d.Status == DriverStatus.available &&
                    d.LastLat.HasValue &&
                    d.LastLng.HasValue &&
                    d.LastSeenAt >= activeThreshold && // ✅ السطر المنقذ: استبعاد الأشباح
                    d.UserId != excludedDriverId)
                .Include(d => d.Vehicles)
                .ToListAsync();

            // 2️⃣ فلترة حسب المقاعد والمسافة (3km)
            var filteredDrivers = drivers
                .Select(d => new
                {
                    Driver = d,
                    Vehicle = GetActiveVehicle(d),
                    Distance = HaversineDistance(
                        (double)pickupLat,
                        (double)pickupLng,
                        (double)d.LastLat!.Value,
                        (double)d.LastLng!.Value)
                })
                .Where(x =>
                    x.Vehicle != null &&
                    x.Vehicle.Seats >= order.PassengerCount &&
                    x.Distance <= _settings.SearchRadiusMeters)
                .OrderBy(x => x.Distance)
                .Take(10) // 3️⃣ أفضل 10 سائقين فقط
                .ToList();

            if (!filteredDrivers.Any())
                return new List<DriverCandidate>();

            // 4️⃣ تجهيز مواقع السائقين لطلب Matrix
            var locations = filteredDrivers
                .Select(x => new DriverLocationDto(
                    (double)x.Driver.LastLat!.Value,
                    (double)x.Driver.LastLng!.Value))
                .ToList();

            // 5️⃣ استدعاء Matrix API مرة واحدة
            var etas = await mapService.GetDistancesAsync(
                locations,
                (double)pickupLat,
                (double)pickupLng);

            // 6️⃣ إنشاء قائمة المرشحين
            var candidates = new List<DriverCandidate>();

            for (int i = 0; i < filteredDrivers.Count; i++)
            {
                if (etas[i] > maxEta)
                    continue;

                candidates.Add(new DriverCandidate
                {
                    DriverId = filteredDrivers[i].Driver.UserId,
                    Eta = etas[i],
                    DistanceMeters = filteredDrivers[i].Distance,
                    IsShared = false
                });
            }

            return candidates;
        }

        // Driver Accept
        // ==========================
        public async Task<string> DriverAcceptOrderAsync(int  orderId, string driverId)
        {



            var dbOrder = await _context.Orders
         .FirstOrDefaultAsync(o => o.OrderId == orderId);


            if (dbOrder == null)
                return "Order not found";

            if (dbOrder.LastOfferedDriverId != driverId)
                return "This order is not assigned to you";

            if (dbOrder.Status != OrderStatus.SearchingDriver)
                return "Order already taken";
            var now = DateTime.UtcNow;

            if (dbOrder.TripOfferSentAt == null ||
    (now - dbOrder.TripOfferSentAt.Value).TotalSeconds > 180)
            {
                return "Offer expired";
            }

            var driver = await _context.Drivers
      .Include(d => d.Vehicles)
      .Include(d => d.User)
      .FirstOrDefaultAsync(d => d.UserId == driverId);

            var vehicle = GetActiveVehicle(driver);
            if (vehicle == null)
                return "Driver has no active vehicle";

            var activeTrip = await _context.Trips
      .Include(t => t.TripOrders).ThenInclude(to => to.Order)
      .Where(t => t.DriverId == driverId &&
                  (t.Status == TripStatus.Assigned || t.Status == TripStatus.InProgress))
      .FirstOrDefaultAsync();

           

            if (activeTrip != null)
            {
                // ✅ الحسبة الصحيحة: تشمل من ركب فعلياً + من تم قبول طلبهم وينتظرون الدور
                int currentPassengers = activeTrip.TripOrders
                    .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp || o.StatusInTrip == TripOrderStatus.Assigned)
                    .Sum(o => o.Order.PassengerCount);

                // التحقق من سعة السيارة
                if (currentPassengers + dbOrder.PassengerCount > vehicle.Seats)
                {
                    return "Not enough seats in the current trip";
                }

                // إضافة الطلب الجديد لنفس الرحلة
                var tripOrder = new TripOrder
                {
                    TripId = activeTrip.TripId,
                    OrderId = dbOrder.OrderId,
                    AssignedAt = DateTime.UtcNow,
                    StatusInTrip = TripOrderStatus.Assigned
                };

                await _context.TripOrders.AddAsync(tripOrder);

                dbOrder.Status = OrderStatus.AssignedToTrip;

                // تحديث حالة السائق حسب عدد الركاب بعد إضافة الطلب الجديد
                int passengers = currentPassengers + dbOrder.PassengerCount;
                driver.Status = passengers < vehicle.Seats ? DriverStatus.Shared : DriverStatus.busy;

                ;

                var eta = await mapService.GetAdditionalETAToTripAsync(activeTrip, dbOrder);

                dbOrder.ExpectedArrivalAt = DateTime.UtcNow.Add(eta); // 🔥 حفظ الوقت المتوقع
                dbOrder.IsDelayNotified = false;


                await _context.SaveChangesAsync();


                // إشعار الراكب
                await _notification.SendNotificationAsync(
     dbOrder.PassengerId,
     NotificationType.DriverAcceptedTrip,
     "Driver Accepted",
     "Driver accepted your trip",
     dbOrder.OrderId,
     activeTrip.TripId,
     new
     {
         driverId = driverId,
         tripId = activeTrip.TripId,
         isShared = true,
         driverName = driver.User.FirstName + " " + driver.User.LastName,
         etaMinutes = (int)eta.TotalMinutes
     }
 );
                await RemoveDriverFromQueue(driverId);

                return "Order added to existing trip";
            }

            else
            {

                dbOrder.Status = OrderStatus.AssignedToTrip;

                var tripId = await CreateTripAsync(dbOrder, driverId);

                var tripOrder = await _context.TripOrders
    .FirstOrDefaultAsync(t => t.OrderId == dbOrder.OrderId && t.TripId == tripId);

                var eta = await mapService.GetETAAsync(
  driver.LastLat.Value,
  driver.LastLng.Value,
  dbOrder.PickupLat,
  dbOrder.PickupLng);

                dbOrder.ExpectedArrivalAt = DateTime.UtcNow.Add(eta); // 🔥 حفظ الوقت المتوقع
                dbOrder.IsDelayNotified = false;

                await _context.SaveChangesAsync();


                await _notification.SendNotificationAsync(
    dbOrder.PassengerId,
    NotificationType.DriverAcceptedTrip,
    "Driver Accepted",
    "Driver accepted your trip",
    dbOrder.OrderId,
    tripId,
    new
    {
        driverId = driverId,
        tripId = tripId,
        isShared = false,
        driverName = driver.User.FirstName + " " + driver.User.LastName,
        etaMinutes = (int)eta.TotalMinutes
    }
);
                await RemoveDriverFromQueue(driverId);

                return "Trip created successfully";
            }
        }

        // تظهر في حالة ان هناك سائق الغى رحلته ويتم البحث عن سائق مناسب لاخذ هذه الرحلة بكل ما فيها 

        public async Task<string> DriverAcceptTripAsync(int tripId, string driverId)
        {
            var trip = await _context.Trips
                .Include(t => t.TripOrders)
                .ThenInclude(o => o.Order)
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
                return "Trip not found";



            // ✅ تحقق من السائق الصحيح
            if (trip.LastOfferedDriverId != driverId)
                return "Not assigned to you";

            // ✅ تحقق من الحالة
            if (trip.Status != TripStatus.SearchingDriver)
                return "Trip already taken";

            var now = DateTime.UtcNow;

            // ✅ تحقق من انتهاء العرض
            if (trip.TripOfferSentAt == null ||
                (now - trip.TripOfferSentAt.Value).TotalSeconds > 180)
                return "Offer expired";


            trip.DriverId = driverId;
            trip.Status = TripStatus.Assigned;
            trip.AssignedAt = DateTime.UtcNow;

            var driver = await _context.Drivers
                .Include(d => d.Vehicles)
                .FirstAsync(d => d.UserId == driverId);

            var vehicle = GetActiveVehicle(driver)!;

            int passengers = trip.TripOrders
                .Where(o => o.StatusInTrip == TripOrderStatus.Assigned ||
                            o.StatusInTrip == TripOrderStatus.PickedUp)
                .Sum(o => o.Order.PassengerCount);

            driver.Status = passengers < vehicle.Seats
                ? DriverStatus.Shared
                : DriverStatus.busy;

            await _context.SaveChangesAsync();

            _activeTripStore.SetDriverTrip(driverId, trip.TripId);

            // إشعار الركاب
            foreach (var o in trip.TripOrders)
            {
                var eta = await mapService.GetETAAsync(
        driver.LastLat.Value,
        driver.LastLng.Value,
        o.Order.PickupLat,
        o.Order.PickupLng
    );

                o.Order.ExpectedArrivalAt = DateTime.UtcNow.Add(eta);
                o.Order.IsDelayNotified = false;



                await _notification.SendNotificationAsync(
     o.Order.PassengerId,
     NotificationType.DriverAcceptedTrip,
     "Driver Assigned",
     "A new driver has taken your trip",
     o.OrderId,
     trip.TripId,
     new
     {
         driverId = driverId,
         tripId = trip.TripId,
         isShared = true,
         totalPassengers = trip.TripOrders.Sum(x => x.Order.PassengerCount)
     }
 );
            }

            await RemoveDriverFromQueue(driverId);


            return "Trip accepted";
        }

        public async Task<string> DriverRejectTripAsync(int tripId, string driverId)
        {

            var trip = await _context.Trips
                .Include(t => t.TripOrders)
                .ThenInclude(o => o.Order)
                .FirstOrDefaultAsync(t => t.TripId == tripId);

            if (trip == null)
                return "Trip not found";



            // ✅ تحقق من السائق الصحيح
            if (trip.LastOfferedDriverId != driverId)
                return "Not assigned to you";

            if (trip.Status != TripStatus.SearchingDriver)
                return "Trip not available";

            // إشعار الركاب
            foreach (var o in trip.TripOrders)
            {
               
                await _notification.SendNotificationAsync(
    o.Order.PassengerId,
    NotificationType.DriverRejectedTrip,
    "Searching new driver",
    "Driver rejected trip",
    o.OrderId,
    trip.TripId,
    new
    {
        driverId = driverId,
        tripId = trip.TripId,
        reason = "DriverRejected",
        isSearchingNewDriver = true
    }
);
            }

            await _notification.SendOfficeNotificationAsync(
    officeUserId: _settings.OfficeUserId,
    type: NotificationType.DriverRejectedTrip,
    title: "Driver Rejected",
    body: $"Driver {driverId} rejected Trip {trip.TripId}",
    orderId: null,
    tripId: trip.TripId,
    extraData: new
    {
        driverId,
        reason = "DriverRejected"
    },
    saveToDb: false
);

            if (!_settings.EnableAutoAssignment)
            {
                return "Auto disabled";
            }

            return await AssignTripEmergencyAsync(tripId, driverId);
        }

        // Driver Reject
        // ==========================
        public async Task<string> DriverRejectOrderAsync(int  orderId, string driverId)
        {
            var order = await _context.Orders
       .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null)
                return "Order not found";

            if (order.Status != OrderStatus.SearchingDriver)
                return "Order is not available for rejection";

            if (order.LastOfferedDriverId != driverId)
                return "This order is not assigned to you";

            var now = DateTime.UtcNow;

            // (اختياري لكن مهم) تحقق من انتهاء الوقت
            if (order.TripOfferSentAt == null ||
                ( now - order.TripOfferSentAt.Value).TotalSeconds > 20)
            {
                return "Offer expired";
            }

           

            await _notification.SendOfficeNotificationAsync(
      officeUserId: _settings.OfficeUserId,
      type: NotificationType.DriverRejectedTrip,
      title: "Driver Rejected",
      body: $"Driver {driverId} rejected order {order.OrderId}",
      orderId: order.OrderId,
      tripId: null,
      extraData: new
      {
          driverId,
          reason = "DriverRejected"
      },
      saveToDb: false
  );

            await _notification.SendNotificationAsync(
     order.PassengerId,
     NotificationType.DriverRejectedTrip,
     "Searching new driver",
     "Driver rejected request",
     order.OrderId,
     null,
     new
     {
         driverId = driverId,
         orderId = order.OrderId,
         status = "searching_new_driver"
     }
 );
            if (!_settings.EnableAutoAssignment)
            {
                return "Auto disabled";
            }

            return await DriverAssignAsync(order, driverId);


        }

        // Create Trip
        // ==========================
        private async Task<int> CreateTripAsync(Order order, string driverId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            var driver = await _context.Drivers
                .Include(d => d.Vehicles)
                .FirstAsync(d => d.UserId == driverId);

            var vehicle = GetActiveVehicle(driver);
            if (vehicle == null)
                throw new Exception("Driver has no active vehicle");

            var trip = new Trip
            {
                DriverId = driverId,
                Status = TripStatus.Assigned,
                CreatedAt = DateTime.UtcNow,
                AssignedAt = DateTime.UtcNow
            };

            await _context.Trips.AddAsync(trip);

            await _context.TripOrders.AddAsync(new TripOrder
            {
                Trip = trip,
                OrderId = order.OrderId,
                AssignedAt = DateTime.UtcNow,
                StatusInTrip = TripOrderStatus.Assigned
            });

            order.Status = OrderStatus.AssignedToTrip;

            int occupiedSeats = order.PassengerCount;

            if (occupiedSeats < vehicle.Seats)
                driver.Status = DriverStatus.Shared;
            else
                driver.Status = DriverStatus.busy;

            await _context.SaveChangesAsync();
            _activeTripStore.SetDriverTrip(driverId, trip.TripId);

            await transaction.CommitAsync();

            return trip.TripId;
        }

        // Queue
        // ==========================
        public async Task<string> EnterQueueAsync(string driverId)
        {
            var driver = await _context.Drivers.FindAsync(driverId);

            if (driver == null || driver.Status != DriverStatus.available)
                return "can not enter to queue";

            bool exists = await _context.OfficeQueueEntries
                .AnyAsync(q => q.DriverId == driverId && q.Status == QueueStatus.InQueue);

            if (exists) return "can not enter to queue because you in queue now";

            var entry = new OfficeQueueEntry
            {
                DriverId = driverId,
                Status = QueueStatus.InQueue,
                EnteredAt = DateTime.UtcNow
            };

            _context.OfficeQueueEntries.Add(entry);

            await _context.SaveChangesAsync();

            await _notification.SendOfficeNotificationAsync(
       officeUserId: _settings.OfficeUserId,
       type: NotificationType.DriverEnteredQueue,
       title: "Driver Entered Queue",
       body: $"Driver {driverId} entered the queue",
       orderId: null,
       tripId: null,
       extraData: new
       {
           driverId,
           action = "entered",
           time = entry.EnteredAt
       },
       saveToDb: true
   );

          


            return "Entered successfully";
        }

        // Helpers
        // ==========================
        private Vehicle? GetActiveVehicle(Driver driver)
        {
            return driver.Vehicles.FirstOrDefault(v => v.IsCurrent && v.IsActive);
        }

        private double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            double R = 6371000;

            double dLat = (lat2 - lat1) * Math.PI / 180;
            double dLon = (lon2 - lon1) * Math.PI / 180;

            double a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) *
                Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return R * c;
        }

        private async Task<Driver?> GetNextDriverFromQueueAsync()
        {
            var entry = await _context.OfficeQueueEntries
                .Where(q => q.Status == QueueStatus.InQueue && q.Driver.Status == DriverStatus.available)
                .OrderBy(q => q.EnteredAt)
                .Include(q => q.Driver)
                .FirstOrDefaultAsync();

            if (entry == null) return null;

            entry.Status = QueueStatus.LeftQueue;
            entry.LeftAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return entry.Driver;
        }

        private async Task<Dictionary<string, double>> CalculateDriversScore(
            List<DriverCandidate> candidates,
            Order order , Dictionary<string, int> violations)
        {
            var driverIds = candidates.Select(c => c.DriverId).Distinct().ToList();
            var now = DateTime.UtcNow;

            // =========================
            // 🔥 Trips Stats
            // =========================
            var tripStats = await _context.Trips
                .Where(t => t.DriverId != null &&  driverIds.Contains(t.DriverId))
                .GroupBy(t => t.DriverId)
                .Select(g => new
                {
                    DriverId = g.Key,
                    LastTrip = g.Max(t => t.CreatedAt),
                    TripsToday = g.Count(t => t.CreatedAt.Date == now.Date)
                })
                .ToDictionaryAsync(x => x.DriverId);

            // =========================
            // ⭐ Ratings
            // =========================
            var ratings = await _context.Ratings
                .Where(r => driverIds.Contains(r.TargetUserId))
                .GroupBy(r => r.TargetUserId)
                .Select(g => new
                {
                    DriverId = g.Key,
                    Avg = g.Average(x => x.Stars)
                })
                .ToDictionaryAsync(x => x.DriverId);

           
            

            var result = new Dictionary<string, double>();



            foreach (var c in candidates)
            {
               
                // =========================
                // 📊 Stats
                // =========================
                double etaMinutes = c.Eta.TotalMinutes;
                double distanceKm = c.DistanceMeters / 1000.0;

                double waiting = 0;
                int tripsToday = 0;

                if (tripStats.ContainsKey(c.DriverId))
                {
                    waiting = (now - tripStats[c.DriverId].LastTrip).TotalMinutes;
                    tripsToday = tripStats[c.DriverId].TripsToday;
                }

                double rating = ratings.ContainsKey(c.DriverId)
                    ? ratings[c.DriverId].Avg
                    : 5;

                int violationCount = violations.ContainsKey(c.DriverId)
                    ? violations[c.DriverId]
                    : 0;

              

                // =========================
                // 🧠 Score
                // =========================
                double score =
                    (etaMinutes * 3) +
                    (distanceKm * 4) -
                    (waiting * 0.2) -
                    (rating * 5) +
                    (tripsToday * 2) +
                    (c.IsShared ? 8 : 0) +
                    (violationCount * 10); // 🔥 تأثير المخالفات

                result[c.DriverId] = score;
            }

            return result;
        }
        public async Task<string>DriverArrivedAsync( int orderId, string driverId)
        {
            var trip = await _context.Trips.Include(t => t.Driver).ThenInclude(d => d.User)
     // ✅ البحث عن الرحلة بغض النظر عن حالتها طالما السائق نشط عليها
     .Where(t => t.DriverId == driverId && (t.Status == TripStatus.Assigned || t.Status == TripStatus.InProgress) && t.TripOrders.Any(o => o.OrderId == orderId))
     .Include(t => t.TripOrders)
     .ThenInclude(o => o.Order)
     .FirstOrDefaultAsync();

            if (trip == null)
                return "Trip not found";

            // ✅ تحقق إنو السائق هو نفسه
            if (trip.DriverId != driverId)
                return "Unauthorized driver";

            // ✅ تحقق من حالة الرحلة
            if (trip.Status != TripStatus.Assigned && trip.Status != TripStatus.InProgress)
                return "Trip not in valid state";

            var tripOrder = await _context.TripOrders
    .Include(to => to.Order)
    .Include(to => to.Trip)
        .ThenInclude(t => t.Driver)
            .ThenInclude(d => d.User)
    .FirstOrDefaultAsync(to =>
        to.OrderId == orderId &&
        to.Trip.DriverId == driverId &&
        (to.Trip.Status == TripStatus.Assigned || to.Trip.Status == TripStatus.InProgress)
    );

            if (tripOrder == null)
                return "Order not in this trip";

            // (اختياري) منع التكرار
            if (tripOrder.StatusInTrip == TripOrderStatus.DroppedOff)
                return "Order already completed";

            if (tripOrder.StatusInTrip == TripOrderStatus.DriverArrived)
                return "Already marked as arrived";

            tripOrder.StatusInTrip = TripOrderStatus.DriverArrived;

            await _notification.SendNotificationAsync(
     tripOrder.Order.PassengerId,
     NotificationType.DriverArrived,
     "Driver Arrived",
     "Your driver has arrived",
     orderId,
     trip.TripId,
     extraData: new
     {
         driverName =trip.Driver.User.FirstName +" "+ trip.Driver.User.LastName,
         destination = tripOrder.Order.DropoffLocation
     }
 );

            return "Arrived notification sent";
        }

        public async Task<string> StartTripAsync(int tripId, string driverId)
        {
            var trip = await _context.Trips.Include(t=>t.Driver).ThenInclude(t=>t.User)
          .Include(t => t.TripOrders)
          .ThenInclude(o => o.Order)
          .FirstOrDefaultAsync(t =>
              t.TripId == tripId &&
              t.DriverId == driverId);


            if (trip == null)
                return "Trip not found or unauthorized";

            if (trip.Status != TripStatus.Assigned)
                return "Trip cannot be started";

            trip.Status = TripStatus.InProgress;
            trip.StartTime = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            foreach (var o in trip.TripOrders)
            {
                await _notification.SendNotificationAsync(
             o.Order.PassengerId,
             NotificationType.TripStarted,
             "Trip Started",
             "Your trip has started",
             o.OrderId,
             tripId,
             extraData: new
             {
                 driverName = trip.Driver.User.FirstName + " " + trip.Driver.User.LastName,
                 destination = o.Order.DropoffLocation
             }
         );
            }

            return "Trip started successfully";
        }


        public async Task<string> PickupAsync(string driverId, int orderId)
        {
            var trip = await _context.Trips.Include(t=>t.Driver).ThenInclude(t=>t.User)
                .Include(t => t.TripOrders)
                .ThenInclude(o => o.Order)
                .FirstOrDefaultAsync(t =>
                    t.DriverId == driverId &&
                   ( t.Status == TripStatus.InProgress || t.Status == TripStatus.Assigned) && t.TripOrders.Any(o => o.OrderId == orderId));

            if (trip == null)
                return "No active trip found";



            var tripOrder = await _context.TripOrders
   .Include(to => to.Order)
   .Include(to => to.Trip)
       .ThenInclude(t => t.Driver)
           .ThenInclude(d => d.User)
   .FirstOrDefaultAsync(to =>
       to.OrderId == orderId &&
       to.Trip.DriverId == driverId &&
       (to.Trip.Status == TripStatus.Assigned || to.Trip.Status == TripStatus.InProgress)
   );




            if (tripOrder == null)
                return "Order not found in this trip";

            tripOrder.Order.ExpectedArrivalAt = null;
            tripOrder.Order.IsDelayNotified = false;

            if (tripOrder.StatusInTrip != TripOrderStatus.Assigned &&
    tripOrder.StatusInTrip != TripOrderStatus.DriverArrived)
            {
                return "Order already picked up or invalid state";
            }

            if (trip.Status == TripStatus.Assigned)
            {
                trip.Status = TripStatus.InProgress;
                trip.StartTime = DateTime.UtcNow;
            }

            tripOrder.StatusInTrip = TripOrderStatus.PickedUp;


            var driver = await _context.Drivers
                .Include(d => d.Vehicles)
                .FirstOrDefaultAsync(d => d.UserId == driverId);

            if (driver == null)
                return "Driver not found";

            var vehicle = GetActiveVehicle(driver);

            if (vehicle == null)
                return "Vehicle not found";

            // ✅ حساب الركاب داخل السيارة فقط
            int passengers = trip.TripOrders
                .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp)
                .Sum(o => o.Order.PassengerCount);

            // ✅ تحديث حالة السائق
            if (passengers < vehicle.Seats)
                driver.Status = DriverStatus.Shared;
            else
                driver.Status = DriverStatus.busy;

            await _context.SaveChangesAsync();

            // ✅ إشعار الراكب
            await _notification.SendNotificationAsync(
        tripOrder.Order.PassengerId,
        NotificationType.PickedUp,
        "Pickup Done",
        "You are now in the trip",
        orderId,
        trip.TripId,
        extraData: new
        {
            driverName = driver.User.FirstName + " " + driver.User.LastName,
            destination = tripOrder.Order.DropoffLocation
        }
    );

            // إشعار للسائق لإظهار الوجهة على خريطته
            await _notification.SendNotificationAsync(
                driver.UserId,
                NotificationType.TripStarted, // نوع جديد لإبلاغ السائق ببدء التحرك للوجهة
                "بدء التوصيل",
                $"وجهة الراكب: {tripOrder.Order.DropoffLocation}",
                orderId,
                trip.TripId,
                extraData: new
                {
                    destLat = tripOrder.Order.DropoffLat,
                    destLng = tripOrder.Order.DropoffLng,
                    destName = tripOrder.Order.DropoffLocation
                }
            );

            return "Pickup successful";
        }


        public async Task<string> DropoffAsync(string driverId, int orderId)
        {
            var trip = await _context.Trips
                .Include(t => t.TripOrders)
                .ThenInclude(o => o.Order)
                .FirstOrDefaultAsync(t =>
            t.DriverId == driverId &&
            t.Status == TripStatus.InProgress);

            if (trip == null)
                return "No active trip found";

            var tripOrder = trip.TripOrders
                .FirstOrDefault(o => o.OrderId == orderId);

            if (tripOrder == null)
                return "Order not found in this trip";

            if (tripOrder.StatusInTrip != TripOrderStatus.PickedUp)
                return "Order is not picked up yet";

            tripOrder.StatusInTrip = TripOrderStatus.DroppedOff;
            tripOrder.Order.Status = OrderStatus.Completed; // ✅ تحديث حالة الطلب الأصلي

            var driver = await _context.Drivers
                .Include(d => d.Vehicles)
                .FirstOrDefaultAsync(d => d.UserId == trip.DriverId);

            if (driver == null)
                return "Driver not found";

            int passengers = trip.TripOrders
     .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp)
     .Sum(o => o.Order.PassengerCount);

            var vehicle = GetActiveVehicle(driver);

            if (vehicle == null)
                return "Vehicle not found";


            // التحقق من أن كل الطلبات في هذه الرحلة انتهت فعلياً
            bool allOrdersFinished = trip.TripOrders.All(o =>
                o.StatusInTrip == TripOrderStatus.DroppedOff ||
                o.StatusInTrip == TripOrderStatus.Cancelled);

           

            if (allOrdersFinished)
            {
                driver.Status = DriverStatus.available;
                trip.Status = TripStatus.Completed;
                trip.CompletedAt = DateTime.UtcNow;
                trip.EndTime = DateTime.UtcNow;

                // إزالة الرحلة من ActiveTripStore
                _activeTripStore.RemoveDriverTrip(trip.DriverId);

                foreach (var o in trip.TripOrders.Where(x => x.StatusInTrip == TripOrderStatus.DroppedOff))
                {
                    await _notification.SendNotificationAsync(
     o.Order.PassengerId,
     NotificationType.RateTrip,
     "Rate your trip",
     "Please rate your driver",
     o.OrderId,
     trip.TripId,
     extraData: new
     {
         orderId = o.OrderId,
         tripId = trip.TripId,
         countdown = 1800 // 30 دقيقة
     }
 );
                    // ✅ إزالة الركاب من الـgroup الخاص بالرحلة
                    await _hubContext.Clients.Group($"user-{o.Order.PassengerId}")
                                            .SendAsync("LeaveTrip", trip.TripId);
                }
            }
            else
            {
                // 3. تحديث حالة السائق بناءً على الركاب المتبقين (للسماح بركاب جدد في الرحلات المشتركة)
                int remainingPassengers = trip.TripOrders
                    .Where(o => o.StatusInTrip == TripOrderStatus.PickedUp)
                    .Sum(o => o.Order.PassengerCount);

                driver.Status = remainingPassengers < vehicle.Seats ? DriverStatus.Shared : DriverStatus.busy;

                // إشعار الراكب الذي نزل فقط
                await _notification.SendNotificationAsync(
                    tripOrder.Order.PassengerId,
                    NotificationType.RateTrip,
                    "Trip Ended",
                    "You have arrived at your destination",
                    orderId,
                    trip.TripId
                );
            }

            await _context.SaveChangesAsync();

            return "Success";
        }


        public async Task<string> CancelTripByDriverAsync(int tripId, string driverId, TripCancelReason reason)
{
            // 1️⃣ جلب الرحلة مع كافة تفاصيل الركاب والسائق في استعلام واحد
            var trip = await _context.Trips
                .Include(t => t.TripOrders).ThenInclude(o => o.Order)
                .Include(t => t.Driver).ThenInclude(d => d.User)
                .FirstOrDefaultAsync(t => t.TripId == tripId && t.DriverId == driverId);

            if (trip == null) return "Trip not found or unauthorized";

            if (trip.Status != TripStatus.Assigned && trip.Status != TripStatus.InProgress)
                return "Trip cannot be cancelled in current state";

            // 2️⃣ تحديث حالة السائق وإرسال تحديث فوري له عبر SignalR
            if (trip.Driver != null)
            {
                trip.Driver.Status = (reason == TripCancelReason.Accident)
                    ? DriverStatus.offline
                    : DriverStatus.available;

                // 📡 إخبار تطبيق السائق بتغيير حالته فوراً (لتحديث الواجهة لديه)
                await _hubContext.Clients.Group($"user-{driverId}")
                    .SendAsync("UpdateDriverStatus", new
                    {
                        status = trip.Driver.Status.ToString(),
                        reason = reason.ToString()
                    });
            }

            // 2️⃣ إعادة تصفير الرحلة وتحديث "عداد" الوقت (مهم جداً للـ Background Service)
            trip.Status = TripStatus.SearchingDriver;
            trip.DriverId = null;
            trip.TripOfferSentAt = DateTime.UtcNow; // 🔥 إعادة ضبط المهلة (3 دقائق جديدة)
            trip.LastOfferedDriverId = null;        // 🔥 مسح آخر سائق للسماح بالبحث من جديد

            // 3️⃣ إعادة الطلبات لحالة "البحث" وتصفير الرحلة
            foreach (var tripOrder in trip.TripOrders)
            {
                tripOrder.StatusInTrip = TripOrderStatus.Unassigned;
                tripOrder.Order.Status = OrderStatus.SearchingDriver;
                tripOrder.UnassignedAt = DateTime.UtcNow;
            }

          
            _activeTripStore.RemoveDriverTrip(driverId);

            await _context.SaveChangesAsync();

            // 4️⃣ إخطار الركاب وإخراجهم من "مجموعة الرحلة" في SignalR
            foreach (var tripOrder in trip.TripOrders)
            {
                // إشعار (Push Notification)
                await _notification.SendNotificationAsync(
                    tripOrder.Order.PassengerId,
                    NotificationType.DriverCancelledTrip,
                    "تم إلغاء الرحلة",
                    "نعتذر، السائق ألغى الرحلة. جاري البحث عن بديل.",
                    tripOrder.OrderId,
                    trip.TripId,
                    extraData: new
                    {
                        tripId,
                        status = "cancelled",
                        reason = reason.ToString()
                    }
                );

                // أمر لحظي للتطبيق (SignalR) لإغلاق صفحة الرحلة
                await _hubContext.Clients.Group($"user-{tripOrder.Order.PassengerId}")
                    .SendAsync("LeaveTrip", tripId);
            }

            // 5️⃣ إخطار المكتب لتحديث الخريطة لديهم فوراً
            await _notification.SendOfficeNotificationAsync(
    officeUserId: _settings.OfficeUserId,
    type: NotificationType.DriverCancelledTrip,
    title: "Trip Cancelled",
    body: $"Trip {tripId} cancelled by driver {driverId}",
    orderId: null,
    tripId: tripId,
    extraData: new
    {
        driverId,
        reason = reason.ToString()
    },
    saveToDb: true
);

            if (trip.IsManuallyAssigned)
            {
                // رجّعها Auto
                trip.IsManuallyAssigned = false;

                await AssignTripEmergencyAsync(tripId, driverId);
            }
            else
            {
                await AssignTripEmergencyAsync(tripId, driverId);
            }

            return "Trip cancelled successfully";

        }

       


        private async Task<double> GetDriverWeightedRating(string driverId)
        {
            double minVotes = 20;

            var driverStats = await _context.Ratings
                .Where(r => r.TargetUserId == driverId)
                .GroupBy(r => 1)
                .Select(g => new
                {
                    Count = g.Count(),
                    Avg = g.Average(x => x.Stars)
                })
                .FirstOrDefaultAsync();

            if (driverStats == null)
                return 5;

            var globalAvg = await _context.Ratings
                .AverageAsync(r => (double?)r.Stars) ?? 5;

            double weighted =
                ((driverStats.Avg * driverStats.Count) + (globalAvg * minVotes)) /
                (driverStats.Count + minVotes);

            return Math.Round(weighted, 2);
        }

        private async Task RemoveDriverFromQueue(string driverId)
        {
            var entries = await _context.OfficeQueueEntries
                .Where(q => q.DriverId == driverId && q.Status == QueueStatus.InQueue)
                .ToListAsync();

            if (!entries.Any()) return;

            var now = DateTime.UtcNow;

            foreach (var entry in entries)
            {
                entry.Status = QueueStatus.LeftQueue;
                entry.LeftAt = DateTime.UtcNow;

              
            }

            await _context.SaveChangesAsync();

            await _notification.SendOfficeNotificationAsync(
      officeUserId: _settings.OfficeUserId,
      type: NotificationType.DriverLeftQueue,
      title: "Driver Left Queue",
      body: $"Driver {driverId} left the queue",
      orderId: null,
      tripId: null,
      extraData: new
      {
          driverId,
          action = "left",
          time = now
      },
      saveToDb: true
  );


        }

      



      











    }
}
