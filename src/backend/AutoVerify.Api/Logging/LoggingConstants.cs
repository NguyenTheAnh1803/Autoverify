namespace AutoVerify.Api.Logging;

public static class LoggingConstants
{
    public const string LogTypeProperty = "LogType";
    public const string UserIdProperty = "UserId";
    public const string JobNameProperty = "JobName";
    public const string AuditActionProperty = "AuditAction";
    public const string TargetTypeProperty = "TargetType";
    public const string TargetIdProperty = "TargetId";
    public const string ClientIpProperty = "ClientIp";

    public static class LogTypes
    {
        public const string Special = "Special";
        public const string UserEvent = "UserEvent";
        public const string Auto = "Auto";
        public const string App = "App";
    }
}

