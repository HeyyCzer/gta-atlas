using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodeWalker;
using CodeWalker.GameFiles;
using CodeWalker.Rendering;
using CodeWalker.World;
using SharpDX;
using SharpDX.Direct3D11;

namespace GtaAtlas.Render
{
    /// <summary>
    /// CodeWalker renderer hosted as a child control (<c>TopLevel = false</c>). Shows one model at a time:
    /// left drag orbits, right/middle drag pans, wheel zooms, double-click or R resets the camera.
    /// Owns its D3D device; <see cref="Close"/> (or disposing the parent) releases it. GPU copies of models that are no
    /// longer drawn are evicted by the upstream renderable cache (1 s unused), pumped by the content thread.
    /// </summary>
    public sealed class ModelViewport : Form, DXForm
    {
        private readonly GameAssets _assets;
        private readonly Renderer _renderer;
        private readonly Camera _camera;
        private readonly Entity _target = new();
        private readonly Stopwatch _frameTimer = new();
        private readonly bool _initOk;

        private volatile bool _sceneOpen;
        private volatile ViewItem _item;
        private volatile bool _showBounds;
        private volatile DetailLevel _lod;
        private bool _wireframe;
        private readonly LodFilter _lodFilter = new(); // render thread only
        private int _request; // bumps on every Show*, so stale async loads are dropped
        private System.Drawing.Point _mouse;
        private MouseButtons _drag;

        public ModelViewport(GameAssets assets, System.Drawing.Size size)
        {
            _assets = assets;
            TopLevel = false;
            ClientSize = size; // the swap chain is created at this size in Renderer.Init
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            BackColor = System.Drawing.Color.Black;

            _renderer = new Renderer(this, assets.Cache);
            _camera = _renderer.camera;
            _initOk = _renderer.Init();

            _renderer.controllightdir = true;
            _renderer.renderskydome = false;
            _renderer.rendercollisionmeshes = false;
            _renderer.renderclouds = false;
            _renderer.rendermoon = false;
            _renderer.renderskeletons = false;
            _renderer.SelectionFlagsTestAll = true; // draw AllModels filtered by SelectionModelDrawFlags, which picks the LOD
            _renderer.RenderedDrawablesListEnable = true; // to find the drawables (wheels, glass...) whose LOD needs flags
        }

        public Form Form => this;

        /// <summary>Raised (on the UI thread) when the shown model is ready or failed to load; argument is an error or null.</summary>
        public event EventHandler<string> ItemLoaded;

        public bool HasItem => _item != null;

        /// <summary>Levels the shown model has (its main drawable's); High only when nothing is shown.</summary>
        public IReadOnlyList<DetailLevel> AvailableLods => _item?.Lods ?? [DetailLevel.High];

        /// <summary>Requested level of detail; drawables lacking it show the nearest level (<see cref="ModelLods.Effective"/>).</summary>
        public DetailLevel Lod
        {
            get => _lod;
            set => _lod = value;
        }

        public bool Wireframe
        {
            get => _wireframe;
            set
            {
                _wireframe = value;
                if (_renderer.shaders != null) // created with the device
                    _renderer.shaders.wireframe = value;
            }
        }

        /// <summary>Draws the model's bounding box.</summary>
        public bool ShowBounds
        {
            get => _showBounds;
            set => _showBounds = value;
        }

        /// <summary>Draws bones of models that have a skeleton (vehicles, some props).</summary>
        public bool ShowSkeleton
        {
            get => _renderer.renderskeletons;
            set => _renderer.renderskeletons = value;
        }

        public void ShowProp(uint hash)
        {
            var request = ++_request;
            LoadItem(request, () => ViewItem.Prop(_assets, hash));
        }

        public void ShowVehicle(string name)
        {
            var request = ++_request;
            LoadItem(request, () => ViewItem.Vehicle(_assets, name));
        }

        public void Clear()
        {
            ++_request;
            _item = null;
        }

