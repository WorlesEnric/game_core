// Hollowmere - RegionAtmosphere (P3.1): one per region scene, authored through hollowmere.setAtmosphere.
//
// Holds the look of a region: exponential-squared fog, trilight ambient, the sun's colour/intensity/rotation and the
// colour grading the global post-processing volume uses while the region is the focus region. The game applies it on
// RegionEntered (Apply); in a headless run there is no volume and only RenderSettings and the light change.
#nullable enable
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Hollowmere.Game
{
    /// <summary>The atmosphere (fog, ambient, sun, grading) of one region scene.</summary>
    [DisallowMultipleComponent]
    [Authorable("hollowmere.atmosphere", DisplayName = "Region Atmosphere", Scope = AuthorScope.Instance, RuntimeApplicability = RuntimeApply.Live,
        Doc = "Fog, ambient light, sun and colour grading of a region; applied when the player enters the region.")]
    public sealed class RegionAtmosphere : MonoBehaviour
    {
        [AuthorField(Doc = "Fog colour.")]
        [SerializeField] private Color fogColor = new Color(0.55f, 0.6f, 0.62f, 1f);

        [AuthorField(Min = 0, Max = 1, Doc = "Exponential-squared fog density.")]
        [SerializeField] private float fogDensity = 0.012f;

        [AuthorField(Doc = "Ambient sky colour (trilight).")]
        [SerializeField] private Color ambientSky = new Color(0.55f, 0.6f, 0.7f, 1f);

        [AuthorField(Doc = "Ambient equator colour (trilight).")]
        [SerializeField] private Color ambientEquator = new Color(0.4f, 0.42f, 0.42f, 1f);

        [AuthorField(Doc = "Ambient ground colour (trilight).")]
        [SerializeField] private Color ambientGround = new Color(0.2f, 0.2f, 0.18f, 1f);

        [AuthorField(Doc = "Sun colour.")]
        [SerializeField] private Color sunColor = new Color(1f, 0.95f, 0.85f, 1f);

        [AuthorField(Min = 0, Max = 8, Doc = "Sun intensity.")]
        [SerializeField] private float sunIntensity = 1.1f;

        [AuthorField(Unit = "deg", Doc = "Sun rotation (Euler angles).")]
        [SerializeField] private Vector3 sunEuler = new Vector3(50f, -30f, 0f);

        [AuthorField(Min = -5, Max = 5, Doc = "Post exposure (EV).")]
        [SerializeField] private float postExposure;

        [AuthorField(Min = -100, Max = 100, Doc = "Saturation offset.")]
        [SerializeField] private float saturation;

        [AuthorField(Min = -100, Max = 100, Doc = "Contrast offset.")]
        [SerializeField] private float contrast;

        [AuthorField(Min = 0, Max = 10, Doc = "Bloom intensity.")]
        [SerializeField] private float bloomIntensity = 0.3f;

        [AuthorField(Min = 0, Max = 1, Doc = "Vignette intensity.")]
        [SerializeField] private float vignetteIntensity = 0.25f;

        [AuthorField(Doc = "Colour filter (multiplied into the image).")]
        [SerializeField] private Color colorFilter = Color.white;

        public Color FogColor => fogColor;

        public float FogDensity => fogDensity;

        public Color AmbientSky => ambientSky;

        public Color AmbientEquator => ambientEquator;

        public Color AmbientGround => ambientGround;

        public Color SunColor => sunColor;

        public float SunIntensity => sunIntensity;

        public Vector3 SunEuler => sunEuler;

        public float PostExposure => postExposure;

        public float Saturation => saturation;

        public float Contrast => contrast;

        public float BloomIntensity => bloomIntensity;

        public float VignetteIntensity => vignetteIntensity;

        public Color ColorFilter => colorFilter;

        /// <summary>Sets every field (the authoring tool's entry point).</summary>
        public void Configure(
            Color fog,
            float density,
            Color sky,
            Color equator,
            Color ground,
            Color sun,
            float intensity,
            Vector3 euler,
            float exposure,
            float saturationOffset,
            float contrastOffset,
            float bloom,
            float vignette,
            Color filter)
        {
            fogColor = fog;
            fogDensity = Mathf.Max(0f, density);
            ambientSky = sky;
            ambientEquator = equator;
            ambientGround = ground;
            sunColor = sun;
            sunIntensity = Mathf.Max(0f, intensity);
            sunEuler = euler;
            postExposure = exposure;
            saturation = saturationOffset;
            contrast = contrastOffset;
            bloomIntensity = Mathf.Max(0f, bloom);
            vignetteIntensity = Mathf.Clamp01(vignette);
            colorFilter = filter;
        }

        /// <summary>
        /// Applies the atmosphere: RenderSettings fog and ambient, the sun light (when given) and the grading overrides of
        /// the volume's runtime profile (when given; created in the profile instance when missing).
        /// </summary>
        public void Apply(Light? sun, Volume? volume)
        {
            RenderSettings.fog = fogDensity > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            if (sun != null)
            {
                sun.color = sunColor;
                sun.intensity = sunIntensity;
                sun.transform.rotation = Quaternion.Euler(sunEuler);
            }

            if (volume == null)
            {
                return;
            }

            VolumeProfile profile = volume.profile;
            if (profile == null)
            {
                return;
            }

            ColorAdjustments adjustments = Override<ColorAdjustments>(profile);
            adjustments.postExposure.Override(postExposure);
            adjustments.saturation.Override(saturation);
            adjustments.contrast.Override(contrast);
            adjustments.colorFilter.Override(colorFilter);
            Bloom bloom = Override<Bloom>(profile);
            bloom.intensity.Override(bloomIntensity);
            Vignette vignette = Override<Vignette>(profile);
            vignette.intensity.Override(vignetteIntensity);
        }

        /// <summary>The atmosphere of a loaded scene (first one found under its roots), or null.</summary>
        public static RegionAtmosphere? Find(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                RegionAtmosphere? found = roots[i].GetComponentInChildren<RegionAtmosphere>(true);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static T Override<T>(VolumeProfile profile)
            where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
            }

            component.active = true;
            return component;
        }
    }
}
