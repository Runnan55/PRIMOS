using System;
using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    public Sound[] musicSounds, sfxSounds;
    public AudioSource musicSource, sfxSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        PlayMusic("OfflineTheme");
    }

    public void PlayMusic(string name)
    {
        Sound s = Array.Find(musicSounds, x => x.name == name);

        if (s == null)
        {
            Debug.Log("Sound not found");
        }

        else
        {
            musicSource.clip = s.clip;
            musicSource.Play();
        }
    }

    public void PlaySFX(string name)
    {
        var matchingSounds = Array.FindAll(sfxSounds, s => s.name.StartsWith(name));

        if (matchingSounds.Length == 0)
        {
            Debug.LogWarning($"[AudioManager] No se encontraron SFX que comiencen con: {name}");
            return;
        }

        Sound selected = matchingSounds[UnityEngine.Random.Range(0, matchingSounds.Length)];

        sfxSource.PlayOneShot(selected.clip);
    }
}