        private void LoadItem(int request, Func<ViewItem> load)
        {
            _item = null;
            Task.Run(async () =>
            {
                await _assets.Ready.ConfigureAwait(false);
                return load();
            }).ContinueWith(t =>
            {
                if (request != _request || IsDisposed)
                    return;
                if (t.IsFaulted)
                {
                    ItemLoaded?.Invoke(this, t.Exception?.GetBaseException().Message);
                    return;
                }
                _item = t.Result;
                ResetCamera();
                ItemLoaded?.Invoke(this, null);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>Frames the current model's bounding sphere from the default angle.</summary>
        public void ResetCamera()
        {
            var item = _item;
            if (item == null || !_sceneOpen)
                return;
            var radius = Math.Max(0.01f, item.Radius);
            _target.Position = item.Center;
            _camera.TargetRotation = _camera.CurrentRotation = new Vector3(0.75f * MathUtil.Pi, 0.25f, 0);
            _camera.TargetDistance = _camera.CurrentDistance = radius * 2.2f;
            _camera.ZNear = Math.Max(0.01f, radius * 0.005f);
            _camera.UpdateProj = true;
        }

        /// <summary>Colour behind the model.</summary>
        public System.Drawing.Color Background
        {
            get => BackColor;
            set
            {
                BackColor = value;
                ApplyBackground();
            }
        }

        private void ApplyBackground() => _renderer.DXMan.SetClearColour(System.Drawing.Color.FromArgb(255, BackColor));

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (_initOk)
                _renderer.Start();
        }

        public void InitScene(Device device)
        {
            _renderer.DeviceCreated(device, ClientSize.Width, ClientSize.Height);
            _renderer.shaders.deferred = false;
            _renderer.shaders.wireframe = _wireframe;
            _renderer.shaders.hdr = false; // the HDR target is cleared to a hardcoded blue; without it DXManager.clearcolour is used
            ApplyBackground();

            _camera.FollowEntity = _target;
            _target.Orientation = Quaternion.LookAtLH(Vector3.Zero, Vector3.Up, Vector3.ForwardLH);

            _sceneOpen = true;
            new Thread(ContentLoop) { IsBackground = true, Name = "ModelViewport content" }.Start();
            _frameTimer.Start();
            BeginInvoke(ResetCamera);
        }

        /// <summary>Uploads loaded meshes/textures to the GPU (the renderable cache needs its own pump).</summary>
        private void ContentLoop()
        {
            while (_sceneOpen)
            {
                if (!_renderer.ContentThreadProc())
                    Thread.Sleep(1);
            }
        }

        public void CleanupScene()
        {
            _sceneOpen = false;
            _renderer.DeviceDestroyed();
        }

        public void BuffersResized(int w, int h)
        {
            if (_sceneOpen) // DXManager resizes on any size change, even before the scene exists
                _renderer.BuffersResized(w, h);
        }

        public bool ConfirmQuit() => true;

        public void RenderScene(DeviceContext context)
        {
            var elapsed = (float)_frameTimer.Elapsed.TotalSeconds;
            _frameTimer.Restart();

            if (!Monitor.TryEnter(_renderer.RenderSyncRoot, 50))
                return;
            try
            {
                var item = _item;
                var lod = _lod;
                _renderer.Update(elapsed, _mouse.X, _mouse.Y);
                _renderer.BeginRender(context);
                _renderer.RenderSkyAndClouds();
                item?.Render(_renderer, lod);
                _lodFilter.Apply(_renderer, item, lod);
                _renderer.RenderQueued();
                if (_showBounds && item != null)
                {
                    _renderer.SelectionBoxes.Add(new MapBox
                    {
                        CamRelPos = -_camera.Position, // the model sits at the origin
                        BBMin = item.BBMin,
                        BBMax = item.BBMax,
                        Orientation = Quaternion.Identity,
                        Scale = Vector3.One,
                    });
                    _renderer.RenderSelectionGeometry(MapSelectionMode.Entity);
                }
                _renderer.RenderFinalPass();
                _renderer.EndRender();
            }
            finally
            {
                Monitor.Exit(_renderer.RenderSyncRoot);
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            Focus(); // the wheel goes to the focused window
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            _drag |= e.Button;
            _mouse = e.Location;
            Focus();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag &= ~e.Button;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var dx = e.X - _mouse.X;
            var dy = e.Y - _mouse.Y;
            _mouse = e.Location;

            if ((_drag & MouseButtons.Left) != 0)
                _camera.MouseRotate(dx, dy);
            else if ((_drag & (MouseButtons.Right | MouseButtons.Middle)) != 0)
            {
                // Pan the orbit target in screen space; speed scales with distance so it tracks the cursor.
                var scale = _camera.CurrentDistance * 2f * (float)Math.Tan(_camera.FieldOfView * 0.5f) / Math.Max(1, ClientSize.Height);
                var move = _camera.ViewInvQuaternion.Multiply(new Vector3(-dx * scale, dy * scale, 0));
                _target.Position += move;
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta != 0)
                _camera.MouseZoom(e.Delta);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left)
                ResetCamera();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.R && e.Modifiers == Keys.None)
                ResetCamera();
        }
    }
}
