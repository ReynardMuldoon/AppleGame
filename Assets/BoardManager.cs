using System;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

public class BoardManager : MonoBehaviour
{
    // 외부에서 참조할 필요가 있는지 검토 후 private으로 변경 
    public const int column = 17;
    public const int row = 10;
    public const float spacer = 0.8f;
    public const float appleSize = 0.7f;

    [SerializeField] private Apple applePrefab;

    private Apple[,] appleGrid; 
    private int[,] appleArray;

    // For Debugging 
    public Vector2 dragStartPos;
    public Vector2 dragEndPos;

    void Start()
    {
        appleArray = new int[row, column];
        appleGrid = new Apple[row, column];
        GenerateBoard();
    }

    // 디버깅을 위한 Update 함수 
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragStartPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        }
        else if (Mouse.current.leftButton.isPressed)
        {
            Vector2 currentPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            List<Apple> applesInDraggedArea = GetApplesInDraggedArea(dragStartPos, currentPos);
            foreach (Apple apple in applesInDraggedArea)
            {
                apple.SetSelected(true);
            }
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            dragEndPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            List<Apple> applesInDraggedArea = GetApplesInDraggedArea(dragStartPos, dragEndPos);
            int selectedSum = 0;
            foreach (Apple apple in applesInDraggedArea)
            {
                selectedSum += apple.GetValue();
                apple.SetSelected(false);
            }
            Debug.Log($"Dragged Area: Start({dragStartPos}), End({dragEndPos}), Apples Count: {applesInDraggedArea.Count}, Selected Sum: {selectedSum}");

            // dragStartPos와 dragEndPos를 초기화하여 다음 Dragging을 준비
            dragStartPos = Vector2.zero;
            dragEndPos = Vector2.zero;
        }
    }

    // 보드 생성 및 초기화 
    public void GenerateBoard()
    {
        GenerateAppleArray();

        for (int i = 0; i < row; i++)
        {
            float startX = -(column - 1) / 2.0f * spacer;
            float startY = (row - 1) / 2.0f * spacer;

            Vector3 appleScale = new Vector3(appleSize, appleSize, 1); 

            for (int j = 0; j < column; j++)
            {
                // 미리 계산한 시작점을 기준으로 현재 인덱스만큼 이동시킴 
                Vector2 position = new Vector2(startX + j * spacer, startY - i * spacer);

                // 오브젝트 생성 및 트랜스폼 설정 
                Apple apple = Instantiate(applePrefab, this.transform);
                apple.transform.localScale = appleScale;
                apple.transform.localPosition = position;

                apple.SetValue(appleArray[i, j]);
                appleGrid[i, j] = apple;
            }
        }
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
                if (appleGrid[i, j] != null)
                {
                    apples.Add(appleGrid[i, j]);
                }
            }
        }

        return apples;
    }

    private void GenerateAppleArray()
    {
        int sum = 0;

        for (int i = 0; i < row; i++)
        {
            for (int j = 0; j < column; j++)
            {
                appleArray[i, j] = UnityEngine.Random.Range(1, 10);
                sum += appleArray[i, j];
            }
        }

        int remainder = sum % 10;

        // 이미 10의 배수라면 아무것도 할 필요가 없음
        if (remainder != 0)
        {
            int randomRow, randomCol;

            // 단 하나의 예외 방어: 나머지 값과 똑같은 숫자를 가진 사과는 고르지 않는다
            do
            {
                randomRow = UnityEngine.Random.Range(0, row);
                randomCol = UnityEngine.Random.Range(0, column);
            } while (appleArray[randomRow, randomCol] == remainder);
            // 170개 중 나머지와 같은 숫자가 아닌 사과를 찾는 것은 보통 1번이면 끝

            int targetVal = appleArray[randomRow, randomCol] - remainder;

            if (targetVal <= 0)
            {
                targetVal += 10;
            }

            appleArray[randomRow, randomCol] = targetVal;
        }
    }

    // Vector2 좌표를 받아서 AppleGrid의 인덱스로 변환
    private (int rowIndex, int columnIndex) GetAppleGridIndex(Vector2 pos)
    {
        // GenerateBoard의 위치 공식을 역산하여 그리드 인덱스 계산
        int r = Mathf.CeilToInt(-pos.y / spacer + (row - 1) / 2.0f);
        int c = Mathf.CeilToInt(pos.x / spacer + (column - 1) / 2.0f);

        // 드래그 영역이 보드 외곽으로 나갔을 때 인덱스가 배열 범위를 벗어나지 않도록 안전하게 제한
        r = Mathf.Clamp(r, 0, row);
        c = Mathf.Clamp(c, 0, column);

        return (r, c);
    }
}
