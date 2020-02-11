namespace Marten
{
    public static class MartenJsonNetExtensions
    {
        /// <summary>
        /// Use Jsn.NET serialization with Enum values
        /// stored as either integers or strings
        /// </summary>
        /// <param name="storeOptions"></param>
        /// <param name="enumStorage"></param>
        /// <param name="casing">Casing style to be used in serialization</param>
        /// <param name="collectionStorage">Allow to set collection storage as raw arrays (without explicit types)</param>
        /// <param name="nonPublicMembersStorage">Allow non public members to be used during deserialization</param>
        public static StoreOptions UseJsonNetSerializer(this StoreOptions storeOptions,
            EnumStorage enumStorage = EnumStorage.AsInteger,
            Casing casing = Casing.Default,
            CollectionStorage collectionStorage = CollectionStorage.Default,
            NonPublicMembersStorage nonPublicMembersStorage = NonPublicMembersStorage.Default
        )
        {
            var serializer = new Json.NET.JsonNetSerializer
            {
                EnumStorage = enumStorage,
                Casing = casing,
                CollectionStorage = collectionStorage,
                NonPublicMembersStorage = nonPublicMembersStorage
            };

            storeOptions.Serializer(serializer);

            return storeOptions;
        }
    }
}
