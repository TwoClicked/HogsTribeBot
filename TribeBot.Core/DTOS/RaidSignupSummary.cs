using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TribeBot.Core.DTOS
{
    public class RaidSignupSummary
    {
        public IReadOnlyList<ulong> Yes { get; init; } = Array.Empty<ulong>();
        public IReadOnlyList<ulong> No { get; init; } = Array.Empty<ulong>();
        public IReadOnlyList<ulong> Maybe { get; init; } = Array.Empty<ulong>();
    }
}
