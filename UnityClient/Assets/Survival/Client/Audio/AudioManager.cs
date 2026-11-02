using System.Collections.Generic;
using UnityEngine;

namespace Survival.Client
{
    public enum UiSound
    {
        Click,
        Press,
        Confirm,
        Back,
        Tick,
        Error,
        Whoosh,
        Notify,
    }

    public enum SfxSound
    {
        IntroBoom,
        IntroRiser,
        IntroShimmer,
        Footstep,
        FootstepSand,
        Jump,
        Land,
        Splash,
        Hurt,
        Death,
    }

    public enum AmbienceLayer
    {
        Wind = 0,
        Ocean = 1,
        Birds = 2,
        Night = 3,
        Fire = 4,
    }

    /// <summary>
    /// All sounds are synthesized at startup (see <see cref="ProceduralAudio"/>): no copyrighted or
    /// third-party audio in the build. Provides UI one-shots, 3D-less SFX and crossfaded ambience loops.
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        private readonly Dictionary<UiSound, AudioClip> _ui = new Dictionary<UiSound, AudioClip>();
        private readonly Dictionary<SfxSound, AudioClip[]> _sfx = new Dictionary<SfxSound, AudioClip[]>();
        private readonly AudioSource[] _ambience = new AudioSource[5];
        private readonly float[] _ambienceTarget = new float[5];
        private AudioSource _uiSource;
        private AudioSource _sfxSource;
        private bool _ready;

        public bool IsReady => _ready;

        private void Awake()
        {
            _uiSource = gameObject.AddComponent<AudioSource>();
            _uiSource.playOnAwake = false;
            _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            for (int i = 0; i < _ambience.Length; i++)
            {
                var s = gameObject.AddComponent<AudioSource>();
                s.loop = true;
                s.playOnAwake = false;
                s.volume = 0f;
                _ambience[i] = s;
            }
        }

        /// <summary>Synthesizes all clips. Called during the intro loading phase (~100-300 ms).</summary>
        public void Build()
        {
            if (_ready) return;
            _ui[UiSound.Click] = ProceduralAudio.Blip("ui_click", 1250f, 0.045f, 0.35f);
            _ui[UiSound.Press] = ProceduralAudio.Blip("ui_press", 620f, 0.03f, 0.18f);
            _ui[UiSound.Confirm] = ProceduralAudio.Chime("ui_confirm", new[] { 660f, 990f }, 0.22f, 0.32f);
            _ui[UiSound.Back] = ProceduralAudio.Blip("ui_back", 520f, 0.06f, 0.3f);
            _ui[UiSound.Tick] = ProceduralAudio.Blip("ui_tick", 1800f, 0.02f, 0.2f);
            _ui[UiSound.Error] = ProceduralAudio.Chime("ui_error", new[] { 330f, 247f }, 0.25f, 0.3f);
            _ui[UiSound.Whoosh] = ProceduralAudio.Whoosh("ui_whoosh", 0.35f, 0.25f);
            _ui[UiSound.Notify] = ProceduralAudio.Chime("ui_notify", new[] { 880f, 1320f }, 0.3f, 0.25f);

            _sfx[SfxSound.IntroBoom] = new[] { ProceduralAudio.Boom("intro_boom", 2.4f) };
            _sfx[SfxSound.IntroRiser] = new[] { ProceduralAudio.Riser("intro_riser", 2.2f) };
            _sfx[SfxSound.IntroShimmer] = new[] { ProceduralAudio.Shimmer("intro_shimmer", 1.6f) };
            _sfx[SfxSound.Footstep] = new[]
            {
                ProceduralAudio.Footstep("step0", 0, 0.9f), ProceduralAudio.Footstep("step1", 1, 1.0f),
                ProceduralAudio.Footstep("step2", 2, 1.1f), ProceduralAudio.Footstep("step3", 3, 0.95f),
            };
            _sfx[SfxSound.FootstepSand] = new[] { ProceduralAudio.Footstep("sand0", 10, 0.6f), ProceduralAudio.Footstep("sand1", 11, 0.65f) };
            _sfx[SfxSound.Jump] = new[] { ProceduralAudio.Whoosh("jump", 0.2f, 0.18f) };
            _sfx[SfxSound.Land] = new[] { ProceduralAudio.Footstep("land", 5, 0.7f) };
            _sfx[SfxSound.Splash] = new[] { ProceduralAudio.Splash("splash", 0.6f) };
            _sfx[SfxSound.Hurt] = new[] { ProceduralAudio.Chime("hurt", new[] { 180f, 140f }, 0.25f, 0.4f) };
            _sfx[SfxSound.Death] = new[] { ProceduralAudio.Boom("death", 1.6f) };

            _ambience[(int)AmbienceLayer.Wind].clip = ProceduralAudio.WindLoop("amb_wind", 6f);
            _ambience[(int)AmbienceLayer.Ocean].clip = ProceduralAudio.OceanLoop("amb_ocean", 8f);
            _ambience[(int)AmbienceLayer.Birds].clip = ProceduralAudio.BirdsLoop("amb_birds", 10f);
            _ambience[(int)AmbienceLayer.Night].clip = ProceduralAudio.CricketsLoop("amb_night", 6f);
            _ambience[(int)AmbienceLayer.Fire].clip = ProceduralAudio.FireLoop("amb_fire", 5f);
            _ready = true;
        }

        public void PlayUi(UiSound s)
        {
            if (!_ready || !_ui.TryGetValue(s, out AudioClip c)) return;
            _uiSource.PlayOneShot(c, Volume(App.Settings?.SfxVolume ?? 1f));
        }

        public void Play(SfxSound s, float volume = 1f, float pitch = 1f)
        {
            if (!_ready || !_sfx.TryGetValue(s, out AudioClip[] clips) || clips.Length == 0) return;
            AudioClip c = clips[Random.Range(0, clips.Length)];
            _sfxSource.pitch = pitch;
            _sfxSource.PlayOneShot(c, volume * Volume(App.Settings?.SfxVolume ?? 1f));
        }

        public void SetAmbience(AmbienceLayer layer, float volume) => _ambienceTarget[(int)layer] = Mathf.Clamp01(volume);

        public void StopAllAmbience()
        {
            for (int i = 0; i < _ambienceTarget.Length; i++) _ambienceTarget[i] = 0f;
        }

        private static float Volume(float channel) => channel * (App.Settings?.MasterVolume ?? 1f);

        private void Update()
        {
            if (!_ready) return;
            float music = Volume(App.Settings?.MusicVolume ?? 1f);
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _ambience.Length; i++)
            {
                AudioSource s = _ambience[i];
                float target = _ambienceTarget[i] * music;
                s.volume = Mathf.MoveTowards(s.volume, target, dt * 0.5f);
                if (s.volume > 0.001f && !s.isPlaying && s.clip != null)
                {
                    s.time = Random.Range(0f, s.clip.length * 0.9f);
                    s.Play();
                }
                else if (s.volume <= 0.001f && s.isPlaying)
                {
                    s.Stop();
                }
            }
        }
    }
}
