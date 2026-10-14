using System.Collections.Generic;

namespace S7CommPlusDriver
{
    // Sessions implementing this contract own item-count batching for a complete
    // logical read, allowing the production protocol session to overlap requests.
    internal interface IS7CommPlusBulkReadSession
    {
        int ReadValues(List<ItemAddress> addresses, out List<object> values, out List<ulong> errors);
    }
}
