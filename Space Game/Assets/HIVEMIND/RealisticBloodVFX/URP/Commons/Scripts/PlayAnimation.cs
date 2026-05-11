using UnityEditor;
using UnityEngine;

namespace RealisticBlood.Animation
{
    [ExecuteInEditMode]
    public class PlayAnimation : MonoBehaviour
    {
        public AnimationClip animationClip;
        public GameObject targetObject;

        private float timer = 0f;
        public float animDuration = 2.0f;
        public float animSpeed = 1.0f;

        public bool movePosition = false;
        public GameObject objectToMove;
        public Vector3 startPosition;
        public Vector3 endPosition;

        public AnimationCurve curveAnim;

        void Update()
        {
#if UNITY_EDITOR
            if (animationClip == null || targetObject == null)
                return;

            // Advance timer
            timer += Time.deltaTime * animSpeed;
            float t = Mathf.Clamp01(timer / animDuration);
            if (t >= 1f)
                timer = 0f;

            float sampleTime = curveAnim.Evaluate(t) * animationClip.length;

            if (!Application.isPlaying)
            {
                // Editor mode preview (Edit mode)
                AnimationMode.StartAnimationMode();
                AnimationMode.SampleAnimationClip(targetObject, animationClip, sampleTime);
            }
            else
            {
                // Play mode sample
                animationClip.SampleAnimation(targetObject, sampleTime);
            }

            if (movePosition && objectToMove != null)
            {
                objectToMove.transform.localPosition = Vector3.Lerp(startPosition, endPosition, curveAnim.Evaluate(t));
            }
#endif
        }

        private void OnDisable()
        {
#if UNITY_EDITOR
            AnimationMode.StopAnimationMode();
#endif
        }

        // Optional: for animation events like "OnFootstep"
        private void OnFootstep() { }
    }
}
