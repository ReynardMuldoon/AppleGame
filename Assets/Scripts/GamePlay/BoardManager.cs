using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class BoardManager : MonoBehaviour
{
    public const float spacer = 0.8f;
    public const float appleSize = 0.7f;

    public int difficultyLevel = 0; // 난이도: 0~5 

    [SerializeField] private Apple applePrefab;

    private Apple[] appleGrid; 
    private int[] appleArray;

    private int estimatedMaxScore; // 현재 보드에서 제거 가능한 사과의 최대 개수 (근삿값)

    void Start()
    {
        appleArray = new int[GameConstants.ROW * GameConstants.COLUMN];
        appleGrid = new Apple[GameConstants.ROW * GameConstants.COLUMN];
    }

    public int[] GetAppleArray()
    {
        return appleArray; 
    }

    public void GenerateBoard(byte[] data)
    {
        for (int i = 0; i < GameConstants.ROW * GameConstants.COLUMN; i++)
        {
            appleArray[i] = (int)data[i];
        }

        for (int i = 0; i < GameConstants.ROW; i++)
        {
            float startX = -(GameConstants.COLUMN - 1) / 2.0f * spacer;
            float startY = (GameConstants.ROW - 1) / 2.0f * spacer;

            Vector3 appleScale = new Vector3(appleSize, appleSize, 1);

            for (int j = 0; j < GameConstants.COLUMN; j++)
            {
                // 미리 계산한 시작점을 기준으로 현재 인덱스만큼 이동시킴 
                Vector2 position = new Vector2(startX + j * spacer, startY - i * spacer);

                // 오브젝트 생성 및 트랜스폼 설정 
                Apple apple = Instantiate(applePrefab, this.transform);
                apple.transform.localScale = appleScale;
                apple.transform.localPosition = position;

                apple.SetValue(appleArray[i * GameConstants.COLUMN + j], i, j);
                appleGrid[i * GameConstants.COLUMN + j] = apple;
            }
        }

        estimatedMaxScore = AppleGameSolver.GetEstimatedMaxScore(appleArray);

        if (estimatedMaxScore <= 90) difficultyLevel = 5;
        else if (estimatedMaxScore <= 95) difficultyLevel = 4;
        else if (estimatedMaxScore <= 100) difficultyLevel = 3;
        else if (estimatedMaxScore <= 105) difficultyLevel = 2;
        else if (estimatedMaxScore <= 110) difficultyLevel = 1;
        else difficultyLevel = 0;

        Debug.Log($"Estimated Max Score: {estimatedMaxScore}, Difficulty Level: {difficultyLevel}");
    }

    // 드래그로 생성된 영역 내에 포함된 사과들의 List 반환 
    public List<Apple> GetApplesInDraggedArea(Vector2 dragStartPos, Vector2 dragEndPos)
    {
        // Mathf.Min / Max를 활용하여 조건문 없이 직관적으로 Min/Max 좌표 추출
        float minX = Mathf.Min(dragStartPos.x, dragEndPos.x);
        float maxX = Mathf.Max(dragStartPos.x, dragEndPos.x);

        float minY = Mathf.Min(dragStartPos.y, dragEndPos.y);
        float maxY = Mathf.Max(dragStartPos.y, dragEndPos.y);

        // 추출한 값으로 좌상단(Left-Top)과 우하단(Right-Bottom) 점 생성
        Vector2 leftTop = new Vector2(minX, maxY);
        Vector2 rightBot = new Vector2(maxX, minY);

        // leftTop과 rightBot을 BoardManager 기준 LocalPosition으로 변환 
        leftTop = this.transform.InverseTransformPoint(leftTop);
        rightBot = this.transform.InverseTransformPoint(rightBot);

        // 인덱스 추출 및 범위 내 사과 탐색 
        (int r1, int c1) = GetAppleGridIndex(leftTop);
        (int r2, int c2) = GetAppleGridIndex(rightBot);

        List<Apple> apples = new List<Apple>();

        // r1, c1이 row 또는 column일 경우 r2, c2 또한 row 또는 column이 되므로 이 경우 
        // 조건문에 의해 AppleGrid의 잘못된 범위에 접근하지 않을 수 있다. 
        for (int i = r1; i < r2; i++)
        {
            for (int j = c1; j < c2; j++)
            {
                if (appleGrid[i * GameConstants.COLUMN + j] != null)
                {
                    apples.Add(appleGrid[i * GameConstants.COLUMN + j]);
                }
            }
        }

        return apples;
    }

    public List<Apple> GetApplesByIndex(int r1, int r2, int c1, int c2)
    {
        List<Apple> apples = new List<Apple>();
        for (int r = r1; r <= r2; r++)
        {
            for (int c = c1; c <= c2; c++)
            {
                if (appleGrid[r * GameConstants.COLUMN + c] != null)
                {
                    apples.Add(appleGrid[r * GameConstants.COLUMN + c]);
                }
            }
        }
        return apples;
    }

    public void RemoveSelectedApples(List<Apple> selectedApples)
    {
        foreach (Apple apple in selectedApples)
        {
            (int row, int col) = apple.GetPosition();
            AudioManager.Instance.PlaySFX(SFXType.ApplePop); 
            Destroy(apple.gameObject);
            appleGrid[row * GameConstants.COLUMN + col] = null;
            appleArray[row * GameConstants.COLUMN + col] = 0; // 제거된 사과의 값을 0으로 설정
        }
    }

    // Vector2 좌표를 받아서 AppleGrid의 인덱스로 변환
    private (int rowIndex, int columnIndex) GetAppleGridIndex(Vector2 pos)
    {
        // GenerateBoard의 위치 공식을 역산하여 그리드 인덱스 계산
        int r = Mathf.CeilToInt(-pos.y / spacer + (GameConstants.ROW - 1) / 2.0f);
        int c = Mathf.CeilToInt(pos.x / spacer + (GameConstants.COLUMN - 1) / 2.0f);

        // 드래그 영역이 보드 외곽으로 나갔을 때 인덱스가 배열 범위를 벗어나지 않도록 안전하게 제한
        r = Mathf.Clamp(r, 0, GameConstants.ROW);
        c = Mathf.Clamp(c, 0, GameConstants.COLUMN);

        return (r, c);
    }
}
