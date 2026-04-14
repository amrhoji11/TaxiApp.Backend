using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Core.DTO_S
{
    public class DriverCandidate
    {
        public string DriverId { get; set; } = default!;
        public TimeSpan Eta { get; set; }
        public bool IsShared { get; set; }
        public double Score { get; set; }
        public double DistanceMeters { get; set; }
    }
}
