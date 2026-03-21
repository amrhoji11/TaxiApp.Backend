using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Core.Interfaces
{
    public interface IDispatchRepository
    {
        Task<string> AssignDriverAsync(int orderId);
        Task ReassignDriverAsync(int orderId);
        Task DriverResponseAsync(int orderId, string driverId, bool accepted);
    }
}
