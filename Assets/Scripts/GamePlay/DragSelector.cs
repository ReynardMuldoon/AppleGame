using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class DragSelector : MonoBehaviour
{
    public event Action<Vector2, Vector2> OnDragging;
    public event Action<Vector2, Vector2> OnDragEnd;

    [SerializeField] private Transform selectionBoxUI; 

    private Vector2 startPos = Vector2.zero;
    private Vector2 endPos = Vector2.zero;
    private bool isDragging = false;

    private void OnDisable()
    {
        isDragging = false;
        if (selectionBoxUI != null) selectionBoxUI.gameObject.SetActive(false);
    }

    void Update()
    {
        if (Mouse.current == null || Camera.main == null || selectionBoxUI == null) return;
        // 이번 프레임에서 마우스가 클릭된 경우 (드래그 시작한 경우)
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            if (!MultiplayerHud.BlocksPointer(Mouse.current.position.ReadValue())) HandleDragStart();
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
        endPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        DrawSelectionBox(startPos, endPos); 

        OnDragging?.Invoke(startPos, endPos);
    }

    // 드래그 종료 시 처리할 로직
    private void HandleDragEnd()
    {
        isDragging = false;
        selectionBoxUI.gameObject.SetActive(false);

        endPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
 
        OnDragEnd?.Invoke(startPos, endPos);

        // 드래그 종료 시 각 변수들 초기화
        startPos = Vector2.zero; 
        endPos = Vector2.zero;
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
