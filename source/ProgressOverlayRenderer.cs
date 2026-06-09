using System;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace LockInteract
{
    public class ProgressOverlayRenderer : IRenderer
    {
        // ── Visual constants ──────────────────────────────────────────────────

        private const int   CircleColor    = 0xFFDD88;
        private const float FadeInSpeed    = 0.12f;
        private const float FadeOutSpeed   = 0.20f;
        private const int   CircleMaxSteps = 16;
        private const float OuterRadius    = 24f;
        private const float InnerRadius    = 18f;
        private const int   TextOffsetY    = 10;

        // ── State ─────────────────────────────────────────────────────────────

        private readonly ICoreClientAPI     _api;
        private readonly LockInteractConfig _config;

        private MeshRef?       _mesh;
        private LoadedTexture? _textTexture;

        private float _alpha    = 0f;
        private float _progress = 0f;
        private bool  _visible  = false;

        // ── RenderOrder 0 matches CarryOn — run early in the Ortho stage
        // while the engine's default GUI shader is still active.
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
            _progress = GameMath.Clamp(progress, 0f, 1f);
            _visible  = true;
        }

        public void Hide() => _visible = false;

        // ── IRenderer ─────────────────────────────────────────────────────────

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            // Mirror CarryOn exactly: use whatever shader the engine has active.
            var rend   = _api.Render;
            var shader = rend.CurrentActiveShader;

            _alpha = Math.Clamp(
                _alpha + dt / (_visible ? FadeInSpeed : -FadeOutSpeed),
                0f, 1f);

            if (_progress <= 0f || _alpha <= 0f) return;

            UpdateCircleMesh(_progress);

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

            // ── Progress ring (mirrors CarryOn's HudOverlayRenderer exactly) ──
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

                // Reset noTexture so subsequent renderers aren't affected
                shader.Uniform("noTexture", 0f);
            }

            // ── "Hold to open..." label below the crosshair ───────────────────
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
