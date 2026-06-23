using System.Collections;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("SE")]
    [SerializeField] private AudioClip startupSE;
    [SerializeField] private AudioClip loopSE;
    [SerializeField] private AudioClip m5ConnectSE;

    [Header("Volume")]
    [SerializeField] [Range(0f, 1f)] private float startupVolume = 0.3f;
    [SerializeField] [Range(0f, 1f)] private float loopVolume = 0.3f;

    [Header("Loop Start")]
    [SerializeField] private float loopFadeInDuration = 1.0f; // ループのフェードイン時間

    private AudioSource startupSource;
    private AudioSource loopSource;
    private AudioSource oneShotSource;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        startupSource = gameObject.AddComponent<AudioSource>();
        startupSource.playOnAwake = false;
        startupSource.volume = startupVolume;

        loopSource = gameObject.AddComponent<AudioSource>();
        loopSource.playOnAwake = false;
        loopSource.loop = true;
        loopSource.volume = 0f;

        oneShotSource = gameObject.AddComponent<AudioSource>();
        oneShotSource.playOnAwake = false;
    }

    public void PlayM5ConnectSE()
    {
        if (m5ConnectSE != null)
            oneShotSource.PlayOneShot(m5ConnectSE);
    }

    void Start()
    {
        if (startupSE != null)
        {
            startupSource.clip = startupSE;
            startupSource.Play();

            if (loopSE != null)
                StartCoroutine(StartLoopWithFade(2.0f));
        }
        else if (loopSE != null)
        {
            StartCoroutine(StartLoopWithFade(0f));
        }
    }

    private IEnumerator StartLoopWithFade(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        loopSource.clip = loopSE;
        loopSource.volume = 0f;
        loopSource.Play();

        float elapsed = 0f;
        while (elapsed < loopFadeInDuration)
        {
            elapsed += Time.deltaTime;
            loopSource.volume = Mathf.Lerp(0f, loopVolume, elapsed / loopFadeInDuration);
            yield return null;
        }
        loopSource.volume = loopVolume;
    }
}
