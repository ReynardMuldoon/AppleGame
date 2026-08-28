using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI; 

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // Countdown System 
    public Action OnCountdownEnd;
    [SerializeField] public GameObject countdownPanel;
    [SerializeField] private TextMeshProUGUI countdownText;

    // Audio Volume 
    [SerializeField] private Slider bgmVolumeSlider;
    [SerializeField] private Slider sfxVolumeSlider;
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this; 
        }
        else
        {
            Destroy(gameObject); 
        }
    }

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

    public IEnumerator StartCountdown(int sec)
    {
        countdownPanel.SetActive(true); 

        for (int i = sec; i > 0; i--)
        {
            countdownText.text = $"{i}"; 
            yield return new WaitForSeconds(1);
        }

        OnCountdownEnd?.Invoke(); 
    }
}
