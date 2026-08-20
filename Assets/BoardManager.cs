using NUnit.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BoardManager : MonoBehaviour
{
    public GameObject applePrefab;

    public const int column = 17;
    public const int row = 10;

    public const float spacer = 0.8f;
    public const float appleSize = 0.7f;

    public Vector2 boardCenterPos;
    public GameObject[,] appleGrid; 
    private int[,] appleArray;

    // For Debugging 
    public Vector2 dragStartPos;
    public Vector2 dragEndPos; 

    public void GenerateBoard()
    {
        GenerateAppleArray(); 

        for (int i = 0; i < row; i++)
        {
            for (int j = 0; j < column; j++)
            {
                Vector2 position = new Vector2((j - (column - 1) / 2.0f)* spacer, -(i - (row - 1) / 2.0f) * spacer);
                GameObject apple = Instantiate(applePrefab, this.transform);
                apple.transform.localScale = new Vector3(appleSize, appleSize, 1);
                apple.transform.localPosition = position; 

                apple.GetComponent<Apple>().SetValue(appleArray[i, j]); // Set the value of the apple
                appleGrid[i, j] = apple;
            }
        }
    }

    private void GenerateAppleArray()
    {
        int sum = 0;
        do
        {
            sum = 0;
            for (int i = 0; i < row; i++)
            {
                for (int j = 0; j < column; j++)
                {
                    appleArray[i, j] = UnityEngine.Random.Range(1, 10); // Randomly assign 1 ~ 9 to each cell
                    sum += appleArray[i, j];
                }
            }
        } while (sum % 10 != 0); // Repeat until the sum is divisible by 10

        return ;
    }

    // Return Apples in Dragged Area
    public List<GameObject> GetApplesInDraggedArea(Vector2 dragStartPos, Vector2 dragEndPos)
    {
        // startPos와 endPos를 비교하여 Dragged Area의 leftTop과 rightBot을 구한다.
        Vector2 leftTop = dragStartPos, rightBot = dragEndPos; 
        if (dragStartPos.x > dragEndPos.x)
        {
            leftTop.x = dragEndPos.x;
            rightBot.x = dragStartPos.x;
        }

        if (dragStartPos.y < dragEndPos.y)
        {
            leftTop.y = dragEndPos.y;
            rightBot.y = dragStartPos.y;
        }

        // leftTop과 rightBot을 BoardManager 기준 LocalPosition으로 변환 
        leftTop = this.transform.InverseTransformPoint(leftTop);
        rightBot = this.transform.InverseTransformPoint(rightBot);

        List <GameObject> apples = new List<GameObject>();
        (int r1, int c1) = GetAppleGridIndex(leftTop);
        (int r2, int c2) = GetAppleGridIndex(rightBot);

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

    // Get Apple Index from Position
    private (int row, int column) GetAppleGridIndex(Vector2 pos)
    {
        int r = Mathf.CeilToInt(-pos.y / spacer + (row - 1) / 2.0f);
        int c = Mathf.CeilToInt(pos.x / spacer + (column - 1) / 2.0f);

        // 
        r = Mathf.Clamp(r, 0, row);
        c = Mathf.Clamp(c, 0, column);

        return (r, c);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        appleArray = new int[row, column];
        appleGrid = new GameObject[row, column];
        GenerateBoard();
    }

    // Update is called once per frame
    void Update()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            dragStartPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        }
        else if (Mouse.current.leftButton.isPressed)
        {
            Vector2 currentPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            List<GameObject> applesInDraggedArea = GetApplesInDraggedArea(dragStartPos, currentPos);
            foreach (GameObject apple in applesInDraggedArea)
            {
                apple.GetComponent<Apple>().SetSelected(true);
            }
        }
        else if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            dragEndPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            List<GameObject> applesInDraggedArea = GetApplesInDraggedArea(dragStartPos, dragEndPos);
            int selectedSum = 0;
            foreach (GameObject apple in applesInDraggedArea)
            {
                selectedSum += apple.GetComponent<Apple>().GetValue();
                apple.GetComponent<Apple>().SetSelected(false); 
            }
            Debug.Log($"Dragged Area: Start({dragStartPos}), End({dragEndPos}), Apples Count: {applesInDraggedArea.Count}, Selected Sum: {selectedSum}");

            // dragStartPos와 dragEndPos를 초기화하여 다음 Dragging을 준비
            dragStartPos = Vector2.zero;
            dragEndPos = Vector2.zero;
        }
    }
}
