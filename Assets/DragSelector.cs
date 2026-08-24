using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DragSelector : MonoBehaviour
{
    public event Action<Vector2, Vector2> OnDragging;
    public event Action<Vector2, Vector2> OnDragEnd;

    [SerializeField] private BoardManager boardManager; // for debugging 
    [SerializeField] private Transform selectionBoxUI; 

    private Vector2 startPos = Vector2.zero;
    private Vector2 endPos = Vector2.zero;
    private bool isDragging = false;

    private List<Apple> selectedApples = null;


    void Update()
    {
        // 이번 프레임에서 마우스가 클릭된 경우 (드래그 시작한 경우)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            HandleDragStart();
        }

        // 이전 프레임에서의 마우스의 클릭이 유지된 경우 (드래그 중인 경우) 
        else if (Mouse.current.leftButton.isPressed && isDragging)
        {
            HandleDragging();
        }

        // 이번 프레임에서 마우스의 클릭이 종료된 경우 (드래그 종료된 경우) 
        else if (Mouse.current.leftButton.wasReleasedThisFrame && isDragging)
        {
            HandleDragEnd();
        }
    }

    // 드래그 시작 시 처리할 로직
    private void HandleDragStart()
    {
        startPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue()); 
        isDragging = true;

        // Selection Box UI 활성화 및 위치 설정 
        selectionBoxUI.gameObject.SetActive(true);
        DrawSelectionBox(startPos, startPos); // 초기에는 시작점과 끝점이 같으므로 사각형이 보이지 않음
    }

    // 드래그 중일 때 처리할 로직
    private void HandleDragging()
    {
        // 우선 이전에 선택된 사과들의 하이라이트 끄기 
        if (selectedApples != null && selectedApples.Count > 0)
        {
            foreach (Apple apple in selectedApples)
            {
                apple.SetSelected(false);
            }
        } 

        endPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        DrawSelectionBox(startPos, endPos); 

        // 실제로는 이 부분에서 이벤트를 통해 현재 드래그 범위를 GameManager에게 전송
        selectedApples = boardManager.GetApplesInDraggedArea(startPos, endPos); 
        foreach (Apple apple in selectedApples)
        {
            apple.SetSelected(true); 
        }
        // 여기까지는 디버깅용 

        // 실제 코드 
        OnDragging?.Invoke(startPos, endPos);
        
    }

    // 드래그 종료 시 처리할 로직
    private void HandleDragEnd()
    {
        isDragging = false;
        selectionBoxUI.gameObject.SetActive(false);

        endPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());

        // 실제로는 이 부분에서 이벤트를 통해 현재 드래그 범위를 GameManager에게 전송 
        selectedApples = boardManager.GetApplesInDraggedArea(startPos, endPos);
        int selectedSum = 0;
        foreach (Apple apple in selectedApples) 
        { 
            selectedSum += apple.GetValue();
            apple.SetSelected(false);
        }

        Debug.Log($"Selected Apples Count: {selectedApples.Count}, Sum of Values: {selectedSum}");

        if (selectedSum == 10)
        {
            Debug.Log($"Selected Apples Sum is 10! Removing selected apples and you got {selectedApples.Count} points");
            boardManager.RemoveSelectedApples(selectedApples);
        }

        // 여기까지 디버깅용

        // 실제 코드 
        OnDragEnd?.Invoke(startPos, endPos); 

        // 드래그 종료 시 각 변수들 초기화 및 selectionBoxUI 비활성화
        startPos = Vector2.zero; 
        endPos = Vector2.zero;

        selectedApples.Clear();
    }

    private void DrawSelectionBox(Vector2 start, Vector2 end)
    {
        // Calculate the center and size of the selection box
        Vector2 center = (start + end) / 2f;
        Vector2 size = new Vector2(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));

        // Update the selection box UI's position and scale
        selectionBoxUI.position = center;
        selectionBoxUI.localScale = size;
    }
}
