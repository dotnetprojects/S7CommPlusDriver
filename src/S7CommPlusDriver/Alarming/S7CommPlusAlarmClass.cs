using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace S7CommPlusDriver.Alarming
{
    /// <summary>A configured alarm class read from the downloaded program's engineering metadata.</summary>
    public sealed class S7CommPlusAlarmClass
    {
        internal S7CommPlusAlarmClass(ushort id, string name, int? priority, bool? acknowledgementRequired,
            IDictionary<int, string> displayNames, IDictionary<int, string> shortNames)
        {
            Id = id;
            Name = name;
            Priority = priority;
            AcknowledgementRequired = acknowledgementRequired;
            DisplayNames = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(displayNames));
            ShortNames = new ReadOnlyDictionary<int, string>(new Dictionary<int, string>(shortNames));
        }

        /// <summary>The class ID reported by <see cref="S7CommPlusAlarmHmiInfo.AlarmClass"/>.</summary>
        public ushort Id { get; }
        public string Name { get; }
        /// <summary>Configured class priority, or null if absent. Independent of an individual message's priority.</summary>
        public int? Priority { get; }
        public bool? AcknowledgementRequired { get; }
        /// <summary>Configured display names keyed by LCID; empty when not downloaded.</summary>
        public IReadOnlyDictionary<int, string> DisplayNames { get; }
        /// <summary>Configured short names keyed by LCID; empty when not downloaded.</summary>
        public IReadOnlyDictionary<int, string> ShortNames { get; }
    }
}
