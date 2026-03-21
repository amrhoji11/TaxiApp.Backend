using Microsoft.AspNetCore.SignalR;
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
    public class NotificationRepository : INotificationRepository
    {
        private readonly IHubContext<NotificationHub> _hub;
        private readonly ApplicationDbContext _context;

        public NotificationRepository(
            IHubContext<NotificationHub> hub,
            ApplicationDbContext context)
        {
            _hub = hub;
            _context = context;
        }

        public async Task SendNotificationAsync(
        string userId,
        NotificationType type,
        string title,
        string body,
        int? orderId = null,
        int? tripId = null)
        {
            var notification = new Notification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Body = body,
                OrderId = orderId,
                TripId = tripId,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();

            // إرسال فوري عبر SignalR
            await _hub.Clients
                .Group($"user-{userId}")
                .SendAsync("ReceiveNotification", new
                {
                    notification.NotificationId,
                    notification.Type,
                    notification.Title,
                    notification.Body,
                    notification.OrderId,
                    notification.TripId,
                    notification.CreatedAt
                });
        }
    }
}
