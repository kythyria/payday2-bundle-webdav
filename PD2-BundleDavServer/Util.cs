using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PD2BundleDavServer
{
    static class AsyncEnumerable
    {
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
        public static async IAsyncEnumerable<T> Empty<T>()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        {
            yield break;
        }
    }
}
