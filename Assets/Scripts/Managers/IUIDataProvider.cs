public interface IUIDataProvider
{
    void RegisterInterest(UIDataType mask);
    void UnregisterInterest(UIDataType mask);
}