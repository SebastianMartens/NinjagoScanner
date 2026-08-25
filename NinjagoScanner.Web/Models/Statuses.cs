namespace NinjagoScanner.Web.Models;

public static class AnalysisStatuses
{
    public const string Ok = "ok";
    public const string Uncertain = "uncertain";
    public const string Failed = "failed";
    public const string Pending = "pending";
}

public static class ReviewStatuses
{
    public const string Unreviewed = "unreviewed";
    public const string Verified = "verified";
    public const string Incorrect = "incorrect";
}

public static class Languages
{
    public const string German = "de";
    public const string English = "en";
    public const string Polish = "pl";
    public const string Unknown = "unknown";
    public const string Default = German;
}
