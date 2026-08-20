using UnityEngine;

public class Apple : MonoBehaviour
{
    [SerializeField]
    private int value; // The value of the apple

    public void SetValue(int newValue)
    {
        value = newValue;
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
