using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S;
using TaxiApp.Backend.Core.Models;

namespace TaxiApp.Backend.Core.Interfaces
{
    public interface IDriverApprovalRepository
    {
        // جلب كل السائقين الذين ينتظرون الموافقة
        Task<IEnumerable<DriverPendingResponseDto>> GetPendingDriversAsync();

        // الموافقة على السائق وتغيير حالته إلى Active
        Task<bool> ApproveDriverAsync(string officeId,string driverId);

        // جلب بيانات سائق معين بالتفصيل
        Task<Driver?> GetDriverByIdAsync(string driverId);
    }
}