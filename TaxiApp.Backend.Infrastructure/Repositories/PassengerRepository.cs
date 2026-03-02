using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;
using TaxiApp.Backend.Core.Interfaces;
using TaxiApp.Backend.Core.Models;
using TaxiApp.Backend.Infrastructure.Data;

namespace TaxiApp.Backend.Infrastructure.Repositories
{
    public class PassengerRepository : IPassengerRepository
    {
        private readonly ApplicationDbContext context;
        private readonly UserManager<ApplicationUser> userManager;

        public PassengerRepository(ApplicationDbContext _context, UserManager<ApplicationUser> userManager)
        {
            context = _context;
            this.userManager = userManager;
        }

       

        public async Task<bool> UpdatePassengerProfileAsync(string userId, UpdatePassengerRequest request)
        {
            var user = await userManager.FindByIdAsync(userId);
            if (user == null)
                return false;

            var passenger = await context.Passengers
                .FirstOrDefaultAsync(p => p.UserId == userId);

            if (passenger == null)
                return false;

            // تحديث الاسم
            if (!string.IsNullOrWhiteSpace(request.FirstName))
                user.FirstName = request.FirstName;

            if (!string.IsNullOrWhiteSpace(request.LastName))
                user.LastName = request.LastName;

            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
                return false;

            // تحديث / حذف العنوان
            if (request.RemoveAddress)
                passenger.Address = null;
            else if (!string.IsNullOrWhiteSpace(request.Address))
                passenger.Address = request.Address;

            var folderPath = Path.Combine(Directory.GetCurrentDirectory(), "Images");

            if (!Directory.Exists(folderPath))
                Directory.CreateDirectory(folderPath);

            // حذف صورة
            if (request.RemoveProfilePhoto)
            {
                if (!string.IsNullOrEmpty(passenger.ProfilePhotoUrl))
                {
                    var oldPath = Path.Combine(folderPath, passenger.ProfilePhotoUrl);
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                passenger.ProfilePhotoUrl = null;
            }
            // رفع صورة جديدة
            else if (request.ProfilePhotoImg != null && request.ProfilePhotoImg.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(request.ProfilePhotoImg.FileName).ToLower();

                if (!allowedExtensions.Contains(extension))
                    return false;

                var fileName = Guid.NewGuid() + extension;
                var filePath = Path.Combine(folderPath, fileName);

                using var stream = System.IO.File.Create(filePath);
                await request.ProfilePhotoImg.CopyToAsync(stream);

                if (!string.IsNullOrEmpty(passenger.ProfilePhotoUrl))
                {
                    var oldPath = Path.Combine(folderPath, passenger.ProfilePhotoUrl);
                    if (System.IO.File.Exists(oldPath))
                        System.IO.File.Delete(oldPath);
                }

                passenger.ProfilePhotoUrl = fileName;
            }

            passenger.UpdatedAt = DateTime.UtcNow;

            await context.SaveChangesAsync();

            return true;
        }
    }
}
