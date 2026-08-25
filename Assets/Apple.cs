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

    private bool isSelected = false; // Whether the apple is selected

    private int rowIndex;
    private int columnIndex; 

    public int GetValue()
    {
        return value;
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
}
