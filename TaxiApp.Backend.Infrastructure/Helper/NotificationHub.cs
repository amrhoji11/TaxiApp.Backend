using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Infrastructure.Helper
{
    [Authorize]
    public class NotificationHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                // إضافة المستخدم إلى مجموعة خاصة به
                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    $"user-{userId}");
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(
                    Context.ConnectionId,
                    $"user-{userId}");
            }

            await base.OnDisconnectedAsync(exception);
        }

        // للركاب للانضمام لرحلة
        public async Task JoinTrip(int tripId)
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                $"trip-{tripId}");
        }

        // للخروج من الرحلة
        public async Task LeaveTrip(int tripId)
        {
            await Groups.RemoveFromGroupAsync(
                Context.ConnectionId,
                $"trip-{tripId}");
        }

        // للمكتب لمراقبة السائقين
        public async Task JoinOffice()
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                "office");
        }
    }
}
