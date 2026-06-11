using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace LockInteract
{
    /// <summary>
    /// Renders the hold progress overlay in the Ortho stage:
    ///   - A circular progress ring centred on the crosshair.
    ///   - A "Hold to open…" text label positioned below the ring.
    ///
    /// Both elements share the same alpha and fade in/out together.
    ///
    /// RenderOrder 0 ensures we run early in the Ortho stage while the
    /// engine's GUI shader is still the active shader. The engine activates
    /// the GUI shader before firing Ortho renderers and expects it to remain
    /// active throughout; we must not call shader.Stop().
    ///
    /// Crosshair position: when the mouse is grabbed (in-world) we use the
    /// screen centre. When the mouse is free (dialog open) we follow the cursor.
    /// </summary>
    public class ProgressOverlayRenderer : IRenderer
    {
        // ── Visual constants ──────────────────────────────────────────────────

        private const int   CircleColor    = 0xFFDD88; // warm amber
        private const float FadeInSpeed    = 0.12f;    // seconds to full opacity
        private const float FadeOutSpeed   = 0.20f;    // seconds to transparent
        private const int   CircleMaxSteps = 16;       // triangle strip segments
        private const float OuterRadius    = 24f;      // pixels
        private const float InnerRadius    = 18f;      // pixels (ring thickness)
        private const int   TextOffsetY    = 10;       // pixels below ring bottom

        // ── State ─────────────────────────────────────────────────────────────

        private readonly ICoreClientAPI     _api;
        private readonly LockInteractConfig _config;

        private MeshRef?       _mesh;
        private LoadedTexture? _textTexture;

        private float _alpha    = 0f;
        private float _progress = 0f;
        private bool  _visible  = false;

        public double RenderOrder => 0;
        public int    RenderRange => 10;

        public ProgressOverlayRenderer(ICoreClientAPI api, LockInteractConfig config)
        {
            _api    = api;
            _config = config;
            _api.Event.RegisterRenderer(this, EnumRenderStage.Ortho);
            UpdateCircleMesh(1f);
        }

        public void SetProgress(float progress)
        {
            // Reject NaN/Infinity before clamping — GameMath.Clamp passes NaN through.
            if (float.IsNaN(progress) || float.IsInfinity(progress)) progress = 0f;
            _progress = GameMath.Clamp(progress, 0f, 1f);
            _visible  = true;
        }

        public void Hide() => _visible = false;

        // ── IRenderer ─────────────────────────────────────────────────────────

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            var rend   = _api.Render;
            var shader = rend.CurrentActiveShader;

            // Fade alpha in or out each frame. Guard against a bad dt so a single
            // NaN frame can't permanently poison _alpha.
            if (!float.IsNaN(dt) && !float.IsInfinity(dt))
            {
                _alpha = Math.Clamp(
                    _alpha + dt / (_visible ? FadeInSpeed : -FadeOutSpeed),
                    0f, 1f);
            }
            if (float.IsNaN(_alpha)) _alpha = 0f;

            if (_progress <= 0f || _alpha <= 0f) return;

            UpdateCircleMesh(_progress);

            // Crosshair position in screen pixels
            int cx, cy;
            if (_api.Input.MouseGrabbed)
            {
                cx = rend.FrameWidth  / 2;
                cy = rend.FrameHeight / 2;
            }
            else
            {
                cx = _api.Input.MouseX;
                cy = _api.Input.MouseY;
            }

            // ── Progress ring ─────────────────────────────────────────────────
            if (_config.ShowProgressOverlay && shader != null)
            {
                float r = ((CircleColor >> 16) & 0xFF) / 255f;
                float g = ((CircleColor >>  8) & 0xFF) / 255f;
                float b = (CircleColor         & 0xFF) / 255f;

                shader.Uniform("rgbaIn",    new Vec4f(r, g, b, _alpha));
                shader.Uniform("extraGlow",  0);
                shader.Uniform("applyColor", 0);
                shader.Uniform("tex2d",      0);
                shader.Uniform("noTexture",  1f);
                shader.UniformMatrix("projectionMatrix", rend.CurrentProjectionMatrix);

#pragma warning disable CS0618
                rend.GlPushMatrix();
                rend.GlTranslate(cx, cy, 0);
                rend.GlScale(OuterRadius, OuterRadius, 0);
                shader.UniformMatrix("modelViewMatrix", rend.CurrentModelviewMatrix);
                rend.GlPopMatrix();
#pragma warning restore CS0618

                rend.RenderMesh(_mesh);

                // Restore noTexture so subsequent renderers aren't affected
                shader.Uniform("noTexture", 0f);
            }

            // ── "Hold to open…" label ─────────────────────────────────────────
            if (_config.ShowHoldHint)
            {
                EnsureTextTexture();
                if (_textTexture != null && _textTexture.TextureId > 0)
                {
                    int tx = cx - _textTexture.Width / 2;
                    int ty = cy + (int)OuterRadius + TextOffsetY;
                    rend.Render2DTexturePremultipliedAlpha(
                        _textTexture.TextureId,
                        tx, ty,
                        _textTexture.Width, _textTexture.Height,
                        50f,
                        new Vec4f(1f, 1f, 1f, _alpha));
                }
            }
        }

        // ── Text texture ──────────────────────────────────────────────────────

        private void EnsureTextTexture()
        {
            if (_textTexture != null) return;
            _textTexture = _api.Gui.TextTexture.GenTextTexture(
                Lang.Get("lockinteract:hold-to-open"),
                CairoFont.WhiteSmallText());
        }

        // ── Mesh ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Builds a triangle-strip ring mesh covering [0, progress] of a full circle.
        /// The mesh is in unit space; GlScale applies OuterRadius at render time.
        /// </summary>
        private void UpdateCircleMesh(float progress)
        {
            const float ring = InnerRadius / OuterRadius;
            const float step = 1f / CircleMaxSteps;

            int steps = 1 + (int)Math.Ceiling(CircleMaxSteps * progress);
            var data  = new MeshData(steps * 2, steps * 6, false, false, true, false);

            for (int i = 0; i < steps; i++)
            {
                float p = Math.Min(progress, i * step) * MathF.PI * 2f;
                float x = MathF.Sin(p);
                float y = -MathF.Cos(p);

                data.AddVertexSkipTex(x,        y,        0);
                data.AddVertexSkipTex(x * ring, y * ring, 0);

                if (i > 0)
                {
                    data.AddIndices(new[] { (i*2)-2, (i*2)-1, (i*2)+0 });
                    data.AddIndices(new[] { (i*2)+0, (i*2)-1, (i*2)+1 });
                }
            }

            if (_mesh != null) _api.Render.UpdateMesh(_mesh, data);
            else               _mesh = _api.Render.UploadMesh(data);
        }

        // ── IDisposable ───────────────────────────────────────────────────────

        public void Dispose()
        {
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            if (_mesh != null) { _api.Render.DeleteMesh(_mesh); _mesh = null; }
            _textTexture?.Dispose();
        }
    }
}
