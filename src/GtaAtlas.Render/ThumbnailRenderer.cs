using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using CodeWalker.Rendering;
using CodeWalker.World;
using SharpDX;
using SharpDX.Direct3D11;
using Format = SharpDX.DXGI.Format;
using SampleDescription = SharpDX.DXGI.SampleDescription;
using Device = SharpDX.Direct3D11.Device;

namespace GtaAtlas.Render
{
    /// <summary>
    /// Offscreen model renderer for thumbnails: a hidden window owns the D3D device (DXManager needs one for its swap
    /// chain, which is never presented); frames are drawn on demand and read back from the backbuffer. Waits until
    /// geometry and textures are on the GPU, frames the bounding box, and returns the model on a transparent background
    /// (<see cref="ThumbnailMatte"/>).
    /// The window and device live on a private thread with its own message loop (a window must be pumped and destroyed by
    /// the thread that made it), so every method may be called from any thread; renders run one at a time.
    /// </summary>
    public sealed class ThumbnailRenderer : IDisposable
    {
        public const int DefaultWidth = 384;
        public const int DefaultHeight = 256;

        private const float ViewPitch = 0.3f;

        private const float FieldOfView = 0.6f; // narrower than the viewport's: less perspective distortion
        private const int SettledFrames = 4;
        private const int TimeoutMs = 8000;

        private readonly GameAssets _assets;
        private readonly Thread _thread;
        private readonly Entity _target = new();
        private readonly LodFilter _lods = new();
        private Host _host;
        private Renderer _renderer;
        private Camera _camera;
        private Texture2D _resolve;
        private Texture2D _staging;
        private volatile bool _disposed;
        private bool _sceneClosed;

