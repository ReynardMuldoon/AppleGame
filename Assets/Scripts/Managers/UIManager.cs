using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI; 

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [SerializeField] private CountdownUI countdownUI; 
    [SerializeField] private VolumeUI volumeUI;
    [SerializeField] private GameOverUI gameOverUI;

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

    public void StartCountdown(int sec, Action onComplete)
    {
        StartCoroutine(countdownUI.PlayCountdown(sec, onComplete));
    }
}
