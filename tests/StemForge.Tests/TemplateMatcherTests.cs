using System.Drawing;
using System.Drawing.Imaging;
using StemForge.Core.Live.Automation;
using Xunit;

namespace StemForge.Tests;

public sealed class TemplateMatcherTests
{
    /// <summary>Draws the reference pattern; used for the scene and for the template itself.</summary>
    private static void DrawPattern(Graphics g, int x, int y)
    {
        using var brush = new SolidBrush(Color.FromArgb(90, 160, 220));
        g.FillRectangle(brush, x, y, 24, 12);
        using var inner = new SolidBrush(Color.FromArgb(15, 15, 15));
        g.FillRectangle(inner, x + 6, y + 3, 8, 6);
    }

    private static Bitmap MakeScene(int width, int height, int patchX, int patchY, out Bitmap patch)
    {
        var scene = new Bitmap(width, height, PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(scene))
        {
            g.Clear(Color.FromArgb(32, 34, 38));

            // A few distractors.
            using var brushA = new SolidBrush(Color.FromArgb(12, 12, 12));
            g.FillRectangle(brushA, 0, 0, width / 3, height / 4);
            using var brushB = new SolidBrush(Color.FromArgb(220, 220, 220));
            g.FillRectangle(brushB, width / 2, height / 2, width / 4, height / 5);

            // The pattern we search for. Its two colours differ sharply in luminance,
            // which is what a grayscale matcher can lock onto.
            DrawPattern(g, patchX, patchY);
        }

        // The reference is drawn with the same primitives so it is pixel-identical to
        // the scene region (copying pixels out of a live GDI+ surface is not exact).
        patch = new Bitmap(24, 12);
        using (var g = Graphics.FromImage(patch))
        {
            g.Clear(Color.FromArgb(90, 160, 220));
            DrawPattern(g, 0, 0);
        }

        return scene;
    }

    [Theory]
    [InlineData(40, 30)]
    [InlineData(300, 180)]
    [InlineData(512, 400)]
    public void TheTemplateIsFoundAtTheRightPlace(int x, int y)
    {
        using var scene = MakeScene(600, 460, x, y, out var patch);
        using var template = patch;

        var match = TemplateMatcher.Find(scene, template, threshold: 0.7);

        Assert.True(match.Found, $"score={match.Score:F4} at {match.X},{match.Y} (expected {x},{y})");
        Assert.InRange(match.X, x - 2, x + 2);
        Assert.InRange(match.Y, y - 2, y + 2);
    }

    [Fact]
    public void AnAbsentTemplateIsReportedAsNotFound()
    {
        using var scene = MakeScene(300, 200, 40, 30, out _);
        using var absent = new Bitmap(24, 12);
        using (var g = Graphics.FromImage(absent)) g.Clear(Color.FromArgb(7, 250, 3));

        var match = TemplateMatcher.Find(scene, absent, threshold: 0.95);

        Assert.False(match.Found);
    }

    [Fact]
    public void ATemplateLargerThanTheSceneIsHandled()
    {
        using var scene = new Bitmap(20, 20);
        using var template = new Bitmap(50, 50);

        var match = TemplateMatcher.Find(scene, template);

        Assert.False(match.Found);
    }
}
