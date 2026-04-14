using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Interfaces;

namespace TaxiApp.Backend.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles ="Passenger")]
    public class FavoriteLocationsController : ControllerBase
    {
        private readonly IFavoriteLocationsRepository favoriteLocationsRepository;

        public FavoriteLocationsController(IFavoriteLocationsRepository favoriteLocationsRepository)
        {
            this.favoriteLocationsRepository = favoriteLocationsRepository;
        }

        // إضافة موقع جديد
        [HttpPost("AddFavoriteLocation")]
        public async Task<IActionResult> AddLocation([FromBody] AddFavoriteLocationDto dto)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await favoriteLocationsRepository.AddLocationAsync(userId, dto);

            return Ok(new { message = "تمت إضافة الموقع إلى المفضلة" });
        }

        // عرض المواقع المفضلة
        [HttpGet("GetAllFavoriteLocations")]
        public async Task<IActionResult> GetLocations()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var locations = await favoriteLocationsRepository.GetLocationsAsync(userId);

            return Ok(locations);
        }

        // حذف موقع مفضل
        [HttpDelete("DeleteFavoriteLocation/{locationId}")]
        public async Task<IActionResult> DeleteLocation(int locationId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var success = await favoriteLocationsRepository.DeleteLocationAsync(userId, locationId);

            if (!success)
                return NotFound(new { message = "الموقع غير موجود" });

            return Ok(new { message = "تم حذف الموقع من المفضلة" });
        }

    }
}
