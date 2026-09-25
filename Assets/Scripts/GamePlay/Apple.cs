using TMPro;
using UnityEngine;

public class Apple : MonoBehaviour
{
    [SerializeField]
    private int value; // The value of the apple
    [SerializeField]
    private TextMeshPro valueText;
    [SerializeField]
    private SpriteRenderer backGround;
    [SerializeField]
    private SpriteRenderer hintEffect;

    private bool isSelected = false; // 드래그로 선택되었는지 
    private bool isHinted = false; // 힌트로 선택되었는지 

    private int rowIndex;
    private int columnIndex;

    public int GetValue()
    {
        return value;
    }

    public int GetIndex()
    {
        return rowIndex * GameConstants.COLUMN + columnIndex;
    }

    public (int row, int col) GetPosition()
    {
        return (rowIndex, columnIndex);
    }

    public void SetValue(int newValue, int row, int col)
    {
        value = newValue;
        valueText.text = value.ToString();
        isSelected = true;
        rowIndex = row;
        columnIndex = col;
    }

    public void SetSelected(bool selected)
    {
        isSelected = selected;
        backGround.color = selected ? new Color(1, 1, 0, 1) : new Color(1, 1, 0, 0);
    }

    public void SetHinted(bool hinted)
    {
        isHinted = hinted;
        hintEffect.gameObject.SetActive(hinted);
    }
}
