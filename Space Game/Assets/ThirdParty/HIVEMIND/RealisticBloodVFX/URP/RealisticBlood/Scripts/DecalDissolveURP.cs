// URPDecalDissolver.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class URPDecalDissolver : MonoBehaviour
{
    public float minDelay = 0.5f;
    public float maxDelay = 1.5f;
    public float dissolveDuration = 1.5f;

    private float timer = 0f;
    private float dissolveDelay;
    private bool startedDissolve = false;

    private List<Material> decalMaterials = new List<Material>();

    void Start()
    {
        dissolveDelay = Random.Range(minDelay, maxDelay);

        var projectors = GetComponentsInChildren<DecalProjector>();

        foreach (var projector in projectors)
        {
            Material original = projector.material;
            if (original == null || !original.HasProperty("_Dissolve")) continue;

            Material instanced = Instantiate(original);
            instanced.SetFloat("_Dissolve", 0f);
            projector.material = instanced;

            decalMaterials.Add(instanced);
        }
    }

    void Update()
    {
        if (decalMaterials.Count == 0) return;

        timer += Time.deltaTime;

        if (!startedDissolve && timer >= dissolveDelay)
        {
            startedDissolve = true;
            timer = 0f;
        }

        if (startedDissolve)
        {
            float value = Mathf.Clamp01(timer / dissolveDuration);

            foreach (Material mat in decalMaterials)
            {
                if (mat != null)
                    mat.SetFloat("_Dissolve", value);
            }

            if (value >= 1f)
                Destroy(gameObject);
        }
    }
}
