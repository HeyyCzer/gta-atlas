using GtaAtlas.Data;

namespace GtaAtlas.App.Browsing;

/// <summary>Everything the user has chosen in the shell (category, search box, filters), turned into a query.</summary>
public sealed record BrowseState
{
    public Category Category { get; init; } = Category.Props;
    public string Text { get; init; } = "";
    public string? Dlc { get; init; }
    public Gender Gender { get; init; }

    /// <summary>Only the user's favorites within the current category.</summary>
    public bool FavoritesOnly { get; init; }

    /// <summary>Tag chips toggled on by the user (on top of the category's own tags).</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Switching category drops filters that belong to the old one.</summary>
    public BrowseState WithCategory(Category category) =>
        this with { Category = category, Tags = [], Gender = category.HasGender ? Gender : Gender.Any, FavoritesOnly = false };

    public BrowseState ToggleTag(string tag) =>
        this with { Tags = Tags.Contains(tag) ? Tags.Where(t => t != tag).ToList() : [.. Tags, tag] };

    public SearchQuery ToQuery(int offset = 0, int limit = 100)
    {
        var tags = Category.Tags.Concat(Tags).ToList();
        if (Category.HasGender && Gender != Gender.Any)
            tags.Add(Gender.ToString());

        return new SearchQuery
        {
            Text = string.IsNullOrWhiteSpace(Text) ? null : Text.Trim(),
            Kind = Category.Kind,
            Dlc = Dlc,
            Tags = tags.Distinct().ToList(),
            FavoritesOnly = FavoritesOnly,
            Offset = offset,
            Limit = limit,
        };
    }
}
