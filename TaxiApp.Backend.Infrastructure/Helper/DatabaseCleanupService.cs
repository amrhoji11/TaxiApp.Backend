using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Helper
{
    public class DatabaseCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DatabaseCleanupService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await CleanDatabase();

                // تشغيل مرة كل 24 ساعة
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }

        private async Task CleanDatabase()
        {
            using var scope = _scopeFactory.CreateScope();

            var context = scope.ServiceProvider
                .GetRequiredService<ApplicationDbContext>();

            var date = DateTime.UtcNow.AddDays(-1);

            // حذف مواقع السائقين الأقدم من يوم
            var oldLocations = context.DriverLocations
                .Where(x => x.RecordedAt < date);

            context.DriverLocations.RemoveRange(oldLocations);

            // حذف الإشعارات الأقدم من 7 أيام
            var oldNotifications = context.Notifications
                .Where(x => x.CreatedAt < DateTime.UtcNow.AddDays(-7));

            context.Notifications.RemoveRange(oldNotifications);

            await context.SaveChangesAsync();
        }
    }
}
