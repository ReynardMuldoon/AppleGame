using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class GameOverUI : MonoBehaviour
{
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private TextMeshProUGUI scoreText;
    [SerializeField] private TextMeshProUGUI leftTimeText;

    [SerializeField] private Button returnToTitleButton;
    [SerializeField] private Button restartButton; 

    void Start()
    {
        returnToTitleButton.onClick.AddListener(GameManager.Instance.ReturnToTitle);
        restartButton.onClick.AddListener(GameManager.Instance.RestartGame);
    }

    public void GameOver(int score)
    {
        gameOverPanel.SetActive(true);
        scoreText.text = $"Score: {score}"; 
    }

    public void GameClear(int score, float leftTime)
    {
        GameOver(score);
        leftTimeText.text = $"Left Time: {leftTime}";
    }
}
