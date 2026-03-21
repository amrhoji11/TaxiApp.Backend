using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class DispatchRepository /*: *//*IDispatchRepository*/
    {
        private readonly ApplicationDbContext context;

        public DispatchRepository(ApplicationDbContext context)
        {
            this.context = context;
        }
        private async Task<List<DriverCandidate>> GetSharedDriverCandidatesAsync(Order order, IMapService mapService, TimeSpan maxExtraTime)
        {
            var result =  new List<DriverCandidate>();
            
            var activeTrips = await context.Trips
                .Where(a=>a.DriverId!=null &&(a.Status == TripStatus.Assigned || a.Status == TripStatus.InProgress))
                .Include(a=>a.Driver)
                .ThenInclude(a=>a.Vehicles)
                .Include(t => t.TripOrders)
            .ThenInclude(to => to.Order)
        .ToListAsync();


           

            foreach (var trip in activeTrips)
            {
                if (trip.Driver == null) continue;

                var vehicle = trip.Driver.Vehicles.FirstOrDefault(a=>a.DriverId == trip.DriverId && a.IsCurrent && a.IsActive);

                if (vehicle == null) continue;

                var currentPassenger =  trip.TripOrders
                    .Where(a=>a.StatusInTrip != TripOrderStatus.Cancelled && a.StatusInTrip != TripOrderStatus.DroppedOff)
                    .Sum(a => a.Order.PassengerCount);

                int availableSeats = vehicle.Seats - currentPassenger;

                if (availableSeats < order.PassengerCount) continue;

                if (order.RequiredVehicleSize.HasValue && order.RequiredVehicleSize.Value != vehicle.VehicleSize) continue;

                var extraEta = await mapService.GetAdditionalETAToTripAsync(trip,order);

                if (extraEta > maxExtraTime) continue;

                result.Add(new DriverCandidate
                {
                    DriverId = trip.DriverId,
                    Eta = extraEta,
                    IsShared = true

                });



            }

            return result;
        }
        private async Task<List<DriverCandidate>>  GetNearestDriverCandidatesAsync(Order order, IMapService mapService, TimeSpan maxEta)
        {
            var result = new List<DriverCandidate>();

            var drivers = await context.Drivers.Where(a=>a.Status == DriverStatus.available &&
            a.LastLat.HasValue &&
            a.LastLng.HasValue)
           .Include(a => a.Vehicles)
           .ToListAsync();

            foreach (var driver in drivers)
            {
                var vehicle = driver.Vehicles.FirstOrDefault(a=>a.IsCurrent && a.IsActive);

                if (vehicle == null) continue;

                if(vehicle.Seats < order.PassengerCount) continue;

                if(order.RequiredVehicleSize.HasValue && order.RequiredVehicleSize.Value != vehicle.VehicleSize) continue;

                var eta = await mapService.GetETAAsync(driver.LastLat.Value, driver.LastLng.Value, order.PickupLat, order.PickupLng);

                if (eta > maxEta) continue;

                result.Add(new DriverCandidate
                {
                    DriverId = driver.UserId,
                    Eta = eta,
                    IsShared = false
                });

            }
                
            return result;
            
        }
        private async Task<Driver?> GetNextDriverFromQueueAsync()
        {
           var nextEntry = await context.OfficeQueueEntries
                .Where(a=>a.Status == QueueStatus.InQueue)
                .OrderBy(a=>a.EnteredAt)
                .Include(q => q.Driver)
            .ThenInclude(d => d.Vehicles)
        .FirstOrDefaultAsync();


              if (nextEntry == null) return null;

              var driver = nextEntry.Driver;
              
              var vehicle  = driver.Vehicles.FirstOrDefault(a=>a.IsCurrent && a.IsActive);

            if (vehicle == null) return null;

            nextEntry.LeftAt = DateTime.UtcNow;
            nextEntry.Status = QueueStatus.LeftQueue;

            await context.SaveChangesAsync();

            return driver;


        }
        private async Task<int> CreateTripAsync(Order order, string driverId)
        {
            var existingTrip = await context.Trips
                .Where(a => a.DriverId == driverId && (a.Status == TripStatus.InProgress || a.Status == TripStatus.Assigned))
                .FirstOrDefaultAsync();

            Trip trip;

            if (existingTrip != null)
            {
                trip = existingTrip; // Pooling
            }
            else
            {
                trip = new Trip()
                {
                    DriverId = driverId,
                    Status = TripStatus.Assigned,
                    AssignedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow

                };

                await context.Trips.AddAsync(trip);
            }

            var tripOrder = new TripOrder
            {
                Trip = trip,
                OrderId = order.OrderId,
                AssignedAt = DateTime.UtcNow,
                StatusInTrip = TripOrderStatus.Assigned
            };

            await context.TripOrders.AddAsync(tripOrder);

            order.Status = OrderStatus.AssignedToTrip;
            order.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return trip.TripId;

        }
        public async Task<string> AssignDriverAsync(int orderId, IMapService mapService)
        {
            var order = await context.Orders.FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order == null) return "Order not found";
            if (order.Status != OrderStatus.Pending) return "Order is not pending";

            TimeSpan normalMaxShared = TimeSpan.FromMinutes(10);
            TimeSpan normalMaxEta = TimeSpan.FromMinutes(15);
            TimeSpan urgentMaxEta = TimeSpan.FromMinutes(10);

            List<DriverCandidate> candidates = new();

            if (order.Priority == OrderPriority.Urgent)
            {
                candidates = await GetNearestDriverCandidatesAsync(order,mapService,urgentMaxEta);
            }
            else
            {
                var sharedCandidates = await GetSharedDriverCandidatesAsync(order, mapService, normalMaxShared);

                var nearestCandidates = await GetNearestDriverCandidatesAsync(order, mapService, normalMaxEta);

                candidates = sharedCandidates.Concat(nearestCandidates).ToList();
            }

            if (!candidates.Any())
            {
                var queueDriver = await GetNextDriverFromQueueAsync();

                if(queueDriver == null ) return "No driver available";

                await CreateTripAsync(order,queueDriver.UserId);
                return "Trip created from queue";

            }

            var bestDriver = candidates.MinBy(a => a.Eta);
            await CreateTripAsync(order, bestDriver.DriverId);

            if(bestDriver.IsShared) return "Trip created with Shared Driver";

            return "Trip created with Near Driver";

        }
        public Task DriverResponseAsync(int orderId, string driverId, bool accepted)
        {
            throw new NotImplementedException();
        }

        public Task ReassignDriverAsync(int orderId)
        {
            throw new NotImplementedException();
        }
    }
}
