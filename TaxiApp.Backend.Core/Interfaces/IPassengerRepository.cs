using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TaxiApp.Backend.Core.DTO_S.AuthDto.Requests;

namespace TaxiApp.Backend.Core.Interfaces
{
    public interface IPassengerRepository
    {
        Task<bool> UpdatePassengerProfileAsync(string userId, UpdatePassengerRequest request);
    }
}
