using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class DriverRepository : IDriverRepository
    {
        private readonly ApplicationDbContext _context;

        public DriverRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Driver>> GetPendingDriversAsync()
        {
            // جلب السائقين مع بياناتهم الشخصية من جدول Users
            return await _context.Drivers
                .Include(d => d.User)
                .Where(d => d.Status == DriverStatus.Pending)
                .ToListAsync();
        }

        public async Task<bool> ApproveDriverAsync(string driverId)
        {
            var driver = await _context.Drivers.FindAsync(driverId);
            if (driver == null) return false;

            driver.Status = DriverStatus.available; // تغيير الحالة لنشط
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<Driver?> GetDriverByIdAsync(string driverId)
        {
            return await _context.Drivers
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.UserId == driverId);
        }
    }
}