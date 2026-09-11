using UnityEngine;
using UnityEngine.UI; 
using UnityEngine.SceneManagement;
using TMPro;

public class TitleUI : MonoBehaviour
{
    [SerializeField] private Button gameStartButton;
    [SerializeField] private Button exitButton;
    [SerializeField] private TextMeshProUGUI bestScoreText; 

    void Start()
    {
        if (gameStartButton != null)
        {
            gameStartButton.onClick.AddListener(GameStart);
        }

        if (exitButton != null)
        {
            exitButton.onClick.AddListener(Exit);
        }

        // Load best score from PlayerPrefs
        int bestScore = PlayerPrefs.GetInt(GameConstants.BEST_SCORE_KEY, 0);
        if (bestScoreText != null)
        {
            bestScoreText.text = $"최고 기록: {bestScore}";
        }
    }

    private async void GameStart()
    {
        bool success = await NetworkManager.Instance.ConnectAsync(GameConstants.SERVER_IP, GameConstants.SERVER_PORT);
        if (NetworkManager.Instance.IsConnected)
        {
            SceneManager.LoadScene(GameConstants.GAME_SCENE);
        }
        else
        {
            // 연결 실패 시 처리 (예: 오류 메시지 표시)
        }
    }

    private void Exit()
    {
    #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
    #else
                Application.Quit();
    #endif
    }
}
