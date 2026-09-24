using System;
using UnityEngine;
using System.Collections.Generic;

// 사용할 효과음들의 이름표 정의 
public enum SFXType
{
    ApplePop
}

// 인스펙터에서 enum과 오디오 클립을 짝지어줄 구조체 선언 
[Serializable] 
public struct SoundMapping
{
    public SFXType type;
    public AudioClip clip; 
}
public class AudioManager : MonoBehaviour
{
    // 싱글톤 패턴: 전역에서 단 하나만 존재하고 어디서든 접근 가능 
    public static AudioManager Instance { get; private set; }

    // 오디오 소스: 용도에 따라 분리 
    [Header("AudioSources")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;

    // 오디오 클립 (음원 파일) 
    [Header("BGM")]
    [SerializeField] private AudioClip mainBGM;

    [Header("SFX Mappings")]
    // 인스펙터에서 직접 짝지어주기 위한 리스트 
    [SerializeField] private List<SoundMapping> sfxMappings;

    // 실제 게임 중 빠른 검색을 위해 내부적으로 사용할 딕셔너리 
    private Dictionary<SFXType, AudioClip> sfxDictionary; 

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            InitializeDictionary();

            LoadVolume(); // 저장된 볼륨 불러오기 
        }
         
        else
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // BGM 재생 
    public void PlayBGM()
    {
        if (mainBGM != null)
        {
            bgmSource.clip = mainBGM;
            bgmSource.loop = true; 
            bgmSource.Play(); 
        }
    }

    // BGM 정지 
    public void StopBGM()
    {
        if (bgmSource == null) return; 
        bgmSource.Stop(); 
    }

    // BGM 일시 정지 
    public void PauseBGM()
    {
        if (bgmSource.isPlaying)
        {
            bgmSource.Pause(); 
        }
    }

    // BGM 일시 정지 해제 
    public void UnPauseBGM()
    {
        bgmSource.UnPause();
    }

    public void PlaySFX(SFXType type)
    {
        if (sfxDictionary.TryGetValue(type, out AudioClip clip))
        {
            sfxSource.PlayOneShot(clip); 
        }
        else
        {
            Debug.LogWarning($"[AudioManager] {type}에 연결된 오디오 클립이 없습니다."); 
        }
    }

    public void SetBGMVolume(float volume)
    {
        if (bgmSource != null) 
        { 
            bgmSource.volume = volume; 
        }
    }

    public void SetSFXVolume(float volume)
    {
        if (sfxSource != null)
        {
            sfxSource.volume = volume;
        }
    }

    private void InitializeDictionary()
    {
        sfxDictionary = new Dictionary<SFXType, AudioClip>();

        foreach (var mapping in sfxMappings)
        {
            // 중복된 키가 들어가는 것을 방지 
            if (!sfxDictionary.ContainsKey(mapping.type) && mapping.clip != null)
            {
                sfxDictionary.Add(mapping.type, mapping.clip);
            }
        }
    }

    private void LoadVolume()
    {
        float savedBGMVolume = PlayerPrefs.GetFloat(GameConstants.BGM_VOLUME_KEY, 1.0f);
        float savedSFXVolume = PlayerPrefs.GetFloat(GameConstants.SFX_VOLUME_KEY, 1.0f);
        if (bgmSource != null)
        {
            bgmSource.volume = savedBGMVolume; 
        }

        if (sfxSource != null)
        {
            sfxSource.volume = savedSFXVolume;
        }
    }
}
