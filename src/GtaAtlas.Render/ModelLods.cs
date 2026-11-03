using System.Collections.Generic;
using System.Linq;
using CodeWalker.GameFiles;

namespace GtaAtlas.Render
{
    /// <summary>Drawable level of detail, most detailed first (same order as <see cref="DrawableModelsBlock"/>).</summary>
    public enum DetailLevel
    {
        High,
        Medium,
        Low,
        VeryLow,
    }

    /// <summary>Which models of a drawable to show for a chosen <see cref="DetailLevel"/>.</summary>
    public static class ModelLods
    {
        public static DrawableModel[] Models(DrawableBase drawable, DetailLevel level)
        {
            var models = drawable?.DrawableModels;
            var list = level switch
            {
                DetailLevel.High => models?.High,
                DetailLevel.Medium => models?.Med,
                DetailLevel.Low => models?.Low,
                _ => models?.VLow,
            };
            return list is { Length: > 0 } ? list : null;
        }

        /// <summary>Levels the drawable has models for. A drawable without a models block counts as High only.</summary>
        public static IReadOnlyList<DetailLevel> Available(DrawableBase drawable)
        {
            if (drawable?.DrawableModels is null)
                return [DetailLevel.High];
            var levels = All.Where(l => Models(drawable, l) is not null).ToList();
            return levels.Count > 0 ? levels : [DetailLevel.High];
        }

        /// <summary>
        /// The level actually shown when <paramref name="wanted"/> is asked for: itself if present, else the nearest
        /// more detailed one, else the nearest less detailed one (wheels and glass often have fewer levels than the body).
        /// </summary>
        public static DetailLevel Effective(DrawableBase drawable, DetailLevel wanted) => Effective(Available(drawable), wanted);

        public static DetailLevel Effective(IReadOnlyList<DetailLevel> available, DetailLevel wanted)
        {
            if (available.Contains(wanted))
                return wanted;
            var better = available.Where(l => l < wanted).ToList();
            return better.Count > 0 ? better.Max() : available.Min();
        }

        /// <summary>Models to hide so that only <see cref="Effective"/> renders.</summary>
        public static IEnumerable<DrawableModel> Hidden(DrawableBase drawable, DetailLevel wanted)
        {
            if (drawable?.DrawableModels is null)
                return [];
            var shown = Effective(drawable, wanted);
            return All.Where(l => l != shown).SelectMany(l => Models(drawable, l) ?? []);
        }

        /// <summary>
        /// Levels of a vehicle split over two files: High is the <c>_hi</c> model when there is one, lower levels come
        /// from the regular model (whose own High is a mid-detail mesh, so it's only used without a <c>_hi</c>).
        /// </summary>
        public static IReadOnlyList<DetailLevel> Vehicle(DrawableBase hi, DrawableBase lods)
        {
            if (hi is null)
                return Available(lods);
            if (lods is null)
                return [DetailLevel.High];
            return [DetailLevel.High, .. Available(lods).Where(l => l != DetailLevel.High)];
        }

        /// <summary>Whether <paramref name="wanted"/> draws the <c>_hi</c> model rather than the regular one.</summary>
        public static bool UsesHi(DrawableBase hi, DrawableBase lods, DetailLevel wanted) =>
            hi is not null && (lods is null || Effective(Vehicle(hi, lods), wanted) == DetailLevel.High);

        private static readonly DetailLevel[] All = [DetailLevel.High, DetailLevel.Medium, DetailLevel.Low, DetailLevel.VeryLow];
    }
}
