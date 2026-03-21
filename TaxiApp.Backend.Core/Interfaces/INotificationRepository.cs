using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Core.Interfaces
{
    public interface INotificationRepository
    {
        Task SendNotificationAsync(
        string userId,
        NotificationType type,
        string title,
        string body,
        int? orderId = null,
        int? tripId = null);


    }
}
