using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Transform))]
public class InfiniteMoveWithRandomStopInterval : MonoBehaviour
{
    [Header("Movement Settings")]
    [Tooltip("How fast the character moves forward.")]
    public float moveSpeed = 3f;

    [Header("Move Duration Settings")]
    [Tooltip("Minimum time (in seconds) the character moves before stopping.")]
    public float minMoveDuration = 4f;

    [Tooltip("Maximum time (in seconds) the character moves before stopping.")]
    public float maxMoveDuration = 6f;

    [Header("Stop Settings")]
    [Tooltip("How long (in seconds) the character stays still before moving again.")]
    public float stopDuration = 2f;

    private bool isStopping = false;

    private void Start()
    {
        StartCoroutine(MoveStopRoutine());
    }

    private void Update()
    {
        if (!isStopping)
        {
            // Move the character forward in world space
            transform.Translate(Vector3.forward * moveSpeed * Time.deltaTime);
        }
    }

    private IEnumerator MoveStopRoutine()
    {
        while (true)
        {
            // Move phase
            float moveTime = Random.Range(minMoveDuration, maxMoveDuration);
            isStopping = false;
            yield return new WaitForSeconds(moveTime);

            // Stop phase
            isStopping = true;
            yield return new WaitForSeconds(stopDuration);
        }
    }
}
