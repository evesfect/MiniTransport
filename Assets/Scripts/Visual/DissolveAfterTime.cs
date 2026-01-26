using System.Collections;
using UnityEngine;

public class DissolveAfterTime : MonoBehaviour
{
    [Header("Timing")]
    public float delay = 5f;
    public float dissolveDuration = 1f;
    public AnimationCurve dissolveCurve = AnimationCurve.Linear(0, 0, 1, 1);

    [Header("Shader Control")]
    [Tooltip("Name of the dissolve float property in the shader")]
    public string dissolveProperty = "_Dissolve";

    [Tooltip("Value when fully visible")]
    public float visibleValue = 0f;

    [Tooltip("Value when fully dissolved")]
    public float dissolvedValue = 1f;

    [Header("Behavior")]
    public bool destroyOnComplete = true;

    Renderer[] renderers;
    MaterialPropertyBlock mpb;
    int dissolveID;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>();
        mpb = new MaterialPropertyBlock();
        dissolveID = Shader.PropertyToID(dissolveProperty);

        SetDissolve(visibleValue);
    }

    void OnEnable()
    {
        StartCoroutine(DissolveRoutine());
    }

    IEnumerator DissolveRoutine()
    {
        yield return new WaitForSeconds(delay);

        float t = 0f;
        while (t < dissolveDuration)
        {
            t += Time.deltaTime;
            float normalized = Mathf.Clamp01(t / dissolveDuration);
            float curveValue = dissolveCurve.Evaluate(normalized);
            float dissolve = Mathf.Lerp(visibleValue, dissolvedValue, curveValue);

            SetDissolve(dissolve);
            yield return null;
        }

        SetDissolve(dissolvedValue);

        if (destroyOnComplete)
            Destroy(gameObject);
    }

    void SetDissolve(float value)
    {
        foreach (var r in renderers)
        {
            r.GetPropertyBlock(mpb);
            mpb.SetFloat(dissolveID, value);
            r.SetPropertyBlock(mpb);
        }
    }
}
