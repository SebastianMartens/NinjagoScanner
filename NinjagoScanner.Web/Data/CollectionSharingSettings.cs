namespace NinjagoScanner.Web.Data;

public enum CollectionVisibility
{
    /// <summary>"Nur ich" - nobody but the owner.</summary>
    Private,
    /// <summary>"Freunde" - accepted friends (the default when no settings row exists).</summary>
    Friends
}

public class CollectionSharingSettings
{
    public string CollectionId { get; set; } = default!;
    public CollectionVisibility Visibility { get; set; } = CollectionVisibility.Friends;
}
