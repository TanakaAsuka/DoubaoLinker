using System;
using UnityEngine;
using UnityEngine.UI;

public class Controller : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [SerializeField] Button closeButton;
    void Start()
    {
        closeButton.onClick.AddListener(BtnHandler);
    }

    private void BtnHandler()
    {
        Application.Quit();
    }

    private void OnDestroy()
    {
        closeButton.onClick.RemoveListener(BtnHandler);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
