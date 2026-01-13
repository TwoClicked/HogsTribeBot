using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TribeBot.Core.Entities;

namespace TribeBot.Core.Interfaces
{
    public interface IKvKService
    {
        Task<KvKEvent> CreateKvKAsync(
            string name,
            DateTime startUtc,
            DateTime endUtc);

        Task<KvKEvent?> GetActiveKvKAsync();

        Task EndActiveKvKAsync();
    }
}
