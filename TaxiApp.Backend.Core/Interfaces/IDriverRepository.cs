using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;

namespace TaxiApp.Backend.Core.Interfaces
{
    public interface IDriverRepository
    {
        Task<bool> UpdateDriverProfileAsync(string userId, UpdateDriverRequest request);
    }
}
