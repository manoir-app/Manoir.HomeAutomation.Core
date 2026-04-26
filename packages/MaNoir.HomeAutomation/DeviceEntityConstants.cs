namespace MaNoir.HomeAutomation;

public static class DeviceEntityConstants
{
    public static class Kinds
    {
        public const string MainServer = "manoirapp:device/home-graph";
        public const string HomeAutomation = "manoirapp:device/homeautomation";
        public const string Display = "manoirapp:device/display";
        public const string Security = "manoirapp:device/security";
        public const string Network = "manoirapp:device/network";
        public const string MobileDevice = "manoirapp:device/mobiledevice";
    }

    public static class Categories
    {
        public const string Default = "";
        public const string Identity = "identity";
        public const string Status = "status";
        public const string Configuration = "config";
    }
}