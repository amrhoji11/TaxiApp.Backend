using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TaxiApp.Backend.Core.Models
{
    public class DriverCandidate
    {
        public string DriverId { get; set; } = null!;
        public TimeSpan Eta { get; set; }
        public bool IsShared { get; set; }
    }
}
