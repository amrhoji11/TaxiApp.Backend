using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class DriverApprovalRepository : IDriverApprovalRepository
    {
        private readonly ApplicationDbContext _context;

        public DriverApprovalRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<DriverPendingResponseDto>> GetPendingDriversAsync()
        {
            // جلب السائقين مع بياناتهم الشخصية من جدول Users
            var drivers = await _context.DriverApprovals
        .Include(a => a.Driver)
            .ThenInclude(d => d.User)
        .Where(a => a.Status == ApprovalStatus.pending)
        .Select(a => new DriverPendingResponseDto
        {
            UserId= a.DriverId,
            FullName= a.Driver.User.FirstName+" "+a.Driver.User.LastName,
            PhoneNumber=a.Driver.User.PhoneNumber
        })
        .ToListAsync();

            return drivers;


        }

        public async Task<bool> ApproveDriverAsync(string officeId,string driverId)
        {
            var approval = await _context.DriverApprovals
         .FirstOrDefaultAsync(a => a.DriverId == driverId);

            if (approval == null)
                return false;

            if (approval.Status != ApprovalStatus.pending)
                return false;


            approval.Status = ApprovalStatus.approved;
            approval.ReviewedByUserId = officeId;
            approval.ReviewedAt = DateTime.UtcNow;

            // عند الموافقة نجعله Offline مبدئياً
            var driver = await _context.Drivers.FindAsync(driverId);
            if (driver != null)
                driver.Status = DriverStatus.offline;

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