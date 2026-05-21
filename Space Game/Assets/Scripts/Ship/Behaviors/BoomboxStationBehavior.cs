using FriendSlop.Player;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Ship
{
    // Static-station boombox (v1: no carrying yet). Server-authoritative
    // state machine: stopped -> playing track 0 -> playing track 1 -> ... ->
    // stopped. Each press cycles to the next state.
    //
    // Playback sync: when the server starts a track it records
    // NetworkManager.ServerTime as `_serverStartTime`; clients use
    // BoomboxPlayback.ComputeLocalPlaybackTime to drop their local
    // AudioSource head at the same offset so a late-joiner hears the same
    // moment in the track. The pattern mirrors the public boombox in lots
    // of Lethal-Company-style games.
    //
    // The boombox does NOT route through AudioCue — that layer is for short
    // gameplay cues (loot pickup, meteor warning) with a procedural
    // fallback. Looping music wants a real AudioClip on the GameObject and
    // a registry-of-music slot is overkill for v1. When `tracks` is empty
    // the boombox shows a placeholder prompt and stays silent — no
    // procedural fallback.
    [RequireComponent(typeof(AudioSource))]
    public class BoomboxStationBehavior : ShipStationBehavior
    {
        [SerializeField] private AudioClip[] tracks;
        [SerializeField] private float volume = 0.7f;

        // -1 = stopped.
        public NetworkVariable<int> TrackIndex = new(BoomboxPlayback.NoTrack);
        public NetworkVariable<bool> IsPlaying = new(false);
        public NetworkVariable<double> ServerStartTime = new(0d);

        private AudioSource _audioSource;

        private void Awake()
        {
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource != null)
            {
                _audioSource.loop = true;
                _audioSource.playOnAwake = false;
                _audioSource.spatialBlend = 1f; // 3D
            }
        }

        public override void OnHostNetworkSpawn(ShipStation host)
        {
            TrackIndex.OnValueChanged += OnTrackChanged;
            IsPlaying.OnValueChanged += OnIsPlayingChanged;
            ApplyLocalPlayback();
        }

        public override void OnHostNetworkDespawn(ShipStation host)
        {
            TrackIndex.OnValueChanged -= OnTrackChanged;
            IsPlaying.OnValueChanged -= OnIsPlayingChanged;

            if (_audioSource != null && _audioSource.isPlaying)
                _audioSource.Stop();
        }

        public override bool AllowsInteract(NetworkFirstPersonController player, ShipStation host)
        {
            // Boombox can be used by anyone — no occupancy gate.
            return player != null && !player.IsDead && !player.IsBeingCarried.Value;
        }

        public override string BuildPrompt(NetworkFirstPersonController player, ShipStation host)
        {
            if (host == null) return string.Empty;

            if (tracks == null || tracks.Length == 0)
            {
                return $"{host.DisplayName} (track slot empty)";
            }

            if (!IsPlaying.Value)
            {
                return $"E play {host.DisplayName}";
            }

            var idx = TrackIndex.Value;
            if (idx < 0 || idx >= tracks.Length)
            {
                return $"E next track on {host.DisplayName}";
            }

            var clip = tracks[idx];
            var label = clip != null ? clip.name : $"track {idx + 1}";
            return $"E next ({label}) on {host.DisplayName}";
        }

        public override void HandleInteractServer(ulong senderClientId, ShipStation host)
        {
            if (!IsServer) return;

            var trackCount = tracks != null ? tracks.Length : 0;
            if (trackCount == 0)
            {
                // No tracks authored — interact is a no-op.
                return;
            }

            if (!IsPlaying.Value)
            {
                TrackIndex.Value = 0;
                IsPlaying.Value = true;
                ServerStartTime.Value = NetworkManager.ServerTime.Time;
                return;
            }

            var next = BoomboxPlayback.NextTrack(TrackIndex.Value, trackCount);
            if (next == BoomboxPlayback.NoTrack)
            {
                IsPlaying.Value = false;
                TrackIndex.Value = BoomboxPlayback.NoTrack;
                return;
            }

            // Stop after cycling past the last track: if we just wrapped to
            // 0 (meaning current was the final track), stop instead of
            // looping the playlist.
            if (next == 0 && TrackIndex.Value == trackCount - 1)
            {
                IsPlaying.Value = false;
                TrackIndex.Value = BoomboxPlayback.NoTrack;
                return;
            }

            TrackIndex.Value = next;
            ServerStartTime.Value = NetworkManager.ServerTime.Time;
        }

        private void OnTrackChanged(int previous, int current)
        {
            ApplyLocalPlayback();
        }

        private void OnIsPlayingChanged(bool previous, bool current)
        {
            ApplyLocalPlayback();
        }

        private void ApplyLocalPlayback()
        {
            if (_audioSource == null) return;

            var trackCount = tracks != null ? tracks.Length : 0;
            var idx = TrackIndex.Value;

            if (!IsPlaying.Value || trackCount == 0 || idx < 0 || idx >= trackCount)
            {
                if (_audioSource.isPlaying) _audioSource.Stop();
                return;
            }

            var clip = tracks[idx];
            if (clip == null)
            {
                if (_audioSource.isPlaying) _audioSource.Stop();
                return;
            }

            _audioSource.clip = clip;
            _audioSource.volume = Mathf.Clamp01(volume);

            var now = NetworkManager != null
                ? NetworkManager.ServerTime.Time
                : ServerStartTime.Value;
            var offset = BoomboxPlayback.ComputeLocalPlaybackTime(
                ServerStartTime.Value, now, clip.length);
            _audioSource.time = (float)offset;

            if (!_audioSource.isPlaying)
                _audioSource.Play();
        }
    }
}
