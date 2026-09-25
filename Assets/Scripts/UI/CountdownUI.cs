using TMPro;
using UnityEngine;
using System.Collections;
using System;

public class CountdownUI : MonoBehaviour
{
    [SerializeField] private GameObject countdownPanel;
    [SerializeField] private TextMeshProUGUI countdownText;

    public IEnumerator PlayCountdown(int sec, Action onComplete)
    {
        countdownPanel.SetActive(true);

        for (int i = sec; i > 0; i--)
        {
            countdownText.text = $"{i}";
            yield return new WaitForSeconds(1);
        }

        countdownPanel.SetActive(false);

        onComplete();
    }
}
