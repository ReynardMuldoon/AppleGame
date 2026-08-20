using System;
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

    public int GetValue()
    {
        return value;
    }

    public void SetValue(int newValue)
    {
        value = newValue;
        valueText.text = value.ToString();
        backGround.color = Color.white;
        isSelected = true;
    }
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        backGround.color = selected ? Color.yellow : Color.white; // Change color based on selection
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