        public ThumbnailRenderer(GameAssets assets, int width = DefaultWidth, int height = DefaultHeight)
        {
            _assets = assets;
            Width = width;
            Height = height;
            Exception error = null;
            using var started = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                try
                {
                    Create();
                }
                catch (Exception e)
                {
                    error = e;
                    return;
                }
                finally
                {
                    started.Set();
                }
                Application.Run(); // until Dispose calls ExitThread
            })
            { IsBackground = true, Name = "ThumbnailRenderer" };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            started.Wait();
            if (error != null)
                throw new InvalidOperationException("Could not create the thumbnail renderer: " + error.Message, error);
        }

        private void Create()
        {
            _host = new Host(this) { ClientSize = new System.Drawing.Size(Width, Height) };
            _renderer = new Renderer(_host, _assets.Cache);
            _camera = _renderer.camera;
            if (!_renderer.Init()) // creates the window handle, device and swap chain
            {
                _host.Dispose();
                throw new InvalidOperationException("Could not create the graphics device.");
            }

            _renderer.controllightdir = true;
            _renderer.renderskydome = false;
            _renderer.rendercollisionmeshes = false;
            _renderer.renderclouds = false;
            _renderer.rendermoon = false;
            _renderer.renderskeletons = false;
            _renderer.SelectionFlagsTestAll = true; // LOD picked by LodFilter, as in ModelViewport
            _renderer.RenderedDrawablesListEnable = true; // LodFilter and the loaded check need them
            _renderer.RenderableCache.MaxItemsPerLoop = 64; // upload everything between frames
            InitScene(_renderer.DXMan.device);
        }

        public int Width { get; }
        public int Height { get; }

        public System.Drawing.Bitmap RenderProp(uint hash) => Render(() => ViewItem.Prop(_assets, hash));

        public System.Drawing.Bitmap RenderVehicle(string name) => Render(() => ViewItem.Vehicle(_assets, name));

        /// <summary>
        /// Loads (on the calling thread) and draws the model, blocking until it's fully streamed in (or a timeout).
        /// Throws <see cref="ModelNotFoundException"/> when the game has no model or nothing ever draws.
        /// </summary>
        private System.Drawing.Bitmap Render(Func<ViewItem> load)
        {
            _assets.Ready.Wait();
            var item = load();
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                return (System.Drawing.Bitmap)_host.Invoke(() => Draw(item));
            }
            catch (InvalidOperationException) when (_disposed) // the window went away while waiting
            {
                throw new ObjectDisposedException(nameof(ThumbnailRenderer));
            }
        }

        private System.Drawing.Bitmap Draw(ViewItem item)
        {
            ObjectDisposedException.ThrowIf(_sceneClosed, this);
            var context = _renderer.DXMan.context;
            Frame(item);

            var clock = Stopwatch.StartNew();
            var settled = 0;
            var lastCount = -1;
            while (settled < SettledFrames)
            {
                PumpContent();
                DrawFrame(context, item, System.Drawing.Color.Black);
                var count = _renderer.RenderedDrawables.Count;
                settled = count > 0 && count == lastCount && FullyLoaded() ? settled + 1 : 0;
                lastCount = count;
                if (clock.ElapsedMilliseconds > TimeoutMs)
                {
                    if (count == 0)
                        throw new ModelNotFoundException("The model did not render.");
                    break; // some texture never arrived: keep what there is
                }
                if (settled == 0)
                    Thread.Sleep(2); // the GameAssets pump thread is reading files
            }

            var onBlack = Capture(context);
            DrawFrame(context, item, System.Drawing.Color.White);
            var onWhite = Capture(context);
            var bgra = ThumbnailMatte.Combine(onBlack, onWhite);
            if (ThumbnailMatte.IsEmpty(bgra))
                throw new ModelNotFoundException("The model rendered empty.");
            return ThumbnailMatte.ToBitmap(bgra, Width, Height);
        }

        private void Frame(ViewItem item)
        {
            _camera.FieldOfView = FieldOfView;
            _camera.TargetRotation = _camera.CurrentRotation = new Vector3(item.FrontYaw, ViewPitch, 0);
            _camera.UpdateProj = true;
            _camera.Update(0); // computes ViewMatrix for the angle
            AimLight(-_camera.ViewDirection);
            var (target, distance) = Framing.Fit(item.BBMin, item.BBMax, _camera.ViewMatrix, FieldOfView, (float)Width / Height);
            _target.Position = target;
            _camera.TargetDistance = _camera.CurrentDistance = distance;
            _camera.ZNear = Math.Max(0.01f, item.Radius * 0.005f);
            _camera.UpdateProj = true;
        }

        /// <summary>
        /// Key light from over the camera's left shoulder, so the side in view is lit whatever way the model faces.
        /// <c>lightdirx/y</c> are the renderer's yaw/pitch of the direction towards the light (world is Z-up).
        /// </summary>
        private void AimLight(Vector3 towardsCamera)
        {
            var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, towardsCamera));
            var light = Vector3.Normalize(towardsCamera + side * 0.5f + Vector3.UnitZ * 0.6f);
            _renderer.lightdiry = (float)Math.Asin(light.Z);
            _renderer.lightdirx = (float)Math.Atan2(light.X, -light.Y);
        }

        /// <summary>Uploads queued meshes and textures (the job of the viewport's content thread).</summary>
        private void PumpContent()
        {
            for (var i = 0; i < 100 && _renderer.ContentThreadProc(); i++)
            {
            }
        }

        /// <summary>Every drawable drawn last frame has its geometry and textures on the GPU, and nothing waits for upload.</summary>
        private bool FullyLoaded()
        {
            var cache = _renderer.RenderableCache;
            if (cache.TotalQueueLength > 0)
                return false;
            foreach (var rendered in _renderer.RenderedDrawables)
            {
                var renderable = cache.GetRenderable(rendered.Drawable);
                if (renderable is not { IsLoaded: true, AllTexturesLoaded: true })
                    return false;
            }
            return true;
        }

        private void DrawFrame(DeviceContext context, ViewItem item, System.Drawing.Color background)
        {
            _renderer.DXMan.SetClearColour(background);
            lock (_renderer.RenderSyncRoot)
            {
                _renderer.DXMan.SetDefaultRenderTarget(context);
                _renderer.Update(1 / 60f, 0, 0);
                _renderer.BeginRender(context);
                _renderer.RenderSkyAndClouds();
                item.Render(_renderer, DetailLevel.High);
                _lods.Apply(_renderer, item, DetailLevel.High);
                _renderer.RenderQueued();
                _renderer.RenderFinalPass();
                _renderer.EndRender();
            }
        }

        /// <summary>Backbuffer as RGBA bytes, top row first (resolving MSAA first).</summary>
        private byte[] Capture(DeviceContext context)
        {
            var backbuffer = _renderer.DXMan.backbuffer;
            Texture2D source = backbuffer;
            if (backbuffer.Description.SampleDescription.Count > 1)
            {
                context.ResolveSubresource(backbuffer, 0, _resolve, 0, Format.R8G8B8A8_UNorm);
                source = _resolve;
            }
            context.CopyResource(source, _staging);
            var box = context.MapSubresource(_staging, 0, MapMode.Read, MapFlags.None);
            try
            {
                var row = Width * 4;
                var pixels = new byte[row * Height];
                for (var y = 0; y < Height; y++)
                    Marshal.Copy(box.DataPointer + y * box.RowPitch, pixels, y * row, row);
                return pixels;
            }
            finally
            {
                context.UnmapSubresource(_staging, 0);
            }
        }

        private void InitScene(Device device)
        {
            _renderer.DeviceCreated(device, Width, Height);
            _renderer.shaders.deferred = false;
            _renderer.shaders.hdr = false; // HDR clears to a fixed blue; the matte needs the clear colour
            _camera.FollowEntity = _target;
            _target.Orientation = Quaternion.LookAtLH(Vector3.Zero, Vector3.Up, Vector3.ForwardLH);

            var desc = new Texture2DDescription
            {
                Width = Width,
                Height = Height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.R8G8B8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Default,
                BindFlags = BindFlags.ShaderResource,
            };
            _resolve = new Texture2D(device, desc);
            desc.Usage = ResourceUsage.Staging;
            desc.BindFlags = BindFlags.None;
            desc.CpuAccessFlags = CpuAccessFlags.Read;
            _staging = new Texture2D(device, desc);
        }

        private void CleanupScene()
        {
            if (_sceneClosed)
                return;
            _sceneClosed = true;
            _resolve?.Dispose();
            _staging?.Dispose();
            _renderer.DeviceDestroyed();
        }

        /// <summary>Releases the device and ends the thread, after a render in progress.</summary>
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _host.Invoke(() =>
            {
                _host.Close(); // DXManager releases the device on FormClosing (calls CleanupScene)
                CleanupScene(); // in case closing a never-shown window raised nothing
                _host.Dispose();
                Application.ExitThread();
            });
            _thread.Join(TimeSpan.FromSeconds(5));
        }

        /// <summary>Never-shown window the swap chain is bound to.</summary>
        private sealed class Host : Form, DXForm
        {
            private readonly ThumbnailRenderer _owner;

            public Host(ThumbnailRenderer owner)
            {
                _owner = owner;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
            }

            public Form Form => this;
            public void InitScene(Device device) { } // called by the owner once its fields are set
            public void CleanupScene() => _owner.CleanupScene();
            public void RenderScene(DeviceContext context) { }
            public void BuffersResized(int w, int h) { }
            public bool ConfirmQuit() => true;
        }
    }
}
