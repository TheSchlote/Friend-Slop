using FriendSlop.Effects;
using Unity.Netcode;
using UnityEngine;

namespace FriendSlop.Round
{
    // Server-to-all-clients audio cue broadcast hub. MonoBehaviour systems
    // (MeteorShower, AnomalySpawner, future hazards) can't host their own
    // ClientRpcs because they aren't NetworkBehaviours — they route through
    // here instead. NetworkBehaviour systems already on a NetworkObject
    // (RoamingMonster, NetworkLootItem, NetworkFirstPersonController) call
    // AudioCue.Play directly from their NetworkVariable change handlers or
    // own [ClientRpc]s.
    //
    // Mirrors the existing TeleporterSoundClientRpc pattern in
    // RoundManager.PlayerPlacement.cs.
    public partial class RoundManager
    {
        // Call from server-only code. Fans out to every client (host
        // included, since the host is also a client). On a dedicated server
        // the host wouldn't hear it — Friend Slop is host-mode only so the
        // host always plays the cue.
        public void ServerBroadcastAudioCue(AudioCueId id, Vector3 position)
        {
            if (!IsServer || id == AudioCueId.None) return;
            BroadcastAudioCueClientRpc(id, position);
        }

        [ClientRpc]
        private void BroadcastAudioCueClientRpc(AudioCueId id, Vector3 position)
        {
            AudioCue.Play(id, position);
        }
    }
}
