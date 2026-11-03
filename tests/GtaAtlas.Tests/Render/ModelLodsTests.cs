using CodeWalker.GameFiles;
using GtaAtlas.Render;

namespace GtaAtlas.Tests.Render;

public class ModelLodsTests
{
    private static readonly DrawableModel H = new(), M = new(), L = new(), V = new();

    private static Drawable Make(DrawableModel? high = null, DrawableModel? med = null, DrawableModel? low = null, DrawableModel? vlow = null) =>
        new()
        {
            DrawableModels = new DrawableModelsBlock
            {
                High = high is null ? null : [high],
                Med = med is null ? null : [med],
                Low = low is null ? null : [low],
                VLow = vlow is null ? null : [vlow],
            },
        };

    [Fact]
    public void Lists_levels_with_models()
    {
        Assert.Equal([DetailLevel.High, DetailLevel.Low], ModelLods.Available(Make(H, low: L)));
        Assert.Equal([DetailLevel.High], ModelLods.Available(new Drawable()));
    }

    [Fact]
    public void Hides_every_other_level()
    {
        var all = Make(H, M, L, V);
        Assert.Equal([M, L, V], ModelLods.Hidden(all, DetailLevel.High));
        Assert.Equal([H, M, V], ModelLods.Hidden(all, DetailLevel.Low));
    }

    [Theory]
    [InlineData(DetailLevel.Medium, DetailLevel.High)]   // missing: nearest more detailed
    [InlineData(DetailLevel.VeryLow, DetailLevel.Low)]
    [InlineData(DetailLevel.Low, DetailLevel.Low)]
    public void Missing_level_falls_back_to_more_detail(DetailLevel wanted, DetailLevel shown)
    {
        Assert.Equal(shown, ModelLods.Effective(Make(H, low: L), wanted));
    }

    [Fact]
    public void Falls_back_to_less_detail_when_nothing_better()
    {
        Assert.Equal(DetailLevel.Medium, ModelLods.Effective(Make(med: M, low: L), DetailLevel.High));
        Assert.Equal([L], ModelLods.Hidden(Make(med: M, low: L), DetailLevel.High));
    }

    [Fact]
    public void Drawable_without_models_block_hides_nothing()
    {
        Assert.Empty(ModelLods.Hidden(new Drawable(), DetailLevel.Low));
    }

    [Fact]
    public void Vehicle_high_comes_from_hi_model_and_lower_levels_from_regular_one()
    {
        var hi = Make(H);
        var lods = Make(H, M, L, V);
        Assert.Equal([DetailLevel.High, DetailLevel.Medium, DetailLevel.Low, DetailLevel.VeryLow], ModelLods.Vehicle(hi, lods));
        Assert.True(ModelLods.UsesHi(hi, lods, DetailLevel.High));
        Assert.False(ModelLods.UsesHi(hi, lods, DetailLevel.Medium));
    }

    [Fact]
    public void Vehicle_with_one_file_uses_its_levels()
    {
        Assert.Equal([DetailLevel.High], ModelLods.Vehicle(Make(H), null));
        Assert.True(ModelLods.UsesHi(Make(H), null, DetailLevel.Low));
        Assert.Equal([DetailLevel.High, DetailLevel.Low], ModelLods.Vehicle(null, Make(H, low: L)));
        Assert.False(ModelLods.UsesHi(null, Make(H, low: L), DetailLevel.High));
    }
}
