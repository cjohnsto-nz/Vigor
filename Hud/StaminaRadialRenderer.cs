using System;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Vigor.Hud
{
    public struct StaminaSnapshot
    {
        public float Stamina;
        public float MaxStamina;
        public bool IsExhausted;
        public float RecoveryThreshold;
    }

    internal class StaminaRadialRenderer : IRenderer, IDisposable
    {
        private readonly ICoreClientAPI _capi;
        private readonly Func<StaminaSnapshot> _snapshotProvider;

        private MeshRef _backgroundRingMesh;
        private MeshRef[] _staminaMeshes;
        private MeshRef[] _thresholdMeshes;

        private bool _disposed;
        private float _exhaustedFlashTimer;
        private float _smoothedPercent = -1f; // negative indicates uninitialized

        private readonly Matrixf _mvMatrix = new Matrixf();

        private const int Steps = 360; // higher resolution for smoother fill animation

        public double RenderOrder { get; set; } = 0;
        public int RenderRange { get; set; } = 1000;

        public StaminaRadialRenderer(ICoreClientAPI capi, Func<StaminaSnapshot> snapshotProvider)
        {
            _capi = capi;
            _snapshotProvider = snapshotProvider;

            var cfg = VigorModSystem.Instance.CurrentConfig;
            float inner = GameMath.Clamp(cfg.RadialInnerRadius, 0.0f, 1.0f);
            float outer = GameMath.Clamp(cfg.RadialOuterRadius, inner + 0.01f, 2.0f);

            // Upload full ring background
            // Full ring; start at 12 o'clock for consistency
            MeshData bgRing = CreateRing(inner, outer, Steps, -Math.PI / 2, 2 * Math.PI, ColorUtil.BlackArgb);
            _backgroundRingMesh = _capi.Render.UploadMesh(bgRing);

            // Upload stamina meshes for 0..100%
            _staminaMeshes = new MeshRef[Steps + 1];
            for (int i = 0; i <= Steps; i++)
            {
                float percent = i / (float)Steps;
                // Clockwise fill from 12 o'clock: negative angle range
                MeshData mesh = CreateRing(inner, outer, Steps, -Math.PI / 2, -2 * Math.PI * percent, ColorUtil.BlackArgb);
                _staminaMeshes[i] = _capi.Render.UploadMesh(mesh);
            }

            // Precompute thin threshold indicator arcs for each percent position
            _thresholdMeshes = new MeshRef[Steps + 1];
            float thOuter = outer;
            float thInner = (inner + outer) * 0.5f; // mid-line thickness
            double tickRange = 2 * Math.PI * 0.01f; // ~1% arc
            for (int i = 0; i <= Steps; i++)
            {
                // Clockwise position from 12 o'clock
                double start = -Math.PI / 2 - 2 * Math.PI * (i / (double)Steps);
                MeshData arc = CreateArc(thInner, thOuter, Steps, start, tickRange, ColorUtil.BlackArgb);
                _thresholdMeshes[i] = _capi.Render.UploadMesh(arc);
            }
        }

        public void OnRenderFrame(float dt, EnumRenderStage stage)
        {
            var cfg = VigorModSystem.Instance.CurrentConfig;
            var snap = _snapshotProvider?.Invoke() ?? default;
            if (snap.MaxStamina <= 0) return;

            float stamina = GameMath.Clamp(snap.Stamina, 0f, snap.MaxStamina);
            float max = snap.MaxStamina;
            bool exhausted = snap.IsExhausted;
            float threshold = GameMath.Clamp(snap.RecoveryThreshold, 0f, max);

            // Hide when visually full with tolerance, to account for prediction rounding
            if (cfg.HideStaminaOnFull && (stamina / max) >= 0.999f && !exhausted)
            {
                return;
            }

            if (exhausted) _exhaustedFlashTimer += dt; else _exhaustedFlashTimer = 0f;

            IShaderProgram shader = _capi.Render.CurrentActiveShader;

            float scale = 35f * GameMath.Max(0.1f, cfg.RadialScale);

            _mvMatrix
                .Set(_capi.Render.CurrentModelviewMatrix)
                .Translate(_capi.Render.FrameWidth / 2f, _capi.Render.FrameHeight / 2f, 0f)
                .Scale(scale, scale, 1f);

            shader.UniformMatrix("projectionMatrix", _capi.Render.CurrentProjectionMatrix);
            shader.UniformMatrix("modelViewMatrix", _mvMatrix.Values);
            shader.Uniform("tex2d", 0);
            shader.Uniform("noTexture", 1f);

            // Background ring (flashes red when exhausted)
            if (exhausted)
            {
                float flashAlpha = 0.3f + 0.2f * GameMath.Sin(_exhaustedFlashTimer * 5f);
                shader.Uniform("rgbaIn", new Vec4f(1f, 0f, 0f, flashAlpha));
            }
            else
            {
                shader.Uniform("rgbaIn", new Vec4f(1f, 1f, 1f, 0.2f));
            }
            _capi.Render.RenderMesh(_backgroundRingMesh);

            // Stamina ring (yellow - match statbar coloration)
            float staminaPercentRaw = GameMath.Clamp(stamina / max, 0f, 1f);
            // Exponential smoothing to reduce visible stutter from reconciliation
            if (_smoothedPercent < 0f)
            {
                _smoothedPercent = staminaPercentRaw;
            }
            else
            {
                // smoothing factor ~12 Hz response
                float alpha = 1f - (float)Math.Exp(-dt * 12f);
                _smoothedPercent = GameMath.Clamp(_smoothedPercent + (staminaPercentRaw - _smoothedPercent) * alpha, 0f, 1f);
            }

            int idx = GameMath.Clamp((int)(_smoothedPercent * Steps), 0, Steps);
            shader.Uniform("rgbaIn", new Vec4f(0.85f, 0.65f, 0f, 0.7f));
            _capi.Render.RenderMesh(_staminaMeshes[idx]);

            // Recovery threshold indicator (draw only when exhausted to match bar semantics)
            if (exhausted && threshold > 0)
            {
                float thPercent = GameMath.Clamp(threshold / max, 0f, 1f);
                int thIdx = GameMath.Clamp((int)(thPercent * Steps), 0, Steps);
                shader.Uniform("rgbaIn", new Vec4f(1f, 0.65f, 0f, 0.5f)); // orange accent
                _capi.Render.RenderMesh(_thresholdMeshes[thIdx]);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_backgroundRingMesh != null)
            {
                _backgroundRingMesh.Dispose();
                _capi.Render.DeleteMesh(_backgroundRingMesh);
                _backgroundRingMesh = null;
            }

            if (_thresholdMeshes != null)
            {
                foreach (var m in _thresholdMeshes)
                {
                    m?.Dispose();
                    if (m != null) _capi.Render.DeleteMesh(m);
                }
                _thresholdMeshes = null;
            }

            if (_staminaMeshes != null)
            {
                foreach (var m in _staminaMeshes)
                {
                    m?.Dispose();
                    if (m != null) _capi.Render.DeleteMesh(m);
                }
                _staminaMeshes = null;
            }
        }

        private static MeshData CreateRing(float innerRadius, float outerRadius, int divisions, double startAngle, double angleRange, int color)
        {
            // Heavily inspired by Bloodshed's MeshUtil.GetRing
            int segs = Math.Max(1, divisions * 4);
            MeshData mesh = new MeshData(segs * 2 * 2, segs * 2 * 3, false, true, true, false);
            mesh.SetMode(EnumDrawMode.Triangles);

            for (int i = 0; i <= segs; i++)
            {
                double angle = startAngle + i / (double)segs * angleRange;
                mesh.AddVertex((float)Math.Cos(angle) * innerRadius, (float)Math.Sin(angle) * innerRadius, 0f, 0f, color);
                mesh.AddVertex((float)Math.Cos(angle) * outerRadius, (float)Math.Sin(angle) * outerRadius, 0f, 0f, color);
            }

            for (int j = 0; j < segs; j++)
            {
                int[] idx = mesh.Indices;
                int ic = mesh.IndicesCount;
                mesh.IndicesCount = ic + 6;
                idx[ic + 0] = (short)(j * 2);
                idx[ic + 1] = (short)(j * 2 + 1);
                idx[ic + 2] = (short)((j * 2 + 2) % (segs * 2 + 2));
                idx[ic + 3] = (short)(j * 2 + 1);
                idx[ic + 4] = (short)((j * 2 + 3) % (segs * 2 + 2));
                idx[ic + 5] = (short)((j * 2 + 2) % (segs * 2 + 2));
            }

            return mesh;
        }

        private static MeshData CreateArc(float innerRadius, float outerRadius, int divisions, double startAngle, double angleRange, int color)
        {
            // Same as CreateRing but offset by startAngle
            int segs = Math.Max(1, divisions * 4);
            MeshData mesh = new MeshData(segs * 2 * 2, segs * 2 * 3, false, true, true, false);
            mesh.SetMode(EnumDrawMode.Triangles);

            for (int i = 0; i <= segs; i++)
            {
                double angle = startAngle + i / (double)segs * angleRange;
                mesh.AddVertex((float)Math.Cos(angle) * innerRadius, (float)Math.Sin(angle) * innerRadius, 0f, 0f, color);
                mesh.AddVertex((float)Math.Cos(angle) * outerRadius, (float)Math.Sin(angle) * outerRadius, 0f, 0f, color);
            }

            for (int j = 0; j < segs; j++)
            {
                int[] idx = mesh.Indices;
                int ic = mesh.IndicesCount;
                mesh.IndicesCount = ic + 6;
                idx[ic + 0] = (short)(j * 2);
                idx[ic + 1] = (short)(j * 2 + 1);
                idx[ic + 2] = (short)((j * 2 + 2) % (segs * 2 + 2));
                idx[ic + 3] = (short)(j * 2 + 1);
                idx[ic + 4] = (short)((j * 2 + 3) % (segs * 2 + 2));
                idx[ic + 5] = (short)((j * 2 + 2) % (segs * 2 + 2));
            }

            return mesh;
        }
    }
}
