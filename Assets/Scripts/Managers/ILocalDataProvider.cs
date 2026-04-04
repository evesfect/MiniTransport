public interface ILocalDataProvider
{
    void RegisterInterest(SyncDataType mask);
    void UnregisterInterest(SyncDataType mask);
}