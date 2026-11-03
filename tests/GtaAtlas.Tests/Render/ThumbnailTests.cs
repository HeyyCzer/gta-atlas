using System.Diagnostics;
using System.Drawing;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Render;
using SharpDX;
using Color = System.Drawing.Color;
using Rectangle = System.Drawing.Rectangle;

namespace GtaAtlas.Tests.Render;

public class FramingTests
{
    public static TheoryData<float, float, float, float, float, float> Boxes => new()
    {
        { -1, -1, -1, 1, 1, 1 },       // cube
        { -0.3f, -2.5f, -0.2f, 0.3f, 2.5f, 1.5f }, // long flat object (bench, car)
        { 4, 7, 0, 4.2f, 7.1f, 9 },    // tall pole away from the origin
    };

    [Theory]
    [MemberData(nameof(Boxes))]
    public void Fits_every_corner_on_screen_and_fills_the_tighter_axis(float x0, float y0, float z0, float x1, float y1, float z1)
    {
        var min = new Vector3(x0, y0, z0);
        var max = new Vector3(x1, y1, z1);
        const float fov = 0.6f, aspect = 1.5f, fill = 0.88f;
        var forward = Vector3.Normalize(new Vector3(-0.6f, 0.7f, -0.35f));
        var view = Matrix.LookAtRH(Vector3.Zero, forward, Vector3.UnitZ); // rotation only, like Camera.ViewMatrix

        var (target, distance) = Framing.Fit(min, max, view, fov, aspect, fill);

        var eye = target - forward * distance;
        var viewProj = Matrix.LookAtRH(eye, target, Vector3.UnitZ) * Matrix.PerspectiveFovRH(fov, aspect, 0.01f, 1000f);
        float minX = 9, maxX = -9, minY = 9, maxY = -9;
        for (var i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
            var p = Vector3.TransformCoordinate(c, viewProj);
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }

        Assert.InRange(minX, -fill - 1e-3f, 1);
        Assert.InRange(maxX, -1, fill + 1e-3f);
        Assert.InRange(minY, -fill - 1e-3f, 1);
        Assert.InRange(maxY, -1, fill + 1e-3f);
        Assert.True(Math.Max(maxX - minX, maxY - minY) > 2 * fill - 0.05f, $"not tight: x {minX:0.00}..{maxX:0.00}, y {minY:0.00}..{maxY:0.00}");
        Assert.True(Math.Abs(minX + maxX) < 0.05f && Math.Abs(minY + maxY) < 0.05f, $"off-centre: x {minX:0.00}..{maxX:0.00}, y {minY:0.00}..{maxY:0.00}");
    }
}

public class ThumbnailMatteTests
{
    [Fact]
    public void Recovers_alpha_and_colour_from_black_and_white_renders()
    {
        // Pixels: background, opaque red, 50% green (glass), 25% white edge.
        byte[] onBlack = [0, 0, 0, 255, 200, 0, 0, 255, 0, 64, 0, 255, 64, 64, 64, 255];
        byte[] onWhite = [255, 255, 255, 255, 200, 0, 0, 255, 128, 192, 128, 255, 255, 255, 255, 255];

        var bgra = ThumbnailMatte.Combine(onBlack, onWhite);

        Assert.Equal([0, 0, 0, 0], bgra[0..4]);
        Assert.Equal([0, 0, 200, 255], bgra[4..8]);
        Assert.Equal(127, bgra[11]);
        Assert.InRange(bgra[9], 126, 130); // green unpremultiplied: 64 / 0.5
        Assert.Equal(64, bgra[15]);
        Assert.Equal(255, bgra[12]);
    }

    [Fact]
    public void Empty_when_nothing_is_opaque()
    {
        var black = new byte[64 * 4];
        var white = Enumerable.Repeat((byte)255, 64 * 4).ToArray();
        Assert.True(ThumbnailMatte.IsEmpty(ThumbnailMatte.Combine(black, white)));
        Assert.False(ThumbnailMatte.IsEmpty(ThumbnailMatte.Combine(black, black)));
    }
}

