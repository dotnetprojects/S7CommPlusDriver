namespace S7CommPlusDriver
{
    public sealed class S7CommPlusCommunicationResources
    {
        internal S7CommPlusCommunicationResources(S7CommPlusCommunicationResourceSnapshot resources)
        {
            TagsPerReadRequestMax = resources.TagsPerReadRequestMax;
            TagsPerWriteRequestMax = resources.TagsPerWriteRequestMax;
            PlcAttributesMax = resources.PlcAttributesMax;
            PlcAttributesFree = resources.PlcAttributesFree;
            PlcSubscriptionsMax = resources.PlcSubscriptionsMax;
            PlcSubscriptionsFree = resources.PlcSubscriptionsFree;
            SubscriptionMemoryMax = resources.SubscriptionMemoryMax;
            SubscriptionMemoryFree = resources.SubscriptionMemoryFree;
        }

        public int TagsPerReadRequestMax { get; }
        public int TagsPerWriteRequestMax { get; }
        public int PlcAttributesMax { get; }
        public int PlcAttributesFree { get; }
        public int PlcSubscriptionsMax { get; }
        public int PlcSubscriptionsFree { get; }
        public int SubscriptionMemoryMax { get; }
        public int SubscriptionMemoryFree { get; }
    }
}
