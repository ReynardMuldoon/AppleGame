using UnityEngine;
using UnityEngine.UI; 

public class VolumeUI : MonoBehaviour
{
    // PlayerPefs에 사용할 고유 키값 
    private const string BGM_VOLUME_KEY = "BGM_Volume";
    private const string SFX_VOLUME_KEY = "SFX_Volume";

    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider; 

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (bgmVolumeSlider != null)
        {
            bgmVolumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(BGM_VOLUME_KEY, 1.0f));
            bgmVolumeSlider.onValueChanged.AddListener(AudioManager.Instance.SetBGMVolume);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(SFX_VOLUME_KEY, 1.0f));
            sfxVolumeSlider.onValueChanged.AddListener(AudioManager.Instance.SetSFXVolume);
        }
    }
}
