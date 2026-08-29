using UnityEngine;
using UnityEngine.UI; 

public class VolumeUI : MonoBehaviour
{
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider; 

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (bgmVolumeSlider != null)
        {
            bgmVolumeSlider.onValueChanged.AddListener(AudioManager.Instance.SetBGMVolume);
        }

        if (sfxVolumeSlider != null)
        {
            sfxVolumeSlider.onValueChanged.AddListener(AudioManager.Instance.SetSFXVolume);
        }
    }
}
