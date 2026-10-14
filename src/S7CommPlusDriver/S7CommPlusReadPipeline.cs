using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace S7CommPlusDriver
{
    internal static class S7CommPlusReadPipeline
    {
        private sealed class PendingRead
        {
            internal GetMultiVariablesRequest Request;
            internal int Start;
            internal int Count;
        }

        internal static int Execute(
            IReadOnlyList<ItemAddress> addresses, int maxItems, int depth,
            Func<GetMultiVariablesRequest, int> send,
            Func<GetMultiVariablesRequest, ISet<ushort>, (int Error, MemoryStream Pdu)> receive,
            Func<IS7pRequest, IS7pResponse, int> validate,
            out List<object> values, out List<ulong> errors)
        {
            values = Enumerable.Repeat<object>(null, addresses.Count).ToList();
            errors = Enumerable.Repeat(ulong.MaxValue, addresses.Count).ToList();
            var pending = new Dictionary<ushort, PendingRead>();
            var sequences = new HashSet<ushort>();
            var next = 0;

            int FillWindow()
            {
                while (pending.Count < depth && next < addresses.Count)
                {
                    var request = S7CommPlusProtocolSession.CreateReadRequestBatch(addresses, next, maxItems, out var count);
                    var error = send(request);
                    if (error != 0) return error;
                    if (!sequences.Add(request.SequenceNumber)) return S7Consts.errIsoInvalidPDU5;
                    pending.Add(request.SequenceNumber, new PendingRead { Request = request, Start = next, Count = count });
                    next += count;
                }
                return 0;
            }

            var result = FillWindow();
            if (result != 0) return result;
            while (pending.Count > 0)
            {
                var received = receive(pending.Values.First().Request, sequences);
                if (received.Error != 0) return received.Error;
                var response = GetMultiVariablesResponse.DeserializeFromPdu(received.Pdu);
                if (response == null || !pending.TryGetValue(response.SequenceNumber, out var completed))
                    return S7Consts.errIsoInvalidPDU5;
                result = validate(completed.Request, response);
                if (result != 0) return result;
                if (response.IntegrityId != unchecked(completed.Request.IntegrityId + completed.Request.SequenceNumber))
                    return S7Consts.errIsoInvalidPDU;
                // Preserve item failures and missing-value status; never expose partial
                // data as a successful logical read after a transport/validation failure.
                foreach (var value in response.Values)
                {
                    if (value.Key == 0 || value.Key > completed.Count) return S7Consts.errIsoInvalidPDU;
                    var index = completed.Start + (int)value.Key - 1;
                    values[index] = value.Value;
                    errors[index] = 0;
                }
                foreach (var error in response.ErrorValues)
                {
                    if (error.Key == 0 || error.Key > completed.Count) return S7Consts.errIsoInvalidPDU;
                    errors[completed.Start + (int)error.Key - 1] = error.Value;
                }
                pending.Remove(response.SequenceNumber);
                sequences.Remove(response.SequenceNumber);
                result = FillWindow();
                if (result != 0) return result;
            }
            return 0;
        }
    }
}
