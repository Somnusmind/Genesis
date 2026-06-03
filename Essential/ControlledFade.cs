using System.Collections;
using UnityEngine;
using UnityEngine.Audio;

public class ControlledFade : MonoBehaviour
{
    public AudioMixer mixer;
    public string exposedParameter;

    [Header("Visual Fade Settings")]
    [Tooltip("Assign the CanvasGroup of the UI/Visuals you want to fade in/out.")]
    public CanvasGroup visualFadeObject;

    [Header("Audio/Video Visualization")]
    [Tooltip(
        "Assign the CanvasGroup of the Audio/Video Visualization to fade together with the master."
    )]
    public CanvasGroup audioVideoCanvasGroup;

    public AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Coroutine currentFadeCoroutine;

    void Awake()
    {
        // Initialize: Ensure content starts invisible (Alpha 0) and Audio starts silent (-80)
        mixer.SetFloat(exposedParameter, -80.0f);

        if (visualFadeObject != null)
        {
            visualFadeObject.alpha = 0f;
            visualFadeObject.blocksRaycasts = false;
        }

        if (audioVideoCanvasGroup != null)
        {
            audioVideoCanvasGroup.alpha = 0f;
            audioVideoCanvasGroup.blocksRaycasts = false;
        }
    }


    public void FadeOut(float duration)
    {
        if (currentFadeCoroutine != null)
            StopCoroutine(currentFadeCoroutine);

        // Fade Out Audio (-80) and Visuals (Alpha 0)
        currentFadeCoroutine = StartCoroutine(FadeCoroutine(-80f, 0f, duration));
    }

    public void FadeIn(float duration)
    {
        Debug.Log("ControlledFade: FadeIn called with duration " + duration);
        if (currentFadeCoroutine != null)
            StopCoroutine(currentFadeCoroutine);

        // Fade In Audio (0) and Visuals (Alpha 1)
        currentFadeCoroutine = StartCoroutine(FadeCoroutine(0f, 1f, duration));
    }

    private IEnumerator FadeCoroutine(float targetVolume, float targetAlpha, float duration)
    {
        float startVolume;
        mixer.GetFloat(exposedParameter, out startVolume);

        float startAlpha = (visualFadeObject != null) ? visualFadeObject.alpha : 0f;
        float startAlphaAV = (audioVideoCanvasGroup != null) ? audioVideoCanvasGroup.alpha : 0f;

        float elapsedTime = 0f;

        // Enable interaction if we are fading IN
        if (targetAlpha > 0.9f)
        {
            if (visualFadeObject != null)
                visualFadeObject.blocksRaycasts = true;
            if (audioVideoCanvasGroup != null)
                audioVideoCanvasGroup.blocksRaycasts = true;
        }

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float normalizedProgress = elapsedTime / duration;

            // Apply animation curve
            float curveValue = fadeCurve.Evaluate(normalizedProgress);

            // Audio fade
            float currentVolume = Mathf.Lerp(startVolume, targetVolume, curveValue);
            mixer.SetFloat(exposedParameter, currentVolume);

            // Visual fade - Master panel / Flickering panel
            if (visualFadeObject != null)
            {
                visualFadeObject.alpha = Mathf.Lerp(startAlpha, targetAlpha, curveValue);
            }

            // Visual fade - Audio/Video Visualization
            if (audioVideoCanvasGroup != null)
            {
                audioVideoCanvasGroup.alpha = Mathf.Lerp(startAlphaAV, targetAlpha, curveValue);
            }

            yield return null;
        }

        mixer.SetFloat(exposedParameter, targetVolume);

        if (visualFadeObject != null)
        {
            visualFadeObject.alpha = targetAlpha;
            visualFadeObject.blocksRaycasts = (targetAlpha > 0.1f);
        }

        if (audioVideoCanvasGroup != null)
        {
            audioVideoCanvasGroup.alpha = targetAlpha;
            audioVideoCanvasGroup.blocksRaycasts = (targetAlpha > 0.1f);
        }

        Debug.Log("ControlledFade: Fade coroutine completed");
    }

    public void SetVolume(float volume)
    {
        mixer.SetFloat(exposedParameter, volume);
    }

    // Stops any active fade coroutine immediately
    public void StopFadeCoroutine()
    {
        if (currentFadeCoroutine != null)
        {
            StopCoroutine(currentFadeCoroutine);
            currentFadeCoroutine = null;
            Debug.Log("ControlledFade: Active fade coroutine stopped.");
        }
    }
}
