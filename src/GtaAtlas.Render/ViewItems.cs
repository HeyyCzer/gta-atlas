using System;
using System.Collections.Generic;
using CodeWalker.GameFiles;
using CodeWalker.Rendering;
using SharpDX;

namespace GtaAtlas.Render
{
    /// <summary>A model shown alone at the origin: a prop archetype or a vehicle.</summary>
    internal abstract class ViewItem
    {
        public Vector3 Center { get; protected init; }
        public float Radius { get; protected init; }
        public Vector3 BBMin { get; protected init; }
        public Vector3 BBMax { get; protected init; }
        public IReadOnlyList<DetailLevel> Lods { get; protected init; }

        /// <summary>Camera yaw that shows the model's front three-quarter view (props face -Y, vehicles +Y).</summary>
        public float FrontYaw { get; protected init; } = -0.25f * MathUtil.Pi;

        public abstract void Render(Renderer renderer, DetailLevel lod);

        /// <summary>Loads a prop's model, blocking; call off the UI thread after <see cref="GameAssets.Ready"/>.</summary>
        public static ViewItem Prop(GameAssets assets, uint hash)
        {
            var arch = assets.Cache.GetArchetype(hash) ?? throw new ModelNotFoundException("Archetype not found in game files.");
            var drawable = assets.LoadDrawable(arch) ?? throw new ModelNotFoundException("Model file not found in game files.");
            return new ArchetypeItem(arch, drawable);
        }

        /// <summary>Loads a vehicle's models, blocking like <see cref="Prop"/>.</summary>
        public static ViewItem Vehicle(GameAssets assets, string name)
        {
            var models = assets.LoadVehicle(name) ?? throw new ModelNotFoundException("Vehicle model not found in game files.");
            return new VehicleItem(models, JenkHash.GenHash(name.ToLowerInvariant()));
        }
    }

    internal sealed class ArchetypeItem : ViewItem
    {
        private readonly Archetype _archetype;

        public ArchetypeItem(Archetype archetype, DrawableBase drawable)
        {
            _archetype = archetype;
            Center = archetype.BSCenter;
            Radius = archetype.BSRadius;
            BBMin = archetype.BBMin;
            BBMax = archetype.BBMax;
            Lods = ModelLods.Available(drawable);
        }

        public override void Render(Renderer renderer, DetailLevel lod) => renderer.RenderArchetype(_archetype, null, null, false);
    }

    internal sealed class VehicleItem : ViewItem
    {
        private readonly VehicleModels _models;
        private readonly uint _txd;

        /// <summary>Same as Renderer.RenderVehicle, without CodeWalker's Vehicle (which needs the slow vehicles.meta load).</summary>
        public VehicleItem(VehicleModels models, uint txd)
        {
            _models = models;
            _txd = txd; // the vehicle's own ytd shares its model name
            var main = (models.Hi ?? models.Lods).Drawable;
            Center = main.BoundingCenter;
            Radius = main.BoundingSphereRadius;
            BBMin = main.BoundingBoxMin;
            BBMax = main.BoundingBoxMax;
            Lods = ModelLods.Vehicle(models.Hi?.Drawable, models.Lods?.Drawable);
            FrontYaw = 0.75f * MathUtil.Pi;
        }

        public override void Render(Renderer renderer, DetailLevel lod)
        {
            var hi = ModelLods.UsesHi(_models.Hi?.Drawable, _models.Lods?.Drawable, lod);
            renderer.RenderFragment(null, null, hi ? _models.Hi : _models.Lods, _txd);
        }
    }

    /// <summary>The game files have no model for the asset; retrying won't help.</summary>
    public sealed class ModelNotFoundException(string message) : Exception(message);

    /// <summary>
    /// Hides the models of other LODs in every drawable rendered this frame (the renderer draws all levels with
    /// <c>SelectionFlagsTestAll</c>). Drawables seen for the first time were already queued with all levels, so they
    /// show mixed for one frame. Flags are reset per item so the renderer's dictionary doesn't keep old models alive.
    /// Render thread only.
    /// </summary>
    internal sealed class LodFilter
    {
        private readonly HashSet<DrawableBase> _flagged = new();
        private ViewItem _item;
        private DetailLevel _lod;

        public void Apply(Renderer renderer, ViewItem item, DetailLevel lod, bool reset = false)
        {
            var flags = renderer.SelectionModelDrawFlags;
            if (item != _item || lod != _lod || reset)
            {
                _item = item;
                _lod = lod;
                flags.Clear();
                _flagged.Clear();
            }
            foreach (var rendered in renderer.RenderedDrawables)
            {
                if (rendered.Drawable == null || !_flagged.Add(rendered.Drawable))
                    continue;
                foreach (var model in ModelLods.Hidden(rendered.Drawable, lod))
                    flags[model] = false;
            }
        }
    }

    internal static class DXManagers
    {
        private static readonly System.Reflection.PropertyInfo ClearColour = typeof(DXManager).GetProperty(nameof(DXManager.clearcolour));

        /// <summary>Sets the colour render targets are cleared to (the setter is private upstream).</summary>
        public static void SetClearColour(this DXManager dx, System.Drawing.Color c) =>
            ClearColour.SetValue(dx, new Color(c.R, c.G, c.B, c.A));
    }
}
