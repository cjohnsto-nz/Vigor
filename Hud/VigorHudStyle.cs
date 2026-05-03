using System;
using Vigor.Config;
using Vintagestory.API.MathTools;

namespace Vigor.Hud
{
    internal static class VigorHudStyle
    {
        public const string DefaultHorizontalStatusBarColorHex = "#D8A500";
        public const string DefaultRadialStatusBarColorHex = "#D8A500";
        public const float DefaultHorizontalStatusBarOpacity = 0.5f;
        public const float DefaultRadialStatusBarOpacity = 0.7f;

        public static double[] ResolveHorizontalBarColor(VigorConfig config)
        {
            var color = ResolveColor(config?.HorizontalStatusBarColorHex, DefaultHorizontalStatusBarColorHex);
            color[3] = ClampOpacity(config?.HorizontalStatusBarOpacity ?? DefaultHorizontalStatusBarOpacity);
            return color;
        }

        public static Vec4f ResolveRadialBarColor(VigorConfig config)
        {
            var color = ResolveColor(config?.RadialStatusBarColorHex, DefaultRadialStatusBarColorHex);
            color[3] = ClampOpacity(config?.RadialStatusBarOpacity ?? DefaultRadialStatusBarOpacity);
            return new Vec4f((float)color[0], (float)color[1], (float)color[2], (float)color[3]);
        }

        private static double[] ResolveColor(string hex, string fallbackHex)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(hex))
                {
                    return ColorUtil.Hex2Doubles(hex.Trim());
                }
            }
            catch
            {
                // Fall back to the baked-in default when the config contains an invalid color string.
            }

            return ColorUtil.Hex2Doubles(fallbackHex);
        }

        private static double ClampOpacity(float opacity)
        {
            return Math.Clamp(opacity, 0f, 1f);
        }
    }
}
