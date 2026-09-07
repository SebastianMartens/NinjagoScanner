namespace NinjagoScanner.Web.Data;

public enum CollectionRole
{
    Owner,
    Reader
}

public class CollectionMembership
{
    public string CollectionId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public CollectionRole Role { get; set; }
}
