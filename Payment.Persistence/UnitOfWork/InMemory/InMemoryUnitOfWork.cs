using Payment.Application.Abstractions.Persistence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Payment.Persistence.UnitOfWork.InMemory
{
    public sealed class InMemoryUnitOfWork
        : IUnitOfWork
    {
        public async Task ExecuteInTransactionAsync(
            Func<CancellationToken, Task> operation,
            CancellationToken cancellationToken = default)
        {
            await operation(cancellationToken);
        }
    }
}
