using System;
using SharpDX;

namespace GtaAtlas.Render
{
    /// <summary>Camera placement that fits a bounding box in the view (thumbnails).</summary>
    public static class Framing
    {
        /// <summary>
        /// Orbit target and distance so the box's projection fills <paramref name="fill"/> of the tighter screen axis,
        /// centred. <paramref name="view"/> is the camera's rotation-only view matrix (right-handed, looking down -Z, as
        /// CodeWalker's <c>Camera.ViewMatrix</c>); the camera sits <c>distance</c> behind the target along the view direction.
        /// </summary>
        public static (Vector3 Target, float Distance) Fit(Vector3 bbMin, Vector3 bbMax, Matrix view, float fovY, float aspect, float fill = 0.94f)
        {
            var tanY = (float)Math.Tan(fovY * 0.5f) * fill;
            var tanX = tanY * aspect;
            var corners = new Vector3[8];
            for (var i = 0; i < 8; i++)
                corners[i] = new Vector3((i & 1) == 0 ? bbMin.X : bbMax.X, (i & 2) == 0 ? bbMin.Y : bbMax.Y, (i & 4) == 0 ? bbMin.Z : bbMax.Z);

            var target = (bbMin + bbMax) * 0.5f;
            var viewInv = Matrix.Transpose(view); // rotation only
            var distance = 0f;
            for (var pass = 0; pass < 4; pass++)
            {
                // Corner c in view space is v + (0, 0, -d); its depth is d - v.Z. Fits when |v.X| <= depth * tanX (same for Y).
                distance = 0f;
                foreach (var c in corners)
                {
                    var v = Vector3.TransformNormal(c - target, view);
                    distance = Math.Max(distance, Math.Max(Math.Abs(v.X) / tanX, Math.Abs(v.Y) / tanY) + v.Z);
                }
                distance = Math.Max(distance, 0.05f);
                if (pass == 3)
                    break;

                // Perspective makes the near side look bigger: shift the target so the projected extents are centred.
                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                foreach (var c in corners)
                {
                    var v = Vector3.TransformNormal(c - target, view);
                    var depth = distance - v.Z;
                    minX = Math.Min(minX, v.X / depth);
                    maxX = Math.Max(maxX, v.X / depth);
                    minY = Math.Min(minY, v.Y / depth);
                    maxY = Math.Max(maxY, v.Y / depth);
                }
                var shift = new Vector3((minX + maxX) * 0.5f * distance, (minY + maxY) * 0.5f * distance, 0);
                target += Vector3.TransformNormal(shift, viewInv);
            }
            return (target, distance);
        }
    }
}
