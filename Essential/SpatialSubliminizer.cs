using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SpatialSubliminalizer : MonoBehaviour
{
    [Header("Settings")]
    public bool enableSpatialEffect = false;
    [Range(0, 1)]
    public float spatialBlendAmount = 0.8f;
    public float circleRadius = 5.0f;
    public float frequencyMultiplier = 1.0f;
    public float heightOffset = 0f;
    public Vector3 centerPosition = Vector3.zero;

    [Tooltip("Reference to BeatGenerator for synchronized rotation speed")]
    public BeatGenerator beatGenerator;

    private AudioSource audioSource;
    private float currentAngle = 0f;
    private float currentBeatFrequency = 8f;

    void Start()
    {
        // Get AudioSource reference if not assigned
        audioSource = GetComponent<AudioSource>();

        // Try to find BeatGenerator if not assigned
        if (beatGenerator == null)
        {
            beatGenerator = FindAnyObjectByType<BeatGenerator>();
            if (beatGenerator == null)
            {
                Debug.LogWarning("SpatialSubliminalizer: No BeatGenerator found. Using default frequency.");
            }
        }

        UpdateSpatialSettings();
    }

    void Update()
    {
        UpdateBeatFrequency();

        if (enableSpatialEffect)
        {
            // Calculate rotation based on current beat frequency and time
            float rotationSpeed = currentBeatFrequency * frequencyMultiplier;
            currentAngle += (rotationSpeed * Time.deltaTime) * 2f * Mathf.PI;

            // Keep angle within 2π range for consistency
            if (currentAngle > 2f * Mathf.PI)
            {
                currentAngle -= 2f * Mathf.PI;
            }

            // Calculate position on circle
            float x = Mathf.Sin(currentAngle) * circleRadius;
            float z = Mathf.Cos(currentAngle) * circleRadius;

            // Apply position
            transform.position = new Vector3(x, heightOffset, z) + centerPosition;
        }
        else
        {
            // Keep at center when disabled
            transform.position = centerPosition;
        }
    }

    public void LoadConfig()
    {
        Config config = ConfigManager.Instance.LoadConfiguration();
        enableSpatialEffect = config.enableSpatialSubliminizer;
        UpdateSpatialSettings();
    }

    // Updates the beat frequency from BeatGenerator
    private void UpdateBeatFrequency()
    {
        if (beatGenerator != null)
        {
            // Get the current beatFrequency from BeatGenerator
            currentBeatFrequency = beatGenerator.beatFrequency;
        }
    }

    // Call this when settings change
    public void UpdateSpatialSettings()
    {
        if (audioSource != null)
        {
            // Set spatial blend based on enabled state
            audioSource.spatialBlend = enableSpatialEffect ? spatialBlendAmount : 0f;

            // Configure audio source for 3D sound
            if (enableSpatialEffect)
            {
                // Configure 3D sound settings for subliminal effect
                // These values create a more diffuse spatial sound appropriate for subliminals
                audioSource.dopplerLevel = 0f;  // Disable doppler effect
                audioSource.rolloffMode = AudioRolloffMode.Custom;
                audioSource.maxDistance = circleRadius * 3f;
                audioSource.minDistance = circleRadius * 0.5f;
            }
        }
    }

    // Call this to toggle the effect
    public void ToggleSpatialEffect(bool enabled)
    {
        enableSpatialEffect = enabled;
        UpdateSpatialSettings();
    }

    void OnValidate()
    {
        UpdateSpatialSettings();
    }
}