[Collection(GameCollection.Name)]
public class ThumbnailRendererTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Renders_prop_and_vehicle_on_transparent_background()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        var session = GameSession.Open(dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });
        using var assets = GameAssets.Start(session.Install);
        await assets.Ready.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);

        using var renderer = new ThumbnailRenderer(assets);
        foreach (var (name, render) in new (string, Func<Bitmap>)[]
                 {
                     ("adder", () => renderer.RenderVehicle("adder")),
                     ("prop_bench_01a", () => renderer.RenderProp(CodeWalker.GameFiles.JenkHash.GenHash("prop_bench_01a"))),
                     ("prop_vend_soda_01", () => renderer.RenderProp(CodeWalker.GameFiles.JenkHash.GenHash("prop_vend_soda_01"))),
                     ("prop_bench_01a again", () => renderer.RenderProp(CodeWalker.GameFiles.JenkHash.GenHash("prop_bench_01a"))),
                 })
        {
            var sw = Stopwatch.StartNew();
            using var bitmap = await Task.Run(render, TestContext.Current.CancellationToken);
            var png = Path.Combine(Path.GetTempPath(), "GtaAtlasTests", $"thumb_{name.Replace(' ', '_')}.png");
            bitmap.Save(png);
            var opaque = Bounds(bitmap);
            output.WriteLine($"{name}: {sw.ElapsedMilliseconds} ms, opaque {opaque}, {png}");

            Assert.Equal(ThumbnailRenderer.DefaultWidth, bitmap.Width);
            Assert.Equal(0, bitmap.GetPixel(0, 0).A);
            Assert.Equal(0, bitmap.GetPixel(bitmap.Width - 1, bitmap.Height - 1).A);
            // Framed: the model spans most of one axis and stays off the edges.
            Assert.True(opaque.Width > bitmap.Width * 0.6 || opaque.Height > bitmap.Height * 0.6, $"too small: {opaque}");
            Assert.True(opaque.Left > 0 && opaque.Top > 0 && opaque.Right < bitmap.Width && opaque.Bottom < bitmap.Height, $"clipped: {opaque}");
            if (name == "adder") // unpainted body: white (paint is carcols, not loaded)
                Assert.True(Brightness(bitmap) > 100, $"unlit: mean {Brightness(bitmap):0}");
            if (name == "prop_vend_soda_01") // red eCola machine
                Assert.True(HasColour(bitmap), "untextured (all grey)");
        }

        Assert.Throws<ModelNotFoundException>(() => renderer.RenderVehicle("not_a_vehicle"));
    }

    private static Rectangle Bounds(Bitmap b)
    {
        int x0 = b.Width, y0 = b.Height, x1 = -1, y1 = -1;
        for (var y = 0; y < b.Height; y++)
        for (var x = 0; x < b.Width; x++)
        {
            if (b.GetPixel(x, y).A < 128)
                continue;
            x0 = Math.Min(x0, x);
            y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x);
            y1 = Math.Max(y1, y);
        }
        return Rectangle.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>Mean luminance of opaque pixels: catches an unlit (black) model.</summary>
    private static double Brightness(Bitmap b)
    {
        double sum = 0;
        var n = 0;
        for (var y = 0; y < b.Height; y += 2)
        for (var x = 0; x < b.Width; x += 2)
        {
            var c = b.GetPixel(x, y);
            if (c.A < 200)
                continue;
            sum += 0.3 * c.R + 0.59 * c.G + 0.11 * c.B;
            n++;
        }
        return n == 0 ? 0 : sum / n;
    }

    private static bool HasColour(Bitmap b)
    {
        var coloured = 0;
        for (var y = 0; y < b.Height; y += 2)
        for (var x = 0; x < b.Width; x += 2)
        {
            Color c = b.GetPixel(x, y);
            if (c.A > 200 && Math.Max(Math.Abs(c.R - c.G), Math.Max(Math.Abs(c.G - c.B), Math.Abs(c.R - c.B))) > 12)
                coloured++;
        }
        return coloured > 50;
    }
}
