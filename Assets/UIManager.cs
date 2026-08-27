using System;
using System.Collections;
using TMPro;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    // Countdown System 
    public Action OnCountdownEnd;
    [SerializeField] public GameObject countdownPanel;
    [SerializeField] private TextMeshProUGUI countdownText;

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
